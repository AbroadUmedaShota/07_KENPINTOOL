using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using KenpinTool.Prototype;
using OpenCvSharp;
using PdfiumViewer;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace KenpinTool.Prototype.Services;

public sealed record ReportMetadata(
    string CaseName,
    string InputPath,
    DateTimeOffset CompletedAt,
    string ToolVersion,
    int TotalPages,
    int OkCount,
    int RescanCount,
    int ExceptionCount,
    int UnreviewedCount);

public sealed record ReportDetection(
    string Code,
    NgLevel Level,
    IReadOnlyList<EvidenceRegion> Evidence);

public sealed record ReportIssueItem(
    int PageIndex,
    string FilePath,
    string FileName,
    int? PdfPageIndex,
    DecisionAction DecisionAction,
    string? ExceptionReasonCode,
    string? ExceptionNote,
    IReadOnlyList<ReportDetection> Detections);

public sealed class ReportGenerator
{
    private const int RenderDpi = 300;
    private const int ThumbnailMaxWidth = 640;
    private const int ThumbnailMaxHeight = 640;

    public void Generate(string outputPath, ReportMetadata metadata, IReadOnlyList<ReportIssueItem> issueItems)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("Output path is required.", nameof(outputPath));
        }

        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var preparedItems = issueItems
            .Select(item => new ReportIssueItemWithImage(item, BuildThumbnail(item)))
            .ToList();

        var document = new ReportDocument(metadata, preparedItems);
        document.GeneratePdf(outputPath);
    }

    private static byte[]? BuildThumbnail(ReportIssueItem item)
    {
        try
        {
            using var mat = LoadMat(item.FilePath, item.PdfPageIndex);
            if (mat is null || mat.Empty())
            {
                return null;
            }

            using var color = EnsureColor(mat);
            DrawEvidence(color, item.Detections);

            using var resized = ResizeForThumbnail(color);
            if (Cv2.ImEncode(".png", resized, out var buffer))
            {
                return buffer;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static Mat? LoadMat(string filePath, int? pdfPageIndex)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        return pdfPageIndex.HasValue
            ? RenderPdfToMat(filePath, pdfPageIndex.Value)
            : new Mat(filePath, ImreadModes.Color);
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

    private static Mat EnsureColor(Mat source)
    {
        if (source.Channels() == 3)
        {
            return source.Clone();
        }

        var color = new Mat();
        var conversion = source.Channels() == 4
            ? ColorConversionCodes.BGRA2BGR
            : ColorConversionCodes.GRAY2BGR;
        Cv2.CvtColor(source, color, conversion);
        return color;
    }

    private static Mat ResizeForThumbnail(Mat source)
    {
        var scale = Math.Min(
            Math.Min(ThumbnailMaxWidth / (double)source.Width, ThumbnailMaxHeight / (double)source.Height),
            1.0);

        if (scale >= 1.0)
        {
            return source.Clone();
        }

        var resized = new Mat();
        var size = new OpenCvSharp.Size(
            Math.Max(1, (int)(source.Width * scale)),
            Math.Max(1, (int)(source.Height * scale)));
        Cv2.Resize(source, resized, size, 0, 0, InterpolationFlags.Area);
        return resized;
    }

    private static void DrawEvidence(Mat image, IReadOnlyList<ReportDetection> detections)
    {
        if (detections.Count == 0)
        {
            return;
        }

        var width = image.Width;
        var height = image.Height;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var thickness = Math.Max(2, Math.Min(width, height) / 200);

        foreach (var detection in detections)
        {
            if (detection.Evidence.Count == 0)
            {
                continue;
            }

            var color = LevelToColor(detection.Level);
            foreach (var ev in detection.Evidence)
            {
                var x = Math.Clamp((int)Math.Round(ev.X * width), 0, width - 1);
                var y = Math.Clamp((int)Math.Round(ev.Y * height), 0, height - 1);
                var w = Math.Clamp((int)Math.Round(ev.Width * width), 1, width - x);
                var h = Math.Clamp((int)Math.Round(ev.Height * height), 1, height - y);
                var rect = new Rect(x, y, w, h);
                Cv2.Rectangle(image, rect, color, thickness);
            }
        }
    }

    private static Scalar LevelToColor(NgLevel level) =>
        level switch
        {
            NgLevel.NgA => new Scalar(0, 0, 255),
            NgLevel.NgB => new Scalar(0, 128, 255),
            NgLevel.NgC => new Scalar(0, 255, 255),
            _ => new Scalar(0, 255, 0),
        };

    private sealed record ReportIssueItemWithImage(ReportIssueItem Item, byte[]? ThumbnailBytes);

    private sealed class ReportDocument : IDocument
    {
        private readonly ReportMetadata _metadata;
        private readonly IReadOnlyList<ReportIssueItemWithImage> _issueItems;

        public ReportDocument(ReportMetadata metadata, IReadOnlyList<ReportIssueItemWithImage> issueItems)
        {
            _metadata = metadata;
            _issueItems = issueItems;
        }

        public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(24);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontFamily("Yu Gothic UI").FontSize(10));

                page.Content().Column(column =>
                {
                    column.Spacing(12);
                    column.Item().Element(ComposeHeader);
                    column.Item().Element(ComposeSummary);
                    column.Item().Element(ComposeIssues);
                });
            });
        }

        private void ComposeHeader(IContainer container)
        {
            container.Column(column =>
            {
                column.Spacing(6);
                column.Item().Text("検品完了レポート").FontSize(18).Bold();
                column.Item().Text($"案件名: {_metadata.CaseName}");
                column.Item().Text($"入力パス: {_metadata.InputPath}");
                column.Item().Text($"検品完了日時: {_metadata.CompletedAt:yyyy/MM/dd HH:mm:ss}");
                column.Item().Text($"ツールバージョン: {_metadata.ToolVersion}");
            });
        }

        private void ComposeSummary(IContainer container)
        {
            container.Column(column =>
            {
                column.Spacing(6);
                column.Item().Text("統計サマリー").FontSize(14).Bold();
                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn();
                        columns.RelativeColumn();
                        columns.RelativeColumn();
                        columns.RelativeColumn();
                    });

                    table.Cell().Text("総ページ数").SemiBold();
                    table.Cell().Text("OK").SemiBold();
                    table.Cell().Text("再スキャン").SemiBold();
                    table.Cell().Text("例外承認").SemiBold();

                    table.Cell().Text(_metadata.TotalPages.ToString());
                    table.Cell().Text(_metadata.OkCount.ToString());
                    table.Cell().Text(_metadata.RescanCount.ToString());
                    table.Cell().Text(_metadata.ExceptionCount.ToString());

                    table.Cell().Text("未検品").SemiBold();
                    table.Cell().Text(_metadata.UnreviewedCount.ToString());
                    table.Cell().Text("");
                    table.Cell().Text("");
                });
            });
        }

        private void ComposeIssues(IContainer container)
        {
            container.Column(column =>
            {
                column.Spacing(8);
                column.Item().Text("不備明細（OK 以外）").FontSize(14).Bold();

                if (_issueItems.Count == 0)
                {
                    column.Item().Text("不備ページはありません。").FontColor(Colors.Grey.Darken2);
                    return;
                }

                foreach (var item in _issueItems)
                {
                    column.Item().Element(c => ComposeIssue(c, item));
                }
            });
        }

        private void ComposeIssue(IContainer container, ReportIssueItemWithImage issue)
        {
            container.Border(1).BorderColor(Colors.Grey.Lighten2).Padding(8).Column(column =>
            {
                column.Spacing(6);
                column.Item().Text($"{BuildPageLabel(issue.Item)}  {issue.Item.FileName}").SemiBold();

                column.Item().Row(row =>
                {
                    row.ConstantItem(180).Height(180).Element(image =>
                    {
                        if (issue.ThumbnailBytes is { Length: > 0 })
                        {
                            image.Image(issue.ThumbnailBytes).FitArea();
                            return;
                        }

                        image.Border(1).BorderColor(Colors.Grey.Lighten2).Background(Colors.Grey.Lighten3)
                            .AlignCenter().AlignMiddle()
                            .Text("No Image").FontColor(Colors.Grey.Darken1);
                    });

                    row.RelativeItem().PaddingLeft(12).Column(detail =>
                    {
                        detail.Spacing(4);
                        detail.Item().Text($"判定: {BuildDecisionText(issue.Item)}");
                        detail.Item().Text($"判定理由: {BuildReasonText(issue.Item)}");
                        detail.Item().Text($"NGコード: {BuildNgCodes(issue.Item)}");
                    });
                });
            });
        }

        private static string BuildPageLabel(ReportIssueItem item)
        {
            var pdfSuffix = item.PdfPageIndex.HasValue
                ? $" (PDF p{item.PdfPageIndex:000})"
                : "";
            return $"Page {item.PageIndex:000}{pdfSuffix}";
        }

        private static string BuildDecisionText(ReportIssueItem item)
            => item.DecisionAction switch
            {
                DecisionAction.Rescan => "再スキャン（NG-A）",
                DecisionAction.ExceptionApproved => "例外承認",
                DecisionAction.Ok => "OK",
                _ => item.DecisionAction.ToString(),
            };

        private static string BuildReasonText(ReportIssueItem item)
        {
            if (item.DecisionAction != DecisionAction.ExceptionApproved)
            {
                return "検知結果により再スキャン";
            }

            var parts = new[]
            {
                item.ExceptionReasonCode,
                item.ExceptionNote
            }.Where(p => !string.IsNullOrWhiteSpace(p));

            var combined = string.Join(" ", parts);
            return string.IsNullOrWhiteSpace(combined) ? "例外承認" : combined;
        }

        private static string BuildNgCodes(ReportIssueItem item)
        {
            var codes = item.Detections
                .Select(d => d.Code)
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Distinct()
                .ToArray();

            return codes.Length == 0 ? "-" : string.Join(", ", codes);
        }
    }
}
