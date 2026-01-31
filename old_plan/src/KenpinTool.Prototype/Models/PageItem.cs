using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace KenpinTool.Prototype;

public sealed class PageItem : ObservableObject
{
    private PageDecision? _decision;
    private bool _isAnalyzed;

    public PageItem(int index, string filePath, IReadOnlyList<Detection> detections, int? pdfPageIndex = null)
    {
        if (index <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        if (pdfPageIndex.HasValue && pdfPageIndex.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pdfPageIndex));
        }

        Index = index;
        FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        FileName = Path.GetFileName(filePath);
        PdfPageIndex = pdfPageIndex;

        Detections = new ObservableCollection<Detection>(detections ?? Array.Empty<Detection>());
        HookDetections(Detections);
        Detections.CollectionChanged += DetectionsOnCollectionChanged;
    }

    public int Index { get; }
    public string FilePath { get; }
    public string FileName { get; }
    public int? PdfPageIndex { get; }
    public bool IsPdf => PdfPageIndex.HasValue;

    public ObservableCollection<Detection> Detections { get; }

    public PageDecision? Decision
    {
        get => _decision;
        private set
        {
            if (SetProperty(ref _decision, value))
            {
                InvalidateComputed();
            }
        }
    }

    public bool IsReviewed => Decision is not null;

    public bool IsAnalyzed
    {
        get => _isAnalyzed;
        private set => SetProperty(ref _isAnalyzed, value);
    }

    public bool HasActiveDetections => Detections.Any(d => d.IsActive);

    public bool HasFatalActiveDetections => Detections.Any(d => d.IsActive && d.Level == NgLevel.NgA);

    public bool HasQlT05ActiveDetections => Detections.Any(d => d.IsActive && d.IsQlT05);

    public string BadgeText
    {
        get
        {
            if (Decision is not null)
            {
                return Decision.Action switch
                {
                    DecisionAction.Ok => "OK",
                    DecisionAction.Rescan => "NG-A",
                    DecisionAction.ExceptionApproved => "EXC",
                    _ => "OK",
                };
            }

            if (HasFatalActiveDetections)
            {
                return "NG";
            }

            if (HasActiveDetections)
            {
                return "疑";
            }

            return "未";
        }
    }

    public Brush BadgeBrush
    {
        get
        {
            if (Decision is not null)
            {
                return Decision.Action switch
                {
                    DecisionAction.Ok => NgPalette.LevelBrush(NgLevel.Ok),
                    DecisionAction.Rescan => NgPalette.LevelBrush(NgLevel.NgA),
                    DecisionAction.ExceptionApproved => NgPalette.LevelBrush(NgLevel.NgC),
                    _ => NgPalette.LevelBrush(NgLevel.Ok),
                };
            }

            if (HasFatalActiveDetections)
            {
                return NgPalette.LevelBrush(NgLevel.NgA);
            }

            if (HasActiveDetections)
            {
                return NgPalette.LevelBrush(NgLevel.NgC);
            }

            return NgPalette.WaitBrush();
        }
    }

    public string SummaryText
    {
        get
        {
            var activeCodes = Detections.Where(d => d.IsActive).Select(d => d.Code).ToArray();
            if (activeCodes.Length == 0)
            {
                return FileName;
            }

            var top = string.Join(", ", activeCodes.Take(2));
            var more = activeCodes.Length > 2 ? "…" : "";
            return $"{FileName}  {top}{more}";
        }
    }

    public IReadOnlyList<Detection> ActiveDetections()
        => Detections.Where(d => d.IsActive).ToArray();

    public void ApplyOk(string? reasonCode = null, string? note = null)
    {
        foreach (var detection in Detections)
        {
            detection.IsActive = false;
        }

        Decision = new PageDecision(
            DecisionAction.Ok,
            DateTimeOffset.UtcNow,
            ExceptionReasonCode: string.IsNullOrWhiteSpace(reasonCode) ? null : reasonCode.Trim(),
            ExceptionNote: string.IsNullOrWhiteSpace(note) ? null : note.Trim());
    }

    public void MarkAnalyzed()
    {
        if (IsAnalyzed)
        {
            return;
        }

        IsAnalyzed = true;
    }

    public void RestoreDecision(PageDecision decision)
    {
        if (decision is null)
        {
            throw new ArgumentNullException(nameof(decision));
        }

        if (decision.Action == DecisionAction.Ok || decision.Action == DecisionAction.ExceptionApproved)
        {
            foreach (var detection in Detections)
            {
                detection.IsActive = false;
            }
        }
        else if (decision.Action == DecisionAction.Rescan)
        {
            EscalateSuspicionCodes();
        }

        Decision = decision;
    }

    public void ApplyRescan()
    {
        EscalateSuspicionCodes();
        Decision = new PageDecision(DecisionAction.Rescan, DateTimeOffset.UtcNow);
    }

    public void ApplyException(string reasonCode, string? note)
    {
        if (string.IsNullOrWhiteSpace(reasonCode))
        {
            throw new ArgumentException("Reason code is required.", nameof(reasonCode));
        }

        foreach (var detection in Detections.Where(d => d.IsActive))
        {
            detection.IsActive = false;
        }

        Decision = new PageDecision(
            DecisionAction.ExceptionApproved,
            DateTimeOffset.UtcNow,
            ExceptionReasonCode: reasonCode.Trim(),
            ExceptionNote: string.IsNullOrWhiteSpace(note) ? null : note.Trim());
    }

    private void EscalateSuspicionCodes()
    {
        // Prototype rule: STR-01S/03S -> STR-01/03 when RESCAN is chosen.
        ReplaceSuspicion("STR-01S", "STR-01", NgLevel.NgA, SuggestedAction.Rescan, ReworkType.None);
        ReplaceSuspicion("STR-03S", "STR-03", NgLevel.NgA, SuggestedAction.Rescan, ReworkType.None);
    }

    private void ReplaceSuspicion(string fromCode, string toCode, NgLevel toLevel, SuggestedAction suggestedAction, ReworkType reworkType)
    {
        var targets = Detections.Where(d => d.IsActive && string.Equals(d.Code, fromCode, StringComparison.Ordinal)).ToArray();
        if (targets.Length == 0)
        {
            return;
        }

        foreach (var t in targets)
        {
            var index = Detections.IndexOf(t);
            Detections.RemoveAt(index);
            Detections.Insert(
                index,
                new Detection(
                    toCode,
                    t.Name,
                    toLevel,
                    suggestedAction,
                    reworkType,
                    confidence: t.Confidence,
                    evidence: t.Evidence));
        }
    }

    private void DetectionsOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (var obj in e.OldItems.OfType<Detection>())
            {
                obj.PropertyChanged -= DetectionOnPropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (var obj in e.NewItems.OfType<Detection>())
            {
                obj.PropertyChanged += DetectionOnPropertyChanged;
            }
        }

        InvalidateComputed();
    }

    private void HookDetections(IEnumerable<Detection> detections)
    {
        foreach (var detection in detections)
        {
            detection.PropertyChanged += DetectionOnPropertyChanged;
        }
    }

    private void DetectionOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        => InvalidateComputed();

    private void InvalidateComputed()
    {
        OnPropertyChanged(nameof(IsReviewed));
        OnPropertyChanged(nameof(HasActiveDetections));
        OnPropertyChanged(nameof(HasFatalActiveDetections));
        OnPropertyChanged(nameof(HasQlT05ActiveDetections));
        OnPropertyChanged(nameof(BadgeText));
        OnPropertyChanged(nameof(BadgeBrush));
        OnPropertyChanged(nameof(SummaryText));
    }
}

