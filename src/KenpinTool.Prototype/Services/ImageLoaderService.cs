using System.IO;
using System.Threading.Channels;
using System.Windows.Media.Imaging;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;

namespace KenpinTool.Prototype.Services;

public interface IImageLoaderService
{
    ValueTask EnqueueAsync(string filePath);
    IAsyncEnumerable<BitmapSource> GetImageStreamAsync(CancellationToken cancellationToken = default);
}

public class ImageLoaderService : IImageLoaderService
{
    private readonly Channel<string> _requestChannel = Channel.CreateBounded<string>(new BoundedChannelOptions(10)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = false
    });

    private readonly Channel<BitmapSource> _resultChannel = Channel.CreateBounded<BitmapSource>(new BoundedChannelOptions(5)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = true
    });

    public ImageLoaderService()
    {
        _ = ProcessRequestsAsync();
    }

    public async ValueTask EnqueueAsync(string filePath)
    {
        await _requestChannel.Writer.WriteAsync(filePath);
    }

    public IAsyncEnumerable<BitmapSource> GetImageStreamAsync(CancellationToken cancellationToken = default)
    {
        return _resultChannel.Reader.ReadAllAsync(cancellationToken);
    }

    private async Task ProcessRequestsAsync()
    {
        while (await _requestChannel.Reader.WaitToReadAsync())
        {
            while (_requestChannel.Reader.TryRead(out var filePath))
            {
                try
                {
                    if (!File.Exists(filePath)) continue;

                    // OpenCVで読み込み
                    using var mat = new Mat(filePath, ImreadModes.Color);
                    if (mat.Empty()) continue;

                    // WPF用のBitmapSourceに変換 (WPF UIスレッド以外でも作成可能だが、Freezeが必要)
                    var bitmap = mat.ToWriteableBitmap();
                    bitmap.Freeze(); // 別スレッドに渡すために必須

                    await _resultChannel.Writer.WriteAsync(bitmap);
                }
                catch (Exception ex)
                {
                    // 本来はロギング
                    System.Diagnostics.Debug.WriteLine($"Error loading image {filePath}: {ex.Message}");
                }
            }
        }
    }
}
