using System;
using System.Collections.Generic;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace KenpinTool.Prototype;

public sealed class Detection : ObservableObject
{
    private bool _isActive = true;

    public Detection(
        string code,
        string name,
        NgLevel level,
        SuggestedAction suggestedAction,
        ReworkType reworkType,
        double? confidence = null,
        IReadOnlyList<EvidenceRegion>? evidence = null)
    {
        Code = code ?? throw new ArgumentNullException(nameof(code));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Level = level;
        SuggestedAction = suggestedAction;
        ReworkType = reworkType;
        Confidence = confidence;
        Evidence = evidence ?? Array.Empty<EvidenceRegion>();
    }

    public string Code { get; }
    public string Name { get; }
    public NgLevel Level { get; private set; }
    public SuggestedAction SuggestedAction { get; private set; }
    public ReworkType ReworkType { get; private set; }
    public double? Confidence { get; }
    public IReadOnlyList<EvidenceRegion> Evidence { get; }

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (SetProperty(ref _isActive, value))
            {
                OnPropertyChanged(nameof(DisplayText));
                OnPropertyChanged(nameof(DisplayOpacity));
            }
        }
    }

    public bool IsSuspicion => Code.EndsWith("S", StringComparison.Ordinal);
    public bool IsFatal => Level == NgLevel.NgA;
    public bool IsQlT05 => string.Equals(Code, "QLT-05", StringComparison.OrdinalIgnoreCase);

    public Brush LevelBrush => NgPalette.LevelBrush(Level);

    public double DisplayOpacity => IsActive ? 1.0 : 0.5;

    public string DisplayText
    {
        get
        {
            var conf = Confidence is null ? "" : $" (conf {(int)Math.Round(Confidence.Value * 100)}%)";
            var inactive = IsActive ? "" : " [解除]";
            return $"{Code} [{LevelText(Level)}] {Name}{conf}{inactive}";
        }
    }

    public void Escalate(string newCode, NgLevel newLevel, SuggestedAction suggestedAction, ReworkType reworkType)
    {
        if (string.IsNullOrWhiteSpace(newCode))
        {
            throw new ArgumentException("Code is required.", nameof(newCode));
        }

        if (string.Equals(Code, newCode, StringComparison.Ordinal))
        {
            return;
        }

        // This prototype keeps Code immutable for binding simplicity; escalation creates a new Detection.
        throw new NotSupportedException("Escalation requires replacing the Detection instance.");
    }

    public static string LevelText(NgLevel level) =>
        level switch
        {
            NgLevel.NgA => "NG-A",
            NgLevel.NgB => "NG-B",
            NgLevel.NgC => "NG-C",
            _ => "OK",
        };

    public override string ToString() => DisplayText;
}
