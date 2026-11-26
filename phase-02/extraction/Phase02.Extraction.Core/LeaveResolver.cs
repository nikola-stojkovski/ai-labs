using System.Globalization;

namespace Phase02.Extraction.Core;

/// <summary>
/// Turns dates as said into real dates. Picks the year, finds weekdays, rejects impossible dates,
/// and asks instead of guessing when something is ambiguous or missing.
/// </summary>
public static class LeaveResolver
{
    public static LeaveRequest Resolve(LeaveMentions mentions, DateOnly today)
    {
        var items = mentions.Mentions ?? [];
        if (items.Count == 0)
        {
            return Ask("Which days would you like to take off?");
        }

        var periods = new List<LeavePeriod>();

        foreach (var mention in items)
        {
            if (mention.Start is null)
            {
                return Ask("On which day should your leave start?");
            }

            var start = ResolveDate(mention.Start, today, notBefore: null);
            if (start.Question is not null)
            {
                return Ask(start.Question);
            }

            // A half day is always a single day, even if the model left End empty
            if (mention.End is null && !mention.HalfDay)
            {
                return Ask($"Your leave starts on {Describe(start.Date)}. On which day does it end?");
            }

            var end = mention.End is null ? start : ResolveDate(mention.End, today, notBefore: start.Date);
            if (end.Question is not null)
            {
                return Ask(end.Question);
            }

            periods.Add(new LeavePeriod(start.Date, end.Date, mention.HalfDay));
        }

        return new LeaveRequest(periods, null);
    }

    private static Resolved ResolveDate(SaidDate said, DateOnly today, DateOnly? notBefore)
    {
        switch (said.Kind)
        {
            case DateKind.Today:
                return new(today, null);

            case DateKind.Tomorrow:
                return new(today.AddDays(1), null);

            case DateKind.Weekday:
                if (!Enum.TryParse<DayOfWeek>(said.Weekday, ignoreCase: true, out var weekday))
                {
                    return new(default, "Which exact day did you mean?");
                }

                var coming = NextOnOrAfter(notBefore ?? today.AddDays(1), weekday);

                // "Next Friday" on a Wednesday: this Friday or the one after? Ask instead of guessing.
                return said.Next
                    ? new(default, $"Do you mean {Describe(coming)} or {Describe(coming.AddDays(7))}?")
                    : new(coming, null);

            case DateKind.Date when said.Day is { } day && said.Month is { } month:
                if (month is < 1 or > 12 || day < 1)
                {
                    return new(default, "Which exact day did you mean?");
                }

                // A date without a year is the first one on or after the reference day
                var from = notBefore ?? today;
                for (var year = from.Year; year <= from.Year + 1; year++)
                {
                    if (day <= DateTime.DaysInMonth(year, month) && new DateOnly(year, month, day) >= from)
                    {
                        return new(new DateOnly(year, month, day), null);
                    }
                }

                return new(default, $"{day} {MonthName(month)} isn't a valid date. Which day did you mean?");

            default:
                return new(default, "Which exact day did you mean?");
        }
    }

    private static DateOnly NextOnOrAfter(DateOnly from, DayOfWeek weekday) =>
        from.AddDays(((int)weekday - (int)from.DayOfWeek + 7) % 7);

    private static string Describe(DateOnly date) => date.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);

    private static string MonthName(int month) => CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(month);

    private static LeaveRequest Ask(string question) => new([], question);

    private readonly record struct Resolved(DateOnly Date, string? Question);
}