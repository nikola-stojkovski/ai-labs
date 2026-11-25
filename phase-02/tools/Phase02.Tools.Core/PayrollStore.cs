using Labs.Common;

namespace Phase02.Tools.Core;

/// <summary>
/// In-memory stand-in for the payroll database: 50 synthetic employees, 12 payslips each (2026).
/// </summary>
public sealed class PayrollStore
{
    private readonly Dictionary<Guid, IReadOnlyList<Payslip>> _payslips;

    public PayrollStore(int employeeCount = 50)
    {
        Employees = SyntheticPayroll.Employees(employeeCount);
        _payslips = Employees.ToDictionary(employee => employee.Id, employee => SyntheticPayroll.Payslips(employee));
    }

    public IReadOnlyList<Employee> Employees { get; }

    public IReadOnlyList<Payslip> PayslipsFor(Guid employeeId) => _payslips[employeeId];
}