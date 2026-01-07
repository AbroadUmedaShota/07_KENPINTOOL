using System.IO;
using System.Collections.Generic;

namespace KenpinTool.Prototype.Services;

public sealed class SimpleValidationService
{
    private const long MinFileSize = 1024; // 1KB

    public IReadOnlyList<Detection> ValidateFile(string filePath)
    {
        var detections = new List<Detection>();

        if (!File.Exists(filePath))
        {
            detections.Add(new Detection(
                "SYS-01",
                "ファイルが見つかりません",
                NgLevel.NgA,
                SuggestedAction.Rescan,
                ReworkType.None));
            return detections;
        }

        try
        {
            var info = new FileInfo(filePath);
            if (info.Length < MinFileSize)
            {
                detections.Add(new Detection(
                    "STR-01",
                    "ファイルサイズ異常（破損または白紙の可能性）",
                    NgLevel.NgA,
                    SuggestedAction.Rescan,
                    ReworkType.None,
                    confidence: 1.0));
            }
        }
        catch
        {
            // Access error etc.
            detections.Add(new Detection(
                "SYS-02",
                "ファイルアクセスエラー",
                NgLevel.NgA,
                SuggestedAction.Rescan,
                ReworkType.None));
        }

        return detections;
    }
}
