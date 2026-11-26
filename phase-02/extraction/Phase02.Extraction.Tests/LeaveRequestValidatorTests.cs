using Phase02.Extraction.Core;
using Xunit;

namespace Phase02.Extraction.Tests;

public class LeaveRequestValidatorTests
{
    private static readonly DateOnly Today = new(2026, 11, 25);

    private static LeavePeriod Period(string start, string end, bool halfDay = false) =>
        new(DateOnly.Parse(start), DateOnly.Parse(end), halfDay);

    [Fact]
    public void Accepts_full_days_followed_by_a_half_day()
    {
        var request = new LeaveRequest(
            [Period("2027-05-12", "2027-05-16"), Period("2027-05-17", "2027-05-17", halfDay: true)],
            null);

        Assert.Empty(LeaveRequestValidator.Validate(request, Today));
    }

    [Fact]
    public void Accepts_no_periods_when_there_is_a_follow_up_question()
    {
        var request = new LeaveRequest([], "Which days around the holidays would you like off?");

        Assert.Empty(LeaveRequestValidator.Validate(request, Today));
    }

    [Fact]
    public void Rejects_no_periods_without_a_follow_up_question()
    {
        var errors = LeaveRequestValidator.Validate(new LeaveRequest([], null), Today);

        Assert.Contains("No leave periods and no follow-up question.", errors);
    }

    [Fact]
    public void Treats_a_null_list_from_json_as_empty()
    {
        var errors = LeaveRequestValidator.Validate(new LeaveRequest(null!, null), Today);

        Assert.Contains("No leave periods and no follow-up question.", errors);
    }

    [Fact]
    public void Rejects_a_period_that_ends_before_it_starts()
    {
        var errors = LeaveRequestValidator.Validate(new LeaveRequest([Period("2026-12-10", "2026-12-05")], null), Today);

        Assert.Contains(errors, error => error.Contains("ends before it starts"));
    }

    [Fact]
    public void Rejects_a_half_day_that_spans_several_days()
    {
        var errors = LeaveRequestValidator.Validate(new LeaveRequest([Period("2026-12-01", "2026-12-03", halfDay: true)], null), Today);

        Assert.Contains(errors, error => error.Contains("spans more than one day"));
    }

    [Fact]
    public void Rejects_dates_more_than_a_year_ahead()
    {
        var errors = LeaveRequestValidator.Validate(new LeaveRequest([Period("2028-05-12", "2028-05-16")], null), Today);

        Assert.Contains(errors, error => error.Contains("a year after"));
    }

    [Fact]
    public void Sends_periods_longer_than_30_days_to_hr()
    {
        var errors = LeaveRequestValidator.Validate(new LeaveRequest([Period("2026-12-01", "2027-03-10")], null), Today);

        Assert.Contains(errors, error => error.Contains("needs HR"));
    }

    [Fact]
    public void Rejects_overlapping_periods()
    {
        var request = new LeaveRequest([Period("2026-12-01", "2026-12-05"), Period("2026-12-04", "2026-12-06")], null);

        Assert.Contains(LeaveRequestValidator.Validate(request, Today), error => error.Contains("overlap"));
    }
}