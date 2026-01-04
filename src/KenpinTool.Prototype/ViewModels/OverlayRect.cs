using System.Windows.Media;

namespace KenpinTool.Prototype;

public sealed record OverlayRect(
    double X,
    double Y,
    double Width,
    double Height,
    Brush Stroke,
    string Label,
    double Opacity
);

