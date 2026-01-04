using System;
using System.IO;

namespace KenpinTool.Prototype;

public sealed record RunContext(
    string CaseName,
    string InputFolderPath,
    string OutputDirectory,
    string AuditLogPath,
    string CsvPath,
    string CaseJsonPath
)
{
    public static RunContext Create(string inputFolderPath)
    {
        var baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KenpinTool.Prototype",
            "runs");

        var caseName = Path.GetFileName(Path.TrimEndingDirectorySeparator(inputFolderPath));
        var safeCaseName = string.Concat(caseName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));

        var runDirName = $"{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}_{safeCaseName}";
        var outputDir = Path.Combine(baseDir, runDirName);
        Directory.CreateDirectory(outputDir);

        return new RunContext(
            CaseName: caseName,
            InputFolderPath: inputFolderPath,
            OutputDirectory: outputDir,
            AuditLogPath: Path.Combine(outputDir, "audit.jsonl"),
            CsvPath: Path.Combine(outputDir, "result.csv"),
            CaseJsonPath: Path.Combine(outputDir, "case.json"));
    }
}

