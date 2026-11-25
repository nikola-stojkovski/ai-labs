using Labs.Common;
using System.Globalization;

namespace Phase02.Tools.Core;

public sealed record EmployeeView(string FirstName, string Country, decimal MonthlyBaseGross);

public sealed record PayslipView(
    string Period,
    decimal Gross,
    decimal IncomeTax,
    decimal SocialContributions,
    decimal Net,
    bool HasBonus);

/// <summary>
/// "Not found" is a normal result the model should pass on, not an exception.
/// </summary>
public sealed record PayslipLookup(bool Found, string? Message, PayslipView? Payslip)
{
    public static PayslipLookup NotFound(string message) => new(false, message, null);

    public static PayslipLookup From(Payslip payslip) => new(
        true,
        null,
        new PayslipView(
            payslip.Period.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            payslip.Gross,
            payslip.IncomeTax,
            payslip.SocialContributions,
            payslip.Net,
            payslip.HasBonus));
}

public sealed record NetPayCalculation(decimal Gross, decimal IncomeTax, decimal SocialContributions, decimal Net);