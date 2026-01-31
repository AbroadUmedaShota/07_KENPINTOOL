using System.Collections.Generic;

namespace KenpinTool.Prototype;

public sealed record ExceptionDialogRequest(IReadOnlyList<string> ReasonCodeOptions);

