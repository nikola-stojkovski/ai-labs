using Labs.Common;
using Microsoft.Extensions.AI;
using Phase02.Extraction.Core;
using Xunit;

namespace Phase02.Extraction.Tests;

public class LeaveExtractorTests
{
    private static readonly DateOnly Today = new(2026, 11, 25);

    [Fact]
    public async Task Valid_json_becomes_a_valid_request()
    {
        const string json = """
            {"periods":[{"start":"2027-05-12","end":"2027-05-16","halfDay":false},
                        {"start":"2027-05-17","end":"2027-05-17","halfDay":true}],
             "followUpQuestion":null}
            """;

        var (result, _) = await ExtractWith(json);

        Assert.Equal(ExtractionStatus.Valid, result.Status);
        Assert.Equal(2, result.Request!.Periods.Count);
        Assert.True(result.Request.Periods[1].HalfDay);
    }

    [Fact]
    public async Task Chatty_text_is_unparseable_not_an_exception()
    {
        var (result, _) = await ExtractWith("Sure! You'll be off from 12 to 16 May, plus a half day on the 17th.");

        Assert.Equal(ExtractionStatus.Unparseable, result.Status);
        Assert.Null(result.Request);
    }

    [Fact]
    public async Task An_impossible_date_is_unparseable()
    {
        var (result, _) = await ExtractWith("""{"periods":[{"start":"2027-02-30","end":"2027-03-02","halfDay":false}],"followUpQuestion":null}""");

        Assert.Equal(ExtractionStatus.Unparseable, result.Status);
    }

    [Fact]
    public async Task Valid_json_that_breaks_a_rule_is_invalid()
    {
        var (result, _) = await ExtractWith("""{"periods":[{"start":"2027-05-16","end":"2027-05-12","halfDay":false}],"followUpQuestion":null}""");

        Assert.Equal(ExtractionStatus.Invalid, result.Status);
        Assert.Contains(result.Errors, error => error.Contains("ends before it starts"));
    }

    [Fact]
    public async Task The_prompt_gets_today_and_its_weekday_from_the_app()
    {
        var (_, fake) = await ExtractWith("""{"periods":[],"followUpQuestion":"Which days?"}""");

        var system = fake.LastMessages.Single(message => message.Role == ChatRole.System);
        Assert.Contains("Today is 2026-11-25 (Wednesday)", system.Text);
    }

    [Fact]
    public async Task The_json_schema_is_sent_as_the_response_format()
    {
        var (_, fake) = await ExtractWith("""{"periods":[],"followUpQuestion":"Which days?"}""");

        var format = Assert.IsType<ChatResponseFormatJson>(fake.LastOptions!.ResponseFormat);
        Assert.NotNull(format.Schema);
    }

    private static async Task<(ExtractionResult Result, FakeChatClient Fake)> ExtractWith(string modelOutput)
    {
        var fake = new FakeChatClient(modelOutput);
        var result = await new LeaveExtractor(fake).ExtractAsync(
            "I'm off 12-16 May, half day on the 17th.", Today, cancellationToken: TestContext.Current.CancellationToken);
        return (result, fake);
    }
}