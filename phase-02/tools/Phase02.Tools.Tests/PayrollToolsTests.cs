using Labs.Common;
using Microsoft.Extensions.AI;
using Phase02.Tools.Core;
using Xunit;

namespace Phase02.Tools.Tests;

public class PayrollToolsTests
{
    private static readonly PayrollStore Store = new();
    private static readonly Employee Me = Store.Employees[0];
    private static readonly PayrollTools Tools = new(Store, new CurrentUser(Me.TenantId, Me.Id));

    [Fact]
    public void GetPayslip_returns_the_signed_in_employees_payslip()
    {
        var expected = Store.PayslipsFor(Me.Id)[2];

        var lookup = Tools.GetPayslip(2026, 3);

        Assert.True(lookup.Found);
        Assert.Equal("2026-03", lookup.Payslip!.Period);
        Assert.Equal(expected.Net, lookup.Payslip.Net);
    }

    [Fact]
    public void GetPayslip_reports_a_missing_month_instead_of_failing()
    {
        var lookup = Tools.GetPayslip(2025, 11);

        Assert.False(lookup.Found);
        Assert.Null(lookup.Payslip);
        Assert.Equal("No payslip for 2025-11.", lookup.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public void GetPayslip_turns_an_invalid_month_into_a_message(int month)
    {
        var lookup = Tools.GetPayslip(2026, month);

        Assert.False(lookup.Found);
        Assert.Contains("between 1 and 12", lookup.Message);
    }

    [Fact]
    public void CalculateNetPay_does_the_maths_in_code()
    {
        var result = Tools.CalculateNetPay(3000m);

        Assert.Equal(300m, result.IncomeTax);
        Assert.Equal(840m, result.SocialContributions);
        Assert.Equal(1860m, result.Net);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void CalculateNetPay_rejects_a_gross_that_is_not_positive(int gross)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Tools.CalculateNetPay(gross));
    }

    [Fact]
    public void A_user_from_another_tenant_gets_nothing()
    {
        var wrongTenant = new PayrollTools(Store, new CurrentUser("another-tenant", Me.Id));

        Assert.Throws<InvalidOperationException>(() => wrongTenant.GetEmployee());
    }

    [Fact]
    public void No_tool_takes_an_identity_parameter()
    {
        foreach (var tool in Tools.AsAITools().OfType<AIFunction>())
        {
            var parameterNames = tool.JsonSchema.TryGetProperty("properties", out var properties)
                ? properties.EnumerateObject().Select(property => property.Name).ToList()
                : new List<string>();

            Assert.DoesNotContain(parameterNames, name =>
                name.Contains("employee", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("tenant", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("user", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void Every_tool_reaches_the_model_with_a_name_and_a_description()
    {
        var tools = Tools.AsAITools().OfType<AIFunction>().ToList();

        Assert.Equal(
            new[] { "CalculateNetPay", "GetEmployee", "GetPayslip" },
            tools.Select(tool => tool.Name).Order());
        Assert.All(tools, tool => Assert.False(string.IsNullOrWhiteSpace(tool.Description)));
    }
}