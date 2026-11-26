using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Phase02.Extraction.Core;

// Serialised by name ("Date", "Today"...), so the schema lists the allowed values
[JsonConverter(typeof(JsonStringEnumConverter<DateKind>))]
public enum DateKind
{
    Date,
    Today,
    Tomorrow,
    Weekday,
}

[Description("A date exactly as the employee said it, before any calendar maths.")]
public sealed record SaidDate(
    [property: Description("Date: a day and a month were said. Today or Tomorrow: that word was said. Weekday: a day name like Friday was said.")]
    DateKind Kind,
    [property: Description("Day of the month. Only for Kind = Date.")]
    int? Day,
    [property: Description("Month number from 1 to 12. Only for Kind = Date. If one month was said for several days, use it for each of them.")]
    int? Month,
    [property: Description("English day name, like Friday. Only for Kind = Weekday.")]
    string? Weekday,
    [property: Description("True only when the word 'next' was said before the day name.")]
    bool Next);

public sealed record LeaveMention(
    [property: Description("First day of leave, as said.")]
    SaidDate Start,
    [property: Description("Last day of leave, as said. For a single day, the same as Start. Null when the employee didn't say when it ends.")]
    SaidDate? End,
    [property: Description("True when only half of the day is taken.")]
    bool HalfDay);

[Description("Leave dates exactly as the employee said them.")]
public sealed record LeaveMentions(
    [property: Description("Every period of leave the employee asked for. Empty when no dates were said. A return-to-work date is not leave.")]
    IReadOnlyList<LeaveMention> Mentions);