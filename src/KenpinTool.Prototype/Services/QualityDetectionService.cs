using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using OpenCvSharp;
using PdfiumViewer;

namespace KenpinTool.Prototype.Services;

public sealed class QualityDetectionService
{
    private const int RenderDpi = 240;
    private const double MinLineLengthRatio = 0.55;
    private const int MaxEvidenceCount = 8;
    private const double AngleToleranceDeg = 5.0;

    public IReadOnlyList<Detection> DetectQlT05(string filePath, int? pdfPageIndex)
    {
        try
        {
            using var mat = LoadMat(filePath, pdfPageIndex);
            if (mat is null || mat.Empty())
            {
                return Array.Empty<Detection>();
            }

            using var gray = EnsureGray(mat);
            using var blurred = new Mat();
            Cv2.GaussianBlur(gray, blurred, new OpenCvSharp.Size(3, 3), 0);

            using var edges = new Mat();
            Cv2.Canny(blurred, edges, 50, 150);

            var minDim = Math.Min(gray.Width, gray.Height);
            var minLineLength = Math.Max(40, (int)(minDim * MinLineLengthRatio));
            var maxLineGap = Math.Max(8, (int)(minDim * 0.01));

            var lines = Cv2.HoughLinesP(edges, 1, Math.PI / 180, 100, minLineLength, maxLineGap);
            var evidence = BuildEvidence(lines, gray.Width, gray.Height, minDim);
            if (evidence.Count == 0)
            {
                return Array.Empty<Detection>();
            }

            var confidence = Math.Clamp(0.6 + evidence.Count * 0.05, 0.6, 0.9);
            return new[]
            {
                new Detection(
                    "QLT-05",
                    "線状ノイズ（縦線・横線）",
                    NgLevel.NgA,
                    SuggestedAction.Rescan,
                    ReworkType.None,
                    confidence,
                    evidence)
            };
        }
        catch
        {
            return Array.Empty<Detection>();
        }
    }

    private static Mat? LoadMat(string filePath, int? pdfPageIndex)
    {
        if (pdfPageIndex.HasValue)
        {
            return RenderPdfToMat(filePath, pdfPageIndex.Value);
        }

        return new Mat(filePath, ImreadModes.Color);
    }

    private static Mat? RenderPdfToMat(string filePath, int pdfPageIndex)
    {
        using var document = PdfDocument.Load(filePath);
        var zeroBased = pdfPageIndex - 1;
        if (zeroBased < 0 || zeroBased >= document.PageCount)
        {
            return null;
        }

        using var image = document.Render(zeroBased, RenderDpi, RenderDpi, true);
        if (image is null)
        {
            return null;
        }

        using var bitmap = image as Bitmap ?? new Bitmap(image);
        Bitmap? working = null;
        try
        {
            if (bitmap.PixelFormat != PixelFormat.Format32bppArgb)
            {
                working = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format32bppArgb);
                using var g = Graphics.FromImage(working);
                g.DrawImage(bitmap, 0, 0, bitmap.Width, bitmap.Height);
            }
            else
            {
                working = bitmap;
            }

            var rect = new Rectangle(0, 0, working.Width, working.Height);
            var data = working.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                using var mat = Mat.FromPixelData(working.Height, working.Width, MatType.CV_8UC4, data.Scan0, data.Stride);
                return mat.Clone();
            }
            finally
            {
                working.UnlockBits(data);
            }
        }
        finally
        {
            if (!ReferenceEquals(working, bitmap))
            {
                working?.Dispose();
            }
        }
    }

    private static Mat EnsureGray(Mat source)
    {
        if (source.Channels() == 1)
        {
            return source.Clone();
        }

        var gray = new Mat();
        var conversion = source.Channels() == 4
            ? ColorConversionCodes.BGRA2GRAY
            : ColorConversionCodes.BGR2GRAY;
        Cv2.CvtColor(source, gray, conversion);
        return gray;
    }

    private static List<EvidenceRegion> BuildEvidence(
        IReadOnlyList<LineSegmentPoint> lines,
        int width,
        int height,
        int minDim)
    {
        var evidence = new List<EvidenceRegion>();
        if (lines.Count == 0 || width <= 0 || height <= 0)
        {
            return evidence;
        }

        var pad = Math.Max(2, (int)(minDim * 0.0025));
        foreach (var line in lines)
        {
            var dx = line.P2.X - line.P1.X;
            var dy = line.P2.Y - line.P1.Y;
            var angle = Math.Abs(Math.Atan2(dy, dx) * 180.0 / Math.PI);

            var isHorizontal = angle <= AngleToleranceDeg || angle >= 180.0 - AngleToleranceDeg;
            var isVertical = Math.Abs(angle - 90.0) <= AngleToleranceDeg;
            if (!isHorizontal && !isVertical)
            {
                continue;
            }

            var minX = Math.Max(0, Math.Min(line.P1.X, line.P2.X) - pad);
            var maxX = Math.Min(width - 1, Math.Max(line.P1.X, line.P2.X) + pad);
            var minY = Math.Max(0, Math.Min(line.P1.Y, line.P2.Y) - pad);
            var maxY = Math.Min(height - 1, Math.Max(line.P1.Y, line.P2.Y) + pad);

            var rectWidth = maxX - minX + 1;
            var rectHeight = maxY - minY + 1;
            if (rectWidth <= 0 || rectHeight <= 0)
            {
                continue;
            }

            evidence.Add(new EvidenceRegion(
                minX / (double)width,
                minY / (double)height,
                rectWidth / (double)width,
                rectHeight / (double)height));

            if (evidence.Count >= MaxEvidenceCount)
            {
                break;
            }
        }

        return evidence;
    }
}
