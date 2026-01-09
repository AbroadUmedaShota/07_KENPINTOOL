using CommunityToolkit.Mvvm.ComponentModel;

namespace KenpinTool.Prototype;

public sealed class DetectionSettings : ObservableObject
{
    // Defaults
    public const int DefaultCornerDarkThreshold = 30;
    public const int DefaultCornerSampleSize = 10;
    public const double DefaultAspectRatioMin = 1.1;
    public const double DefaultAspectRatioMax = 1.8;
    public const int DefaultLowResDpi = 50;
    
    public const double DefaultMinLineLengthRatio = 0.55;
    public const double DefaultMaxLineGapRatio = 0.01;
    public const int DefaultHoughThreshold = 100;
    public const double DefaultAngleToleranceDeg = 5.0;
    public const double DefaultCannyThreshold1 = 50;
    public const double DefaultCannyThreshold2 = 150;
    public const int DefaultBlurKernelSize = 3;
    public const double DefaultStr01BlackPixelLimit = 0.005; // 0.5%

    // --- Low Validation (SimpleValidationService) ---

    private int _cornerDarkThreshold = DefaultCornerDarkThreshold;
    public int CornerDarkThreshold
    {
        get => _cornerDarkThreshold;
        set => SetProperty(ref _cornerDarkThreshold, value);
    }

    private int _cornerSampleSize = DefaultCornerSampleSize;
    public int CornerSampleSize
    {
        get => _cornerSampleSize;
        set => SetProperty(ref _cornerSampleSize, value);
    }

    private double _aspectRatioMin = DefaultAspectRatioMin;
    public double AspectRatioMin
    {
        get => _aspectRatioMin;
        set => SetProperty(ref _aspectRatioMin, value);
    }

    private double _aspectRatioMax = DefaultAspectRatioMax;
    public double AspectRatioMax
    {
        get => _aspectRatioMax;
        set => SetProperty(ref _aspectRatioMax, value);
    }

    private int _lowResDpi = DefaultLowResDpi;
    public int LowResDpi
    {
        get => _lowResDpi;
        set => SetProperty(ref _lowResDpi, value);
    }

    // --- High Validation (QualityDetectionService) ---

    private double _minLineLengthRatio = DefaultMinLineLengthRatio;
    public double MinLineLengthRatio
    {
        get => _minLineLengthRatio;
        set => SetProperty(ref _minLineLengthRatio, value);
    }

    private double _maxLineGapRatio = DefaultMaxLineGapRatio;
    public double MaxLineGapRatio
    {
        get => _maxLineGapRatio;
        set => SetProperty(ref _maxLineGapRatio, value);
    }

    private int _houghThreshold = DefaultHoughThreshold;
    public int HoughThreshold
    {
        get => _houghThreshold;
        set => SetProperty(ref _houghThreshold, value);
    }

    private double _angleToleranceDeg = DefaultAngleToleranceDeg;
    public double AngleToleranceDeg
    {
        get => _angleToleranceDeg;
        set => SetProperty(ref _angleToleranceDeg, value);
    }

    private double _cannyThreshold1 = DefaultCannyThreshold1;
    public double CannyThreshold1
    {
        get => _cannyThreshold1;
        set => SetProperty(ref _cannyThreshold1, value);
    }

    private double _cannyThreshold2 = DefaultCannyThreshold2;
    public double CannyThreshold2
    {
        get => _cannyThreshold2;
        set => SetProperty(ref _cannyThreshold2, value);
    }

    private int _blurKernelSize = DefaultBlurKernelSize;
    public int BlurKernelSize
    {
        get => _blurKernelSize;
        set => SetProperty(ref _blurKernelSize, value);
    }

    private double _str01BlackPixelLimit = DefaultStr01BlackPixelLimit;
    public double Str01BlackPixelLimit
    {
        get => _str01BlackPixelLimit;
        set => SetProperty(ref _str01BlackPixelLimit, value);
    }

    public void Reset()
    {
        CornerDarkThreshold = DefaultCornerDarkThreshold;
        CornerSampleSize = DefaultCornerSampleSize;
        AspectRatioMin = DefaultAspectRatioMin;
        AspectRatioMax = DefaultAspectRatioMax;
        LowResDpi = DefaultLowResDpi;
        MinLineLengthRatio = DefaultMinLineLengthRatio;
        MaxLineGapRatio = DefaultMaxLineGapRatio;
        HoughThreshold = DefaultHoughThreshold;
        AngleToleranceDeg = DefaultAngleToleranceDeg;
        CannyThreshold1 = DefaultCannyThreshold1;
        CannyThreshold2 = DefaultCannyThreshold2;
        BlurKernelSize = DefaultBlurKernelSize;
        Str01BlackPixelLimit = DefaultStr01BlackPixelLimit;
    }
}
