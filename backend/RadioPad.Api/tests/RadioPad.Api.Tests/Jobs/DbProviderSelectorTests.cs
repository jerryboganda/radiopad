using MySqlConnector;
using RadioPad.Infrastructure.Persistence;
using Xunit;

namespace RadioPad.Api.Tests.Jobs;

/// <summary>
/// The explicit provider switch (RADIOPAD_DB_PROVIDER) and the MySQL connection-string guard rails. Pure unit
/// tests: the environment seams are passed as arguments, so nothing here touches the real environment (other
/// test classes boot whole hosts in parallel and read it).
/// </summary>
public class DbProviderSelectorTests
{
    private const string MySqlConn = "Server=mysql.example.net;Port=3306;Database=radiopad;User=u;Password=p";

    [Theory]
    [InlineData("mysql", DbProvider.MySql)]
    [InlineData("MariaDB", DbProvider.MySql)]
    [InlineData("  MYSQL ", DbProvider.MySql)]
    [InlineData("postgres", DbProvider.Postgres)]
    [InlineData("PostgreSQL", DbProvider.Postgres)]
    [InlineData("sqlite", DbProvider.Sqlite)]
    public void EnvValue_WinsOverTheConnectionString_CaseInsensitively(string env, DbProvider expected)
        // "Server=" would sniff as Postgres; the explicit value must win in every case.
        => Assert.Equal(expected, DbProviderSelector.Resolve(MySqlConn, env));

    [Theory]
    [InlineData("Host=localhost;Port=5432;Database=x", DbProvider.Postgres)]
    [InlineData("Server=db;Database=x", DbProvider.Postgres)]
    [InlineData("server=db;Database=x", DbProvider.Postgres)]
    [InlineData("Data Source=radiopad.dev.db", DbProvider.Sqlite)]
    [InlineData("Data Source=/data/radiopad.db", DbProvider.Sqlite)]
    [InlineData("", DbProvider.Sqlite)]
    public void WithoutEnv_TheLegacySniffAppliesUnchanged(string conn, DbProvider expected)
        // "" = RADIOPAD_DB_PROVIDER unset.
        => Assert.Equal(expected, DbProviderSelector.Resolve(conn, ""));

    [Fact]
    public void UnknownEnvValue_Throws_InsteadOfSilentlyUsingSqlite()
        => Assert.Throws<InvalidOperationException>(() => DbProviderSelector.Resolve("Data Source=x.db", "oracle"));

    [Theory]
    [InlineData("")]
    [InlineData(";SslMode=None")]
    [InlineData(";SslMode=Preferred")]
    public void Production_RejectsAnUnencryptedLink(string ssl)
        => Assert.Throws<InvalidOperationException>(
            () => DbProviderSelector.PrepareMySqlConnectionString(MySqlConn + ssl, isProduction: true, allowInsecure: false));

    [Theory]
    [InlineData(";SslMode=Required")]
    [InlineData(";SslMode=VerifyCA")]
    [InlineData(";SslMode=VerifyFull")]
    public void Production_AcceptsAnEncryptedLink(string ssl)
        => Assert.NotEmpty(DbProviderSelector.PrepareMySqlConnectionString(MySqlConn + ssl, isProduction: true, allowInsecure: false));

    [Fact]
    public void InsecureEscapeHatch_AllowsAPlainLinkInProduction()
        => Assert.NotEmpty(DbProviderSelector.PrepareMySqlConnectionString(MySqlConn, isProduction: true, allowInsecure: true));

    [Fact]
    public void OutsideProduction_APlainLinkIsFine()
        => Assert.NotEmpty(DbProviderSelector.PrepareMySqlConnectionString(MySqlConn, isProduction: false, allowInsecure: false));

    [Fact]
    public void PoolDefaultsTo20_ButAnExplicitSizeIsKept()
    {
        var defaulted = new MySqlConnectionStringBuilder(
            DbProviderSelector.PrepareMySqlConnectionString(MySqlConn, isProduction: false, allowInsecure: false));
        var explicitSize = new MySqlConnectionStringBuilder(
            DbProviderSelector.PrepareMySqlConnectionString(MySqlConn + ";Maximum Pool Size=7", isProduction: false, allowInsecure: false));

        Assert.Equal(20u, defaulted.MaximumPoolSize);
        Assert.Equal(7u, explicitSize.MaximumPoolSize);
    }

    [Fact]
    public void UseAffectedRows_IsForcedOff_BecauseTheAiJobConcurrencyTokenNeedsMatchedRowCounts()
    {
        var prepared = new MySqlConnectionStringBuilder(
            DbProviderSelector.PrepareMySqlConnectionString(MySqlConn + ";UseAffectedRows=true", isProduction: false, allowInsecure: false));

        Assert.False(prepared.UseAffectedRows);
    }
}
