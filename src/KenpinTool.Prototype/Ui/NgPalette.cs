using System.Windows.Media;

namespace KenpinTool.Prototype;

public static class NgPalette
{
    private static readonly Brush Ok = Freeze(new SolidColorBrush(Color.FromRgb(0x28, 0xA7, 0x45)));     // #28A745
    private static readonly Brush NgA = Freeze(new SolidColorBrush(Color.FromRgb(0xDC, 0x35, 0x45)));    // #DC3545
    private static readonly Brush NgB = Freeze(new SolidColorBrush(Color.FromRgb(0xFD, 0x7E, 0x14)));    // #FD7E14
    private static readonly Brush NgC = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x07)));    // #FFC107
    private static readonly Brush Wait = Freeze(new SolidColorBrush(Color.FromRgb(0x6C, 0x75, 0x7D)));   // #6C757D

    public static Brush LevelBrush(NgLevel level) =>
        level switch
        {
            NgLevel.NgA => NgA,
            NgLevel.NgB => NgB,
            NgLevel.NgC => NgC,
            _ => Ok,
        };

    public static Brush WaitBrush() => Wait;

    private static Brush Freeze(Brush brush)
    {
        if (brush.CanFreeze)
        {
            brush.Freeze();
        }

        return brush;
    }
}

