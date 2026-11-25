using Labs.Common;
using Microsoft.Extensions.AI;
using System.ComponentModel;

namespace Phase02.Tools.Core;

/// <summary>
/// Tools for the signed-in employee only. No tool takes an employee or tenant parameter:
/// the model can't ask for someone else's data, because there is no way to express it.
/// </summary>
public sealed class PayrollTools(PayrollStore store, CurrentUser user)
{
    [Description("Gets the signed-in employee's profile: first name, the country they are paid in, the currency of their pay and their monthly base gross pay. Use it for any question about the employee themselves.")]
    public EmployeeView GetEmployee()
    {
        var employee = CurrentEmployee();
        return new EmployeeView(employee.FirstName, CountryNameOf(employee), CurrencyOf(employee), employee.MonthlyGross);
    }

    [Description("Gets the signed-in employee's payslip for one month. Returns Found = false with a message when there is no payslip.")]
    public PayslipLookup GetPayslip(
        [Description("Four-digit year, for example 2026.")] int year,
        [Description("Month number from 1 to 12.")] int month)
    {
        // Bad input from the model becomes a message it can act on, not a crash
        if (month is < 1 or > 12)
        {
            return PayslipLookup.NotFound($"Month must be between 1 and 12, got {month}.");
        }

        if (year is < 2000 or > 2100)
        {
            return PayslipLookup.NotFound($"Year must be between 2000 and 2100, got {year}.");
        }

        var employee = CurrentEmployee();
        var period = new DateOnly(year, month, 1);
        var payslip = store.PayslipsFor(employee.Id).SingleOrDefault(p => p.Period == period);

        return payslip is null
            ? PayslipLookup.NotFound($"No payslip for {period:yyyy-MM}.")
            : PayslipLookup.From(payslip, CurrencyOf(employee));
    }

    [Description("Calculates income tax, social contributions and net pay for a monthly gross amount. Use it for every 'what if' question about pay.")]
    public NetPayCalculation CalculateNetPay(
        [Description("Monthly gross pay as a plain number, without a currency symbol.")] decimal gross)
    {
        // Impossible input is a bug in the model's call: throw, and the model gets a generic error
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(gross);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(gross, 1_000_000m);

        var tax = Math.Round(gross * SyntheticPayroll.IncomeTaxRate, 2);
        var social = Math.Round(gross * SyntheticPayroll.SocialContributionRate, 2);

        return new NetPayCalculation(CurrencyOf(CurrentEmployee()), gross, tax, social, gross - tax - social);
    }

    public IList<AITool> AsAITools() =>
    [
        AIFunctionFactory.Create(GetEmployee),
        AIFunctionFactory.Create(GetPayslip),
        AIFunctionFactory.Create(CalculateNetPay),
    ];

    // Defence in depth: the employee must exist in the user's own tenant
    private Employee CurrentEmployee() =>
        store.Employees.SingleOrDefault(e => e.Id == user.EmployeeId && e.TenantId == user.TenantId)
        ?? throw new InvalidOperationException("The signed-in employee was not found in their tenant.");

    // Pay is in the currency of the country the employee is paid in
    private static string CurrencyOf(Employee employee) => employee.Country switch
    {
        "MK" => "MKD",
        "RS" => "RSD",
        var country => throw new InvalidOperationException($"No currency configured for country '{country}'."),
    };

    // A name, not a code: the model decoded "RS" as Russia
    private static string CountryNameOf(Employee employee) => employee.Country switch
    {
        "MK" => "Macedonia",
        "RS" => "Serbia",
        var country => throw new InvalidOperationException($"No name configured for country '{country}'."),
    };
}