using Bogus;

namespace Labs.Common;

public sealed record Employee(
    Guid Id,
    string TenantId,
    string FirstName,
    string LastName,
    string Country,
    decimal MonthlyGross);

public sealed record Payslip(
    Guid EmployeeId,
    DateOnly Period,
    decimal Gross,
    decimal IncomeTax,
    decimal SocialContributions,
    decimal Net,
    bool HasBonus);

public static class SyntheticPayroll
{
    // Illustrative rates for test data only, not real tax law
    public const decimal IncomeTaxRate = 0.10m;
    public const decimal SocialContributionRate = 0.28m;

    private static readonly string[] Tenants = ["acme", "globex", "initech"];
    private static readonly string[] Countries = ["MK", "RS"];

    public static IReadOnlyList<Employee> Employees(int count = 50, int seed = 42) =>
        new Faker<Employee>()
            .UseSeed(seed)
            .CustomInstantiator(f => new Employee(
                f.Random.Guid(),
                f.PickRandom(Tenants),
                f.Name.FirstName(),
                f.Name.LastName(),
                f.PickRandom(Countries),
                Math.Round(f.Random.Decimal(800m, 4500m), 2)))
            .Generate(count);

    public static IReadOnlyList<Payslip> Payslips(Employee employee, int year = 2026, int seed = 42)
    {
        var faker = new Faker { Random = new Randomizer(seed) };
        var payslips = new List<Payslip>(12);

        for (var month = 1; month <= 12; month++)
        {
            var hasBonus = faker.Random.Bool(0.1f);                          // ~1 in 10 months
            var variation = faker.Random.Decimal(-0.03m, 0.03m);             // overtime, unpaid leave...
            var gross = Math.Round(employee.MonthlyGross * (1 + variation) * (hasBonus ? 1.5m : 1m), 2);
            var tax = Math.Round(gross * IncomeTaxRate, 2);
            var social = Math.Round(gross * SocialContributionRate, 2);

            payslips.Add(new Payslip(
                employee.Id,
                new DateOnly(year, month, 1),
                gross,
                tax,
                social,
                gross - tax - social,
                hasBonus));
        }

        return payslips;
    }
}