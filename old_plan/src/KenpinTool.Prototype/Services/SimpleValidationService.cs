using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using OpenCvSharp;
using PdfiumViewer;

namespace KenpinTool.Prototype;

public sealed class SimpleValidationService
{
    private const long MinFileSize = 1024; // 1KB
    private const int LowResDpi = 50;      // Gatekeeper uses low resolution for speed
    private const int CornerSampleSize = 10; // Pixels to check from each corner
    private const byte DarkThreshold = 30;   // Threshold for "black background" detection

    public IReadOnlyList<Detection> ValidateFile(string filePath, int? pdfPageIndex, DetectionSettings settings)
    {
        var detections = new List<Detection>();

        // 1. Basic File IO Check
        if (!File.Exists(filePath))
        {
            detections.Add(new Detection("SYS-01", "ファイルが見つかりません", NgLevel.NgA, SuggestedAction.Rescan, ReworkType.None));
            return detections;
        }

        try
        {
            var info = new FileInfo(filePath);
            if (info.Length < MinFileSize)
            {
                detections.Add(new Detection("STR-01", "ファイルサイズ異常", NgLevel.NgA, SuggestedAction.Rescan, ReworkType.None, 1.0));
                return detections;
            }

            // 2. Visual Checks (Low Cost)
            using var mat = LoadLowResMat(filePath, pdfPageIndex, settings.LowResDpi);
            if (mat != null && !mat.Empty())
            {
                CheckAspectRatio(mat, detections, settings);
                CheckCornerCuts(mat, detections, settings);
            }
        }
        catch (Exception ex)
        {
            detections.Add(new Detection("SYS-02", $"解析エラー: {ex.Message}", NgLevel.NgA, SuggestedAction.Rescan, ReworkType.None));
        }

        return detections;
    }

    private static Mat? LoadLowResMat(string filePath, int? pdfPageIndex, int dpi)
    {
        if (pdfPageIndex.HasValue)
        {
            using var document = PdfDocument.Load(filePath);
            var idx = pdfPageIndex.Value - 1;
            if (idx < 0 || idx >= document.PageCount) return null;
            
            // Render at very low DPI for speed
            using var image = document.Render(idx, dpi, dpi, true);
            using var bitmap = new Bitmap(image);
            
            // Convert Bitmap to Mat via byte array/memory
            using var ms = new MemoryStream();
            bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Bmp);
            return Mat.FromImageData(ms.ToArray(), ImreadModes.Color);
        }
        else
        {
            return new Mat(filePath, ImreadModes.Color);
        }
    }

    private void CheckAspectRatio(Mat mat, List<Detection> detections, DetectionSettings settings)
    {
        double ratio = (double)mat.Width / mat.Height;
        if (mat.Height > mat.Width) ratio = (double)mat.Height / mat.Width;

        if (ratio > settings.AspectRatioMax || ratio < settings.AspectRatioMin)
        {
            detections.Add(new Detection(
                "STR-03", 
                $"アスペクト比異常 ({ratio:F2})", 
                NgLevel.NgB, 
                SuggestedAction.Review, 
                ReworkType.None,
                0.9));
        }
    }

    private void CheckCornerCuts(Mat mat, List<Detection> detections, DetectionSettings settings)
    {
        using var gray = new Mat();
        Cv2.CvtColor(mat, gray, ColorConversionCodes.BGR2GRAY);

        bool cutDetected = false;
        int w = gray.Width;
        int h = gray.Height;
        int sample = settings.CornerSampleSize;

        // Sample 4 corners
        cutDetected |= IsAreaDark(gray, 0, 0, sample, sample, settings.CornerDarkThreshold); // Top-Left
        cutDetected |= IsAreaDark(gray, w - sample, 0, sample, sample, settings.CornerDarkThreshold); // Top-Right
        cutDetected |= IsAreaDark(gray, 0, h - sample, sample, sample, settings.CornerDarkThreshold); // Bottom-Left
        cutDetected |= IsAreaDark(gray, w - sample, h - sample, sample, sample, settings.CornerDarkThreshold); // Bottom-Right

        if (cutDetected)
        {
            detections.Add(new Detection(
                "CUT-01", 
                "角欠け・角折れの疑い", 
                NgLevel.NgB, 
                SuggestedAction.Review, 
                ReworkType.None,
                0.8,
                new[] { new EvidenceRegion(0, 0, 1, 1) })); // Highlight entire page for now
        }
    }

    private bool IsAreaDark(Mat gray, int x, int y, int width, int height, int threshold)
    {
        // Safety bounds
        x = Math.Clamp(x, 0, gray.Width - 1);
        y = Math.Clamp(y, 0, gray.Height - 1);
        width = Math.Min(width, gray.Width - x);
        height = Math.Min(height, gray.Height - y);

        using var roi = new Mat(gray, new Rect(x, y, width, height));
        var mean = Cv2.Mean(roi).Val0;
        return mean < threshold; 
    }
}
