using Xunit;

namespace RadioPad.Api.Tests.Billing;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class StripeWebhookEnvCollection
{
    public const string Name = "Stripe webhook env";
}