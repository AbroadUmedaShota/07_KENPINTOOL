using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KenpinTool.Prototype;

public sealed class AuditLogWriter : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
    };

    private readonly StreamWriter _writer;
    private string _previousHash = "0";

    public AuditLogWriter(string auditLogPath)
    {
        var dir = Path.GetDirectoryName(auditLogPath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        if (File.Exists(auditLogPath))
        {
            var lastLine = ReadLastLine(auditLogPath);
            if (!string.IsNullOrWhiteSpace(lastLine))
            {
                try
                {
                    var logLine = JsonSerializer.Deserialize<LogLine>(lastLine, JsonOptions);
                    if (!string.IsNullOrWhiteSpace(logLine?.Hash))
                    {
                        _previousHash = logLine.Hash;
                    }
                }
                catch
                {
                    // Ignore parse failures and keep default hash.
                }
            }
        }

        _writer = new StreamWriter(
            new FileStream(auditLogPath, FileMode.Append, FileAccess.Write, FileShare.Read),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private static string? ReadLastLine(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        string? line;
        string? lastLine = null;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            lastLine = line;
        }

        return lastLine;
    }

    public void Append(string type, object data)
    {
        var payload = new
        {
            tsUtc = DateTimeOffset.UtcNow,
            type,
            data,
        };

        var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
        var hash = ComputeHash(_previousHash, payloadJson);

        var line = new
        {
            prev = _previousHash,
            hash,
            payload,
        };

        var lineJson = JsonSerializer.Serialize(line, JsonOptions);
        _writer.WriteLine(lineJson);
        _writer.Flush();

        _previousHash = hash;
    }

    private static string ComputeHash(string previousHash, string payloadJson)
    {
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes($"{previousHash}\n{payloadJson}");
        var hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private sealed class LogLine
    {
        public string? Prev { get; set; }
        public string? Hash { get; set; }
    }

    public void Dispose()
    {
        _writer.Dispose();
    }
}

