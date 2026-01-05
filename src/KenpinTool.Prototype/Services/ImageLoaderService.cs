using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using PdfiumViewer;

namespace KenpinTool.Prototype.Services;

public class ImageLoaderService : IDisposable
{
    private const int PdfRenderDpi = 300;

    private readonly Channel<LoadRequest> _loadChannel;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _processTask;
    private PdfDocument? _cachedPdfDocument;
    private string? _cachedPdfPath;

    public ImageLoaderService()
    {
        // Bounded channel to prevent memory explosion if too many requests pile up.
        // DropOldest is chosen because if the user scrolls fast, we only care about the latest images.
        _loadChannel = Channel.CreateBounded<LoadRequest>(new BoundedChannelOptions(5)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });

        _processTask = Task.Factory.StartNew(ProcessQueueAsync, TaskCreationOptions.LongRunning);
    }

    public Task<BitmapSource?> LoadImageAsync(string filePath, CancellationToken ct = default)
        => LoadImageAsync(filePath, null, ct);

    public async Task<BitmapSource?> LoadImageAsync(string filePath, int? pdfPageIndex, CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        var tcs = new TaskCompletionSource<BitmapSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = new LoadRequest(filePath, pdfPageIndex, tcs, ct);

        try
        {
            await _loadChannel.Writer.WriteAsync(request, ct);
        }
        catch (ChannelClosedException)
        {
            return null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }

        return await tcs.Task;
    }

    private async Task ProcessQueueAsync()
    {
        var reader = _loadChannel.Reader;

        try
        {
            while (await reader.WaitToReadAsync(_cts.Token))
            {
                while (reader.TryRead(out var request))
                {
                    if (request.CancellationToken.IsCancellationRequested)
                    {
                        request.CompletionSource.TrySetCanceled(request.CancellationToken);
                        continue;
                    }

                    try
                    {
                        if (request.PdfPageIndex.HasValue)
                        {
                            var pdfImage = RenderPdfPage(request.FilePath, request.PdfPageIndex.Value);
                            request.CompletionSource.TrySetResult(pdfImage);
                        }
                        else
                        {
                            var image = LoadBitmapImage(request.FilePath);
                            request.CompletionSource.TrySetResult(image);
                        }
                    }
                    catch (Exception ex)
                    {
                        request.CompletionSource.TrySetException(ex);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown
        }
        catch (Exception)
        {
            // In a real app, log this
        }
        finally
        {
            DisposeCachedPdfDocument();
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _loadChannel.Writer.TryComplete();
    }

    private BitmapSource? RenderPdfPage(string filePath, int pdfPageIndex)
    {
        var document = GetPdfDocument(filePath);
        if (document is null)
        {
            return null;
        }

        var zeroBased = pdfPageIndex - 1;
        if (zeroBased < 0 || zeroBased >= document.PageCount)
        {
            return null;
        }

        using var image = document.Render(zeroBased, PdfRenderDpi, PdfRenderDpi, true);
        return ConvertToBitmapSource(image);
    }

    private PdfDocument? GetPdfDocument(string filePath)
    {
        if (_cachedPdfDocument is not null &&
            string.Equals(_cachedPdfPath, filePath, StringComparison.OrdinalIgnoreCase))
        {
            return _cachedPdfDocument;
        }

        DisposeCachedPdfDocument();

        _cachedPdfDocument = PdfDocument.Load(filePath);
        _cachedPdfPath = filePath;
        return _cachedPdfDocument;
    }

    private void DisposeCachedPdfDocument()
    {
        _cachedPdfDocument?.Dispose();
        _cachedPdfDocument = null;
        _cachedPdfPath = null;
    }

    private static BitmapSource? LoadBitmapImage(string filePath)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile;
        bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static BitmapSource? ConvertToBitmapSource(Image image)
    {
        using var bitmap = image as Bitmap ?? new Bitmap(image);
        if (bitmap.PixelFormat != PixelFormat.Format32bppArgb)
        {
            using var converted = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(converted);
            g.DrawImage(bitmap, 0, 0, bitmap.Width, bitmap.Height);
            return CreateBitmapSource(converted);
        }

        return CreateBitmapSource(bitmap);
    }

    private static BitmapSource CreateBitmapSource(Bitmap bitmap)
    {
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var size = Math.Abs(data.Stride) * bitmap.Height;
            var buffer = new byte[size];
            Marshal.Copy(data.Scan0, buffer, 0, size);

            const double dpi = PdfRenderDpi;
            var bitmapSource = BitmapSource.Create(
                bitmap.Width,
                bitmap.Height,
                dpi,
                dpi,
                System.Windows.Media.PixelFormats.Bgra32,
                null,
                buffer,
                data.Stride);
            bitmapSource.Freeze();
            return bitmapSource;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private record LoadRequest(
        string FilePath,
        int? PdfPageIndex,
        TaskCompletionSource<BitmapSource?> CompletionSource,
        CancellationToken CancellationToken);
}
