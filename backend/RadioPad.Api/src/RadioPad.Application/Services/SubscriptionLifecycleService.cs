using RadioPad.Domain.Entities;

namespace RadioPad.Application.Services;

/// <summary>
/// PRD BILL-001..006 — pure mapping from a Stripe <c>Subscription.Status</c>
/// string to <see cref="TenantSettings"/> field updates. Performs no DB
/// writes; the webhook controller is responsible for persisting the entity
/// after calling <see cref="Apply"/>.
/// </summary>
public sealed class SubscriptionLifecycleService
{
    private static readonly TimeSpan DunningWindow = TimeSpan.FromDays(7);

    public void Apply(TenantSettings s, string stripeStatus, DateTimeOffset? trialEnd, DateTimeOffset now)
    {
        s.StripeSubscriptionStatus = stripeStatus ?? "";
        switch ((stripeStatus ?? "").ToLowerInvariant())
        {
            case "trialing":
                s.TrialEndsAt = trialEnd;
                s.SuspendedAt = null;
                s.GracePeriodUntil = null;
                break;

            case "active":
                s.SuspendedAt = null;
                s.GracePeriodUntil = null;
                s.TrialEndsAt = null;
                break;

            case "past_due":
            case "unpaid":
                if (s.GracePeriodUntil is null)
                {
                    s.GracePeriodUntil = now + DunningWindow;
                }
                break;

            case "canceled":
            case "incomplete_expired":
                s.SuspendedAt = now;
                s.GracePeriodUntil = null;
                break;
        }
    }

    /// <summary>
    /// Iter-36 — Stripe <c>invoice.payment_failed</c> handler. Opens a 7-day
    /// grace window the first time, escalates to a hard suspension once the
    /// previously opened window has expired. Pure mapping; the caller is
    /// responsible for persisting <paramref name="s"/>.
    /// </summary>
    public void MarkPaymentFailed(TenantSettings s, DateTimeOffset now)
    {
        s.StripeSubscriptionStatus = "past_due";
        if (s.GracePeriodUntil is null)
        {
            s.GracePeriodUntil = now + DunningWindow;
        }
        else if (s.GracePeriodUntil <= now && s.SuspendedAt is null)
        {
            s.SuspendedAt = now;
        }
    }
}
