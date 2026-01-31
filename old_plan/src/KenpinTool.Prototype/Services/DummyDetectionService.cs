using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace KenpinTool.Prototype;

public sealed class DummyDetectionService
{
    private static readonly Regex CodeRegex = new(@"[A-Z]{3}-\d{2}S?", RegexOptions.Compiled);

    private static readonly IReadOnlyDictionary<string, (string Name, NgLevel Level, SuggestedAction Action, ReworkType ReworkType)> Master =
        new Dictionary<string, (string, NgLevel, SuggestedAction, ReworkType)>(StringComparer.Ordinal)
        {
            ["STR-01S"] = ("ページ抜け疑い", NgLevel.NgC, SuggestedAction.Review, ReworkType.None),
            ["STR-01"] = ("ページ抜け（確定）", NgLevel.NgA, SuggestedAction.Rescan, ReworkType.None),
            ["STR-02"] = ("ページ重複", NgLevel.NgA, SuggestedAction.Rescan, ReworkType.None),
            ["STR-03S"] = ("ページ順序異常疑い", NgLevel.NgC, SuggestedAction.Review, ReworkType.None),
            ["STR-03"] = ("ページ順序異常（確定）", NgLevel.NgA, SuggestedAction.Rescan, ReworkType.None),
            ["STR-04"] = ("ページ回転誤り", NgLevel.NgB, SuggestedAction.Rework, ReworkType.Rotate),

            ["CUT-01"] = ("本文欠け", NgLevel.NgA, SuggestedAction.Rescan, ReworkType.None),
            ["CUT-02"] = ("図枠欠け", NgLevel.NgA, SuggestedAction.Rescan, ReworkType.None),
            ["CUT-03"] = ("端部余白不足", NgLevel.NgB, SuggestedAction.Rework, ReworkType.None),
            ["CUT-04"] = ("過剰トリミング", NgLevel.NgB, SuggestedAction.Rework, ReworkType.None),

            ["QLT-01"] = ("ピンボケ", NgLevel.NgA, SuggestedAction.Rescan, ReworkType.None),
            ["QLT-02"] = ("解像度不足", NgLevel.NgA, SuggestedAction.Rescan, ReworkType.None),
            ["QLT-03"] = ("白飛び/黒潰れ", NgLevel.NgB, SuggestedAction.Rework, ReworkType.None),
            ["QLT-04"] = ("影・写り込み", NgLevel.NgB, SuggestedAction.Rework, ReworkType.None),
            ["QLT-05"] = ("線状ノイズ（縦線・横線）", NgLevel.NgA, SuggestedAction.Rescan, ReworkType.None),

            ["OCR-01"] = ("OCRなし", NgLevel.NgB, SuggestedAction.Rework, ReworkType.Reocr),
            ["OCR-02"] = ("OCR抽出不可", NgLevel.NgA, SuggestedAction.Rescan, ReworkType.Reocr),
            ["OCR-03"] = ("OCR低信頼", NgLevel.NgC, SuggestedAction.Review, ReworkType.None),
            ["OCR-04"] = ("OCRズレ", NgLevel.NgB, SuggestedAction.Rework, ReworkType.Reocr),

            ["ATR-01"] = ("ページサイズ混在", NgLevel.NgB, SuggestedAction.Review, ReworkType.None),
            ["ATR-02"] = ("向き混在", NgLevel.NgB, SuggestedAction.Review, ReworkType.None),
            ["ATR-03"] = ("縮尺異常", NgLevel.NgA, SuggestedAction.Rescan, ReworkType.None),

            ["DWG-01"] = ("細線欠損", NgLevel.NgA, SuggestedAction.Rescan, ReworkType.None),
            ["DWG-02"] = ("圧縮劣化", NgLevel.NgB, SuggestedAction.Rework, ReworkType.Recompress),
            ["DWG-03"] = ("分割結合ズレ", NgLevel.NgA, SuggestedAction.Rescan, ReworkType.SplitMerge),
        };

    public IReadOnlyList<Detection> DetectFromFileName(string filePath)
    {
        var name = Path.GetFileNameWithoutExtension(filePath);
        if (string.IsNullOrWhiteSpace(name))
        {
            return Array.Empty<Detection>();
        }

        var codes = CodeRegex.Matches(name)
            .Select(m => m.Value)
            .Distinct(StringComparer.Ordinal)
            .Where(Master.ContainsKey)
            .ToArray();

        if (codes.Length == 0)
        {
            return Array.Empty<Detection>();
        }

        var results = new List<Detection>(codes.Length);
        foreach (var code in codes)
        {
            var (displayName, level, suggestedAction, reworkType) = Master[code];

            var rnd = new Random(HashCode.Combine(name, code));
            var evidence = BuildEvidence(rnd, level);
            var confidence = level == NgLevel.NgC ? rnd.NextDouble() * 0.2 + 0.5 : rnd.NextDouble() * 0.25 + 0.7; // 0.5-0.7 or 0.7-0.95

            results.Add(
                new Detection(
                    code,
                    displayName,
                    level,
                    suggestedAction,
                    reworkType,
                    confidence: confidence,
                    evidence: evidence));
        }

        return results;
    }

    private static IReadOnlyList<EvidenceRegion> BuildEvidence(Random rnd, NgLevel level)
    {
        if (level == NgLevel.Ok)
        {
            return Array.Empty<EvidenceRegion>();
        }

        var x = rnd.NextDouble() * 0.65 + 0.05;
        var y = rnd.NextDouble() * 0.65 + 0.05;
        var w = rnd.NextDouble() * 0.25 + 0.1;
        var h = rnd.NextDouble() * 0.25 + 0.1;

        if (x + w > 0.98)
        {
            w = 0.98 - x;
        }

        if (y + h > 0.98)
        {
            h = 0.98 - y;
        }

        return new[] { new EvidenceRegion(x, y, w, h) };
    }
}

