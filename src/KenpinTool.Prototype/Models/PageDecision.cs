using System;

namespace KenpinTool.Prototype;

public sealed record PageDecision(
    DecisionAction Action,
    DateTimeOffset TimestampUtc,
    string? ExceptionReasonCode = null,
    string? ExceptionNote = null
);

