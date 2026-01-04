using System;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;

namespace KenpinTool.Prototype.Services;

public class ImageLoaderService : IDisposable
{
    private readonly Channel<LoadRequest> _loadChannel;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _processTask;

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

    public async Task<BitmapSource?> LoadImageAsync(string filePath, CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        var tcs = new TaskCompletionSource<BitmapSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = new LoadRequest(filePath, tcs, ct);

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
                        // Load image using OpenCV
                        using var mat = new Mat(request.FilePath, ImreadModes.Color);
                        if (mat.Empty())
                        {
                            request.CompletionSource.TrySetResult(null);
                            continue;
                        }

                        // Convert to WPF BitmapSource
                        // We must Freeze it to allow it to be shared across threads (passed to UI thread)
                        var bitmap = mat.ToBitmapSource();
                        bitmap.Freeze();

                        request.CompletionSource.TrySetResult(bitmap);
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
    }

    public void Dispose()
    {
        _cts.Cancel();
        _loadChannel.Writer.TryComplete();
    }

    private record LoadRequest(string FilePath, TaskCompletionSource<BitmapSource?> CompletionSource, CancellationToken CancellationToken);
}
