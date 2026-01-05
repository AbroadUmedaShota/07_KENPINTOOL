using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace KenpinTool.Prototype;

public static class CsvExporter
{
    public static void Export(string csvPath, IReadOnlyList<PageItem> pages)
    {
        var dir = Path.GetDirectoryName(csvPath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", new[]
        {
            "page_index",
            "file_name",
            "pdf_page_index",
            "decision_action",
            "decision_ts_utc",
            "exception_reason_code",
            "exception_note",
            "ng_codes",
            "ng_levels",
            "suggested_actions",
            "rework_types",
        }));

        foreach (var page in pages.OrderBy(p => p.Index))
        {
            var decisionAction = page.Decision?.Action.ToString() ?? "";
            var decisionTs = page.Decision?.TimestampUtc.ToString("O") ?? "";
            var excCode = page.Decision?.ExceptionReasonCode ?? "";
            var excNote = page.Decision?.ExceptionNote ?? "";

            var codes = string.Join("|", page.Detections.Select(d => d.Code));
            var levels = string.Join("|", page.Detections.Select(d => Detection.LevelText(d.Level)));
            var suggestedActions = string.Join("|", page.Detections.Select(d => d.SuggestedAction.ToString().ToUpperInvariant()));
            var reworkTypes = string.Join("|", page.Detections.Select(d => d.ReworkType.ToString().ToUpperInvariant()));

            sb.AppendLine(string.Join(",", new[]
            {
                Escape(page.Index.ToString()),
                Escape(page.FileName),
                Escape(page.PdfPageIndex?.ToString() ?? ""),
                Escape(decisionAction),
                Escape(decisionTs),
                Escape(excCode),
                Escape(excNote),
                Escape(codes),
                Escape(levels),
                Escape(suggestedActions),
                Escape(reworkTypes),
            }));
        }

        File.WriteAllText(csvPath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private static string Escape(string value)
    {
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        return value;
    }
}

