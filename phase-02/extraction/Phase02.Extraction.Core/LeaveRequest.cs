using System.ComponentModel;

namespace Phase02.Extraction.Core;

[Description("Leave dates extracted from an employee's message.")]
public sealed record LeaveRequest(
    [property: Description("Leave periods stated in the message. Empty when the dates are missing or unclear.")]
    IReadOnlyList<LeavePeriod> Periods,
    [property: Description("A short question to ask the employee when dates are missing or unclear, otherwise null.")]
    string? FollowUpQuestion);

public sealed record LeavePeriod(
    [property: Description("First day of leave, YYYY-MM-DD.")]
    DateOnly Start,
    [property: Description("Last day of leave, YYYY-MM-DD. The same as Start for a single day.")]
    DateOnly End,
    [property: Description("True when only half of the day is taken. A half day is always a single day.")]
    bool HalfDay);