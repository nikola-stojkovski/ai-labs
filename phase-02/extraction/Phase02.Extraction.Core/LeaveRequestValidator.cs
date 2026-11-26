namespace Phase02.Extraction.Core;

/// <summary>
/// Rules a parsed LeaveRequest must follow. Deterministic: the same output always gets the same verdict,
/// whether the model was confused or manipulated.
/// </summary>
public static class LeaveRequestValidator
{
    public const int MaxDaysPerPeriod = 30;

    public static IReadOnlyList<string> Validate(LeaveRequest request, DateOnly today)
    {
        var errors = new List<string>();

        // JSON can leave a non-nullable property null: C# nullability isn't checked when deserializing
        var periods = request.Periods ?? [];

        if (periods.Count == 0 && string.IsNullOrWhiteSpace(request.FollowUpQuestion))
        {
            errors.Add("No leave periods and no follow-up question.");
        }

        foreach (var period in periods)
        {
            var range = $"{period.Start:yyyy-MM-dd} to {period.End:yyyy-MM-dd}";

            if (period.End < period.Start)
            {
                errors.Add($"Period {range} ends before it starts.");
            }

            if (period.HalfDay && period.Start != period.End)
            {
                errors.Add($"Half day {range} spans more than one day.");
            }

            if (period.Start < today.AddDays(-31) || period.End > today.AddYears(1))
            {
                errors.Add($"Period {range} is not between a month before and a year after {today:yyyy-MM-dd}.");
            }

            if (period.End.DayNumber - period.Start.DayNumber + 1 > MaxDaysPerPeriod)
            {
                errors.Add($"Period {range} is longer than {MaxDaysPerPeriod} days and needs HR.");
            }
        }

        var ordered = periods.OrderBy(period => period.Start).ToList();
        for (var i = 1; i < ordered.Count; i++)
        {
            if (ordered[i].Start <= ordered[i - 1].End)
            {
                errors.Add($"Periods starting {ordered[i - 1].Start:yyyy-MM-dd} and {ordered[i].Start:yyyy-MM-dd} overlap.");
            }
        }

        return errors;
    }
}