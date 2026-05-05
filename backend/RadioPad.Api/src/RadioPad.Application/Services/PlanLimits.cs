using RadioPad.Domain.Enums;

namespace RadioPad.Application.Services;

/// <summary>
/// PRD BILL-001 — per-plan quota table. Used by <see cref="PlanQuotaService"/>
/// to gate AI calls and surface plan information in 402 responses.
/// </summary>
public record PlanLimit(
    int AiCallsPerMonth,
    int InputTokensPerMonth,
    int OutputTokensPerMonth,
    int Seats);

public static class PlanLimits
{
    public static PlanLimit For(TenantPlan plan) => plan switch
    {
        TenantPlan.Trial => new PlanLimit(
            AiCallsPerMonth: 100,
            InputTokensPerMonth: 50_000,
            OutputTokensPerMonth: 25_000,
            Seats: 5),
        TenantPlan.Team => new PlanLimit(
            AiCallsPerMonth: 10_000,
            InputTokensPerMonth: 5_000_000,
            OutputTokensPerMonth: 2_000_000,
            Seats: 25),
        TenantPlan.Enterprise => new PlanLimit(
            AiCallsPerMonth: int.MaxValue,
            InputTokensPerMonth: int.MaxValue,
            OutputTokensPerMonth: int.MaxValue,
            Seats: int.MaxValue),
        _ => For(TenantPlan.Trial),
    };
}
