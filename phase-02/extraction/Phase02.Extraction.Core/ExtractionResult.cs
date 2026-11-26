namespace Phase02.Extraction.Core;

public enum ExtractionStatus
{
    Valid,
    Invalid,       // parsed, but breaks a business rule
    Unparseable,   // not a LeaveRequest at all
}

public sealed record ExtractionResult(
    ExtractionStatus Status,
    LeaveRequest? Request,
    IReadOnlyList<string> Errors,
    string RawOutput);