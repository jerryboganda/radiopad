using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;

namespace RadioPad.Api.Services;

/// <summary>
/// NOTIF-004 — the per-channel PHI tiers for a notification, in one place so no
/// dispatch path can leak more than its tier allows. A <see cref="Notification"/>
/// row's <c>Title</c>/<c>Body</c> are the <b>in-app tier</b> (an authenticated,
/// audited surface): they may carry a modality/body-part/FindingSummary-class
/// descriptor (this is exactly what <c>NotificationProducer.InsertAsync</c>
/// persists as the row Body). Every OTHER channel is stricter:
///
/// <list type="bullet">
///   <item><b>OS toast / mobile push / email</b> — Title is a generic category
///   phrase; Body is AT MOST modality+body-part, and NEVER the FindingSummary,
///   accession, patient name, or MRN. Because a <see cref="Notification"/> row
///   carries no <i>structured</i> modality/body-part field (only the free-text
///   in-app Body, which may itself be a FindingSummary), the safe body cannot
///   echo the row Body — it collapses to a generic, category-appropriate phrase.
///   That is the conservative floor of "at most modality+body-part".</item>
///   <item><b>Webhook</b> — ids + category only, no Title/Body (owned by
///   <c>WebhookDispatchJob</c>, not this helper).</item>
/// </list>
///
/// <see cref="SafeTitle"/> / <see cref="SafeBody"/> derive the push/email-safe
/// strings from a row and are reused by both <c>DeliverPushAsync</c> and
/// <c>DeliverEmailAsync</c> so the two channels can never drift.
/// </summary>
public static class NotificationPhiTier
{
    /// <summary>Generic, PHI-free headline for the OS toast / push / email tier.</summary>
    public static string SafeTitle(Notification n) => n.Category switch
    {
        NotificationCategory.CriticalResult => "Critical result",
        NotificationCategory.PeerReview => "Peer review",
        NotificationCategory.RulebookApproval => "Rulebook update",
        NotificationCategory.TemplateApproval => "Template review",
        NotificationCategory.AiJob => "AI report",
        NotificationCategory.System => "RadioPad alert",
        NotificationCategory.Mention => "You were mentioned",
        _ => "RadioPad",
    };

    /// <summary>
    /// Generic, PHI-free body for the OS toast / push / email tier. Deliberately
    /// derived from the category alone — it NEVER echoes the row's in-app Body,
    /// which may carry a FindingSummary/accession beyond this tier's ceiling.
    /// </summary>
    public static string SafeBody(Notification n) => n.Category switch
    {
        NotificationCategory.CriticalResult => "A critical result needs your attention.",
        NotificationCategory.PeerReview => "A peer review needs your attention.",
        NotificationCategory.RulebookApproval => "A rulebook was updated.",
        NotificationCategory.TemplateApproval => "A template needs your attention.",
        NotificationCategory.AiJob => "An AI task finished.",
        NotificationCategory.System => "Open RadioPad to review.",
        NotificationCategory.Mention => "Open RadioPad to review.",
        _ => "Open RadioPad to review.",
    };
}
