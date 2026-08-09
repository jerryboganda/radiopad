using RadioPad.Infrastructure.Providers.Local;
using Xunit;

namespace RadioPad.Api.Tests.Stt;

public class SherpaMedAsrSttClientTests
{
    [Fact]
    public void ResolveMaxActivePaths_Defaults_To_8()
    {
        var maxActivePaths = LocalSttModels.ResolveMaxActivePaths();
        Assert.Equal(8, maxActivePaths);
    }

    [Fact]
    public void ResolveDecodingMethod_Defaults_To_ModifiedBeamSearch()
    {
        var method = LocalSttModels.ResolveDecodingMethod();
        Assert.Equal("modified_beam_search", method);
    }
}
