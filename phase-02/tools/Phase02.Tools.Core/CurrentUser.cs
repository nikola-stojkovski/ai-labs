namespace Phase02.Tools.Core;

/// <summary>
/// The signed-in employee. In PayPilot this comes from the auth token's claims, never from the model.
/// </summary>
public sealed record CurrentUser(string TenantId, Guid EmployeeId);