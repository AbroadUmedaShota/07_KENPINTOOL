using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace KenpinTool.Prototype;

public sealed class DatabaseService
{
    private const int CurrentSchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
    };

    private readonly string _primaryPath;
    private readonly string _fallbackPath;

    public DatabaseService(string primaryDbPath, string fallbackDbPath)
    {
        if (string.IsNullOrWhiteSpace(primaryDbPath))
        {
            throw new ArgumentException("Primary DB path is required.", nameof(primaryDbPath));
        }

        if (string.IsNullOrWhiteSpace(fallbackDbPath))
        {
            throw new ArgumentException("Fallback DB path is required.", nameof(fallbackDbPath));
        }

        _primaryPath = primaryDbPath;
        _fallbackPath = fallbackDbPath;
        ActivePath = primaryDbPath;
    }

    public string ActivePath { get; private set; }
    public bool IsFallback { get; private set; }

    public void Initialize()
    {
        ActivePath = ResolveDbPath();
        IsFallback = !string.Equals(ActivePath, _primaryPath, StringComparison.OrdinalIgnoreCase);

        var dir = Path.GetDirectoryName(ActivePath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        ExecuteNonQuery(connection, transaction, """
            CREATE TABLE IF NOT EXISTS SchemaVersion (
                Version INTEGER NOT NULL
            );
            """);

        var version = GetSchemaVersion(connection, transaction);
        if (version == 0)
        {
            ApplySchemaVersion1(connection, transaction);
            SetSchemaVersion(connection, transaction, CurrentSchemaVersion);
        }
        else if (version < CurrentSchemaVersion)
        {
            ApplyMigrations(connection, transaction, version, CurrentSchemaVersion);
            SetSchemaVersion(connection, transaction, CurrentSchemaVersion);
        }

        transaction.Commit();
    }

    public int GetOrCreateCase(string caseName, string inputPath, string ruleset, string status)
    {
        using var connection = OpenConnection();
        // ... (existing implementation)
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT Id FROM Cases WHERE InputPath = $inputPath";
            cmd.Parameters.AddWithValue("$inputPath", inputPath);
            var existing = cmd.ExecuteScalar();
            if (existing is long id)
            {
                return (int)id;
            }
        }

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO Cases (CaseName, InputPath, Ruleset, Status, OpenedAtUtc)
                VALUES ($caseName, $inputPath, $ruleset, $status, $openedAtUtc);
                SELECT last_insert_rowid();
                """;
            cmd.Parameters.AddWithValue("$caseName", caseName);
            cmd.Parameters.AddWithValue("$inputPath", inputPath);
            cmd.Parameters.AddWithValue("$ruleset", ruleset);
            cmd.Parameters.AddWithValue("$status", status);
            cmd.Parameters.AddWithValue("$openedAtUtc", DateTimeOffset.UtcNow.ToString("O"));

            var id = cmd.ExecuteScalar();
            return Convert.ToInt32(id, CultureInfo.InvariantCulture);
        }
    }

    public List<CaseRecord> GetCases()
    {
        var result = new List<CaseRecord>();
        using var connection = OpenConnection();
        
        // 1. Get Cases
        var cases = new List<(int Id, string Name, string Path, string Status, DateTimeOffset OpenedAt)>();
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT Id, CaseName, InputPath, Status, OpenedAtUtc FROM Cases ORDER BY Id DESC";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var id = reader.GetInt32(0);
                var name = reader.GetString(1);
                var path = reader.GetString(2);
                var status = reader.GetString(3);
                var openedAtText = reader.GetString(4);
                var openedAt = DateTimeOffset.TryParse(openedAtText, out var dt) ? dt : DateTimeOffset.UtcNow;
                cases.Add((id, name, path, status, openedAt));
            }
        }

        // 2. Get Folders for each case
        foreach (var c in cases)
        {
            var folders = new List<FolderRecord>();
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "SELECT FilePath FROM Pages WHERE CaseId = $caseId";
                cmd.Parameters.AddWithValue("$caseId", c.Id);
                using var reader = cmd.ExecuteReader();
                
                var filePaths = new List<string>();
                while (reader.Read())
                {
                    filePaths.Add(reader.GetString(0));
                }

                // Group by directory
                var grouped = filePaths
                    .Select(p => Path.GetDirectoryName(p) ?? "Unknown")
                    .GroupBy(p => p)
                    .Select(g => new FolderRecord(g.Key, g.Count()))
                    .OrderBy(f => f.Path)
                    .ToList();
                
                folders.AddRange(grouped);
            }

            result.Add(new CaseRecord(c.Id, c.Name, c.Path, c.Status, c.OpenedAt, folders));
        }

        return result;
    }

    public List<PageItem> GetPages(int caseId)
    {
        var result = new List<PageItem>();
        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT PageIndex, FilePath, PdfPageIndex FROM Pages WHERE CaseId = $caseId ORDER BY PageIndex";
        cmd.Parameters.AddWithValue("$caseId", caseId);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var index = reader.GetInt32(0);
            var path = reader.GetString(1);
            var pdfPage = reader.IsDBNull(2) ? (int?)null : reader.GetInt32(2);
            
            result.Add(new PageItem(index, path, Array.Empty<Detection>(), pdfPage));
        }

        return result;
    }

    public Dictionary<int, int> UpsertPages(int caseId, IReadOnlyList<PageItem> pages)
    {
        var result = new Dictionary<int, int>(pages.Count);
        var hashCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        foreach (var page in pages)
        {
            var fileHash = "";
            if (File.Exists(page.FilePath))
            {
                if (!hashCache.TryGetValue(page.FilePath, out fileHash))
                {
                    fileHash = ComputeFileHash(page.FilePath);
                    hashCache[page.FilePath] = fileHash;
                }
            }

            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = """
                    INSERT INTO Pages (CaseId, PageIndex, FilePath, PdfPageIndex, FileHash)
                    VALUES ($caseId, $pageIndex, $filePath, $pdfPageIndex, $fileHash)
                    ON CONFLICT(CaseId, PageIndex) DO UPDATE SET
                        FilePath = excluded.FilePath,
                        PdfPageIndex = excluded.PdfPageIndex,
                        FileHash = excluded.FileHash;
                    """;
                cmd.Parameters.AddWithValue("$caseId", caseId);
                cmd.Parameters.AddWithValue("$pageIndex", page.Index);
                cmd.Parameters.AddWithValue("$filePath", page.FilePath);
                cmd.Parameters.AddWithValue("$pdfPageIndex", (object?)page.PdfPageIndex ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$fileHash", string.IsNullOrWhiteSpace(fileHash) ? DBNull.Value : fileHash);
                cmd.ExecuteNonQuery();
            }

            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = "SELECT Id FROM Pages WHERE CaseId = $caseId AND PageIndex = $pageIndex";
                cmd.Parameters.AddWithValue("$caseId", caseId);
                cmd.Parameters.AddWithValue("$pageIndex", page.Index);
                var id = cmd.ExecuteScalar();
                result[page.Index] = Convert.ToInt32(id, CultureInfo.InvariantCulture);
            }
        }

        transaction.Commit();
        return result;
    }

    public Dictionary<int, List<Detection>> LoadDetections(int caseId)
    {
        var result = new Dictionary<int, List<Detection>>();
        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();

        cmd.CommandText = """
            SELECT p.PageIndex,
                   d.Code,
                   d.Name,
                   d.Level,
                   d.SuggestedAction,
                   d.ReworkType,
                   d.Confidence,
                   d.EvidenceJson
              FROM Detections d
              JOIN Pages p ON p.Id = d.PageId
             WHERE p.CaseId = $caseId
             ORDER BY p.PageIndex;
            """;
        cmd.Parameters.AddWithValue("$caseId", caseId);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var pageIndex = reader.GetInt32(0);
            if (!result.TryGetValue(pageIndex, out var list))
            {
                list = new List<Detection>();
                result[pageIndex] = list;
            }

            var code = reader.GetString(1);
            var name = reader.GetString(2);
            var levelText = reader.GetString(3);
            var suggestedText = reader.GetString(4);
            var reworkText = reader.GetString(5);
            var confidence = reader.IsDBNull(6) ? (double?)null : reader.GetDouble(6);
            var evidenceJson = reader.IsDBNull(7) ? null : reader.GetString(7);

            var level = Enum.TryParse<NgLevel>(levelText, out var parsedLevel) ? parsedLevel : NgLevel.NgA;
            var suggested = Enum.TryParse<SuggestedAction>(suggestedText, out var parsedSuggested) ? parsedSuggested : SuggestedAction.Rescan;
            var rework = Enum.TryParse<ReworkType>(reworkText, out var parsedRework) ? parsedRework : ReworkType.None;

            var evidence = string.IsNullOrWhiteSpace(evidenceJson)
                ? Array.Empty<EvidenceRegion>()
                : JsonSerializer.Deserialize<EvidenceRegion[]>(evidenceJson, JsonOptions) ?? Array.Empty<EvidenceRegion>();

            list.Add(new Detection(code, name, level, suggested, rework, confidence, evidence));
        }

        return result;
    }

    public Dictionary<int, PageDecision> LoadDecisions(int caseId)
    {
        var result = new Dictionary<int, PageDecision>();
        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();

        cmd.CommandText = """
            SELECT p.PageIndex,
                   d.Action,
                   d.TimestampUtc,
                   d.ExceptionReasonCode,
                   d.ExceptionNote
              FROM Decisions d
              JOIN Pages p ON p.Id = d.PageId
             WHERE p.CaseId = $caseId
             ORDER BY p.PageIndex;
            """;
        cmd.Parameters.AddWithValue("$caseId", caseId);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var pageIndex = reader.GetInt32(0);
            var actionText = reader.GetString(1);
            var timestampText = reader.GetString(2);
            var reasonCode = reader.IsDBNull(3) ? null : reader.GetString(3);
            var note = reader.IsDBNull(4) ? null : reader.GetString(4);

            if (!Enum.TryParse<DecisionAction>(actionText, out var action))
            {
                continue;
            }

            if (!DateTimeOffset.TryParse(timestampText, out var timestamp))
            {
                timestamp = DateTimeOffset.UtcNow;
            }

            result[pageIndex] = new PageDecision(action, timestamp, reasonCode, note);
        }

        return result;
    }

    public void SaveDetections(int pageId, IReadOnlyList<Detection> detections)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        using (var deleteCmd = connection.CreateCommand())
        {
            deleteCmd.Transaction = transaction;
            deleteCmd.CommandText = "DELETE FROM Detections WHERE PageId = $pageId";
            deleteCmd.Parameters.AddWithValue("$pageId", pageId);
            deleteCmd.ExecuteNonQuery();
        }

        foreach (var detection in detections)
        {
            using var insertCmd = connection.CreateCommand();
            insertCmd.Transaction = transaction;
            insertCmd.CommandText = """
                INSERT INTO Detections
                    (PageId, Code, Name, Level, SuggestedAction, ReworkType, Confidence, EvidenceJson)
                VALUES
                    ($pageId, $code, $name, $level, $suggestedAction, $reworkType, $confidence, $evidenceJson);
                """;
            insertCmd.Parameters.AddWithValue("$pageId", pageId);
            insertCmd.Parameters.AddWithValue("$code", detection.Code);
            insertCmd.Parameters.AddWithValue("$name", detection.Name);
            insertCmd.Parameters.AddWithValue("$level", detection.Level.ToString());
            insertCmd.Parameters.AddWithValue("$suggestedAction", detection.SuggestedAction.ToString());
            insertCmd.Parameters.AddWithValue("$reworkType", detection.ReworkType.ToString());
            insertCmd.Parameters.AddWithValue("$confidence", detection.Confidence is null ? DBNull.Value : detection.Confidence);
            var evidenceJson = detection.Evidence.Count == 0 ? null : JsonSerializer.Serialize(detection.Evidence, JsonOptions);
            insertCmd.Parameters.AddWithValue("$evidenceJson", string.IsNullOrWhiteSpace(evidenceJson) ? DBNull.Value : evidenceJson);
            insertCmd.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public void SaveDecision(int pageId, PageDecision decision)
    {
        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Decisions (PageId, Action, TimestampUtc, ExceptionReasonCode, ExceptionNote)
            VALUES ($pageId, $action, $timestampUtc, $exceptionReasonCode, $exceptionNote)
            ON CONFLICT(PageId) DO UPDATE SET
                Action = excluded.Action,
                TimestampUtc = excluded.TimestampUtc,
                ExceptionReasonCode = excluded.ExceptionReasonCode,
                ExceptionNote = excluded.ExceptionNote;
            """;
        cmd.Parameters.AddWithValue("$pageId", pageId);
        cmd.Parameters.AddWithValue("$action", decision.Action.ToString());
        cmd.Parameters.AddWithValue("$timestampUtc", decision.TimestampUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$exceptionReasonCode", (object?)decision.ExceptionReasonCode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$exceptionNote", (object?)decision.ExceptionNote ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public void DeleteDecision(int pageId)
    {
        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM Decisions WHERE PageId = $pageId";
        cmd.Parameters.AddWithValue("$pageId", pageId);
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection($"Data Source={ActivePath}");
        connection.Open();
        return connection;
    }

    private static void ExecuteNonQuery(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static string ComputeFileHash(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private string ResolveDbPath()
    {
        if (CanWriteToPath(_primaryPath))
        {
            return _primaryPath;
        }

        return _fallbackPath;
    }

    private static bool CanWriteToPath(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(dir))
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(dir);

            if (File.Exists(path))
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReadOnly) != 0)
                {
                    return false;
                }

                using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                return true;
            }

            var testPath = Path.Combine(dir, $".kenpin_write_test_{Guid.NewGuid():N}");
            using (var stream = new FileStream(testPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.WriteByte(0);
            }

            File.Delete(testPath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static int GetSchemaVersion(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "SELECT Version FROM SchemaVersion LIMIT 1";
        var result = cmd.ExecuteScalar();
        if (result is long version)
        {
            return (int)version;
        }

        return 0;
    }

    private static void SetSchemaVersion(SqliteConnection connection, SqliteTransaction transaction, int version)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "DELETE FROM SchemaVersion";
        cmd.ExecuteNonQuery();

        using var insertCmd = connection.CreateCommand();
        insertCmd.Transaction = transaction;
        insertCmd.CommandText = "INSERT INTO SchemaVersion (Version) VALUES ($version)";
        insertCmd.Parameters.AddWithValue("$version", version);
        insertCmd.ExecuteNonQuery();
    }

    private static void ApplyMigrations(SqliteConnection connection, SqliteTransaction transaction, int fromVersion, int toVersion)
    {
        var current = fromVersion;
        while (current < toVersion)
        {
            var next = current + 1;
            switch (next)
            {
                case 1:
                    ApplySchemaVersion1(connection, transaction);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported schema version: {next}");
            }

            current = next;
        }
    }

    private static void ApplySchemaVersion1(SqliteConnection connection, SqliteTransaction transaction)
    {
        ExecuteNonQuery(connection, transaction, """
            CREATE TABLE IF NOT EXISTS Cases (
                Id INTEGER PRIMARY KEY,
                CaseName TEXT NOT NULL,
                InputPath TEXT NOT NULL,
                Ruleset TEXT NOT NULL,
                Status TEXT NOT NULL,
                OpenedAtUtc TEXT NOT NULL
            );
            """);

        ExecuteNonQuery(connection, transaction, """
            CREATE UNIQUE INDEX IF NOT EXISTS IX_Cases_InputPath
            ON Cases(InputPath);
            """);

        ExecuteNonQuery(connection, transaction, """
            CREATE TABLE IF NOT EXISTS Pages (
                Id INTEGER PRIMARY KEY,
                CaseId INTEGER NOT NULL,
                PageIndex INTEGER NOT NULL,
                FilePath TEXT NOT NULL,
                PdfPageIndex INTEGER,
                FileHash TEXT,
                FOREIGN KEY (CaseId) REFERENCES Cases(Id) ON DELETE CASCADE,
                UNIQUE (CaseId, PageIndex)
            );
            """);

        ExecuteNonQuery(connection, transaction, """
            CREATE TABLE IF NOT EXISTS Detections (
                Id INTEGER PRIMARY KEY,
                PageId INTEGER NOT NULL,
                Code TEXT NOT NULL,
                Name TEXT NOT NULL,
                Level TEXT NOT NULL,
                SuggestedAction TEXT NOT NULL,
                ReworkType TEXT NOT NULL,
                Confidence REAL,
                EvidenceJson TEXT,
                FOREIGN KEY (PageId) REFERENCES Pages(Id) ON DELETE CASCADE
            );
            """);

        ExecuteNonQuery(connection, transaction, """
            CREATE INDEX IF NOT EXISTS IX_Detections_PageId
            ON Detections(PageId);
            """);

        ExecuteNonQuery(connection, transaction, """
            CREATE TABLE IF NOT EXISTS Decisions (
                PageId INTEGER PRIMARY KEY,
                Action TEXT NOT NULL,
                TimestampUtc TEXT NOT NULL,
                ExceptionReasonCode TEXT,
                ExceptionNote TEXT,
                FOREIGN KEY (PageId) REFERENCES Pages(Id) ON DELETE CASCADE
            );
            """);
    }
}

public sealed record CaseRecord(int Id, string Name, string InputPath, string Status, DateTimeOffset OpenedAtUtc, IReadOnlyList<FolderRecord> Folders);

public sealed record FolderRecord(string Path, int PageCount);
