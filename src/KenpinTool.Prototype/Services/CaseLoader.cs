using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PdfiumViewer;

namespace KenpinTool.Prototype;

public sealed class CaseLoader
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".bmp",
        ".tif",
        ".tiff",
    };

    private static readonly HashSet<string> PdfExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf",
    };

    public IReadOnlyList<PageSource> LoadPages(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            throw new ArgumentException("Folder path is required.", nameof(folderPath));
        }

        if (!Directory.Exists(folderPath))
        {
            throw new DirectoryNotFoundException(folderPath);
        }

        var files = Directory.EnumerateFiles(folderPath, "*.*", SearchOption.TopDirectoryOnly)
            .Where(IsSupportedExtension)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

        var pages = new List<PageSource>();

        foreach (var file in files)
        {
            var extension = Path.GetExtension(file);
            if (ImageExtensions.Contains(extension))
            {
                pages.Add(new PageSource(file, null));
                continue;
            }

            if (PdfExtensions.Contains(extension))
            {
                var pageCount = GetPdfPageCount(file);
                for (var i = 1; i <= pageCount; i++)
                {
                    pages.Add(new PageSource(file, i));
                }
            }
        }

        return pages;
    }

    private static bool IsSupportedExtension(string path)
    {
        var extension = Path.GetExtension(path);
        return ImageExtensions.Contains(extension) || PdfExtensions.Contains(extension);
    }

    private static int GetPdfPageCount(string filePath)
    {
        try
        {
            using var document = PdfDocument.Load(filePath);
            var count = document.PageCount;
            if (count <= 0)
            {
                throw new InvalidDataException("PDF has no pages.");
            }

            return count;
        }
        catch (Exception ex) when (ex is not InvalidDataException)
        {
            throw new InvalidDataException($"PDF load failed: {filePath}", ex);
        }
    }
}

public sealed record PageSource(string FilePath, int? PdfPageIndex);

