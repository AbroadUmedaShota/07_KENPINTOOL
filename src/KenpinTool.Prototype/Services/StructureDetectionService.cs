using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using OpenCvSharp;
using PdfiumViewer;

namespace KenpinTool.Prototype;

public sealed class StructureDetectionService
{
    private const int RenderDpi = 240;
    private const int HashSize = 64;
    private static readonly byte[] BitCounts = BuildBitCounts();

    public byte[]? ComputeHash(string filePath, int? pdfPageIndex)
    {
        try
        {
            using var mat = LoadMat(filePath, pdfPageIndex);
            if (mat is null || mat.Empty())
            {
                return null;
            }

            using var gray = EnsureGray(mat);
            using var resized = new Mat();
            Cv2.Resize(gray, resized, new OpenCvSharp.Size(HashSize, HashSize), 0, 0, InterpolationFlags.Area);

            var mean = Cv2.Mean(resized).Val0;
            return BuildAverageHash(resized, mean);
        }
        catch
        {
            return null;
        }
    }

    public double ComputeSimilarity(IReadOnlyList<byte> a, IReadOnlyList<byte> b)
    {
        if (a.Count == 0 || b.Count == 0 || a.Count != b.Count)
        {
            return 0.0;
        }

        var diffBits = 0;
        for (var i = 0; i < a.Count; i++)
        {
            diffBits += BitCounts[a[i] ^ b[i]];
        }

        var totalBits = a.Count * 8;
        return 1.0 - (double)diffBits / totalBits;
    }

    private static Mat? LoadMat(string filePath, int? pdfPageIndex)
    {
        if (pdfPageIndex.HasValue)
        {
            return RenderPdfToMat(filePath, pdfPageIndex.Value);
        }

        return new Mat(filePath, ImreadModes.Grayscale);
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
                return EnsureGray(mat);
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

    private static byte[] BuildAverageHash(Mat gray, double mean)
    {
        var totalBits = HashSize * HashSize;
        var bytes = new byte[totalBits / 8];
        var bitIndex = 0;

        for (var y = 0; y < HashSize; y++)
        {
            for (var x = 0; x < HashSize; x++)
            {
                var value = gray.At<byte>(y, x) >= mean;
                if (value)
                {
                    bytes[bitIndex >> 3] |= (byte)(1 << (bitIndex & 7));
                }

                bitIndex++;
            }
        }

        return bytes;
    }

    private static byte[] BuildBitCounts()
    {
        var counts = new byte[256];
        for (var i = 0; i < counts.Length; i++)
        {
            var v = i;
            byte c = 0;
            while (v > 0)
            {
                c += (byte)(v & 1);
                v >>= 1;
            }

            counts[i] = c;
        }

        return counts;
    }
}
