using Xunit;

namespace RadioPad.Api.Tests.Integration;

/// <summary>
/// Definition for the pre-existing "OrgCreationSerial" collection, which until 2026-07-20 existed
/// only as a name on <c>[Collection]</c> attributes. An undefined collection serializes its members
/// against EACH OTHER but still runs in parallel with the rest of the suite — and RegistrationTests
/// mutates <c>RADIOPAD_ALLOW_SELF_SIGNUP</c>, an env var RegistrationController reads per-request,
/// so its tests could race any concurrently running integration test. DisableParallelization makes
/// membership mean what its name implies. EnvSerializationConventionTests enforces that every
/// env-mutating test class sits in a collection defined this way.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class OrgCreationSerialCollection
{
    public const string Name = "OrgCreationSerial";
}
