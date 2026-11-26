using Phase02.Extraction.Core;
using Xunit;

namespace Phase02.Extraction.Tests;

public class LeaveResolverTests
{
    private static readonly DateOnly Today = new(2026, 11, 25);   // a Wednesday

    private static SaidDate On(int day, int month) => new(DateKind.Date, day, month, null, false);
    private static SaidDate Word(DateKind kind) => new(kind, null, null, null, false);
    private static SaidDate Weekday(string name, bool next = false) => new(DateKind.Weekday, null, null, name, next);

    private static LeaveRequest Resolve(params LeaveMention[] mentions) =>
        LeaveResolver.Resolve(new LeaveMentions(mentions), Today);

    [Fact]
    public void A_month_already_past_this_year_means_next_year()
    {
        var request = Resolve(new LeaveMention(On(12, 5), On(16, 5), false));

        Assert.Equal(new LeavePeriod(new(2027, 5, 12), new(2027, 5, 16), false), Assert.Single(request.Periods));
    }

    [Fact]
    public void A_period_can_run_into_the_next_year()
    {
        var request = Resolve(new LeaveMention(On(28, 12), On(3, 1), false));

        Assert.Equal(new LeavePeriod(new(2026, 12, 28), new(2027, 1, 3), false), Assert.Single(request.Periods));
    }

    [Fact]
    public void Today_and_tomorrow_are_calculated_in_code()
    {
        var request = Resolve(new LeaveMention(Word(DateKind.Today), Word(DateKind.Tomorrow), false));

        Assert.Equal(new LeavePeriod(new(2026, 11, 25), new(2026, 11, 26), false), Assert.Single(request.Periods));
    }

    [Fact]
    public void A_weekday_is_the_coming_one()
    {
        var request = Resolve(new LeaveMention(Weekday("Friday"), Weekday("Friday"), false));

        Assert.Equal(new DateOnly(2026, 11, 27), Assert.Single(request.Periods).Start);
    }

    [Fact]
    public void Next_weekday_is_ambiguous_so_it_asks()
    {
        var request = Resolve(new LeaveMention(Weekday("Friday", next: true), null, false));

        Assert.Empty(request.Periods);
        Assert.Equal("Do you mean 27 November 2026 or 4 December 2026?", request.FollowUpQuestion);
    }

    [Fact]
    public void An_impossible_date_gets_a_useful_question()
    {
        var request = Resolve(new LeaveMention(On(30, 2), On(2, 3), false));

        Assert.Empty(request.Periods);
        Assert.Equal("30 February isn't a valid date. Which day did you mean?", request.FollowUpQuestion);
    }

    [Fact]
    public void A_missing_end_is_asked_for()
    {
        var request = Resolve(new LeaveMention(Word(DateKind.Tomorrow), null, false));

        Assert.Equal("Your leave starts on 26 November 2026. On which day does it end?", request.FollowUpQuestion);
    }

    [Fact]
    public void A_half_day_without_an_end_is_a_single_day()
    {
        var request = Resolve(new LeaveMention(On(17, 5), null, true));

        Assert.Equal(new LeavePeriod(new(2027, 5, 17), new(2027, 5, 17), true), Assert.Single(request.Periods));
    }

    [Fact]
    public void No_mentions_means_asking_for_the_days()
    {
        var request = Resolve();

        Assert.Equal("Which days would you like to take off?", request.FollowUpQuestion);
    }
}