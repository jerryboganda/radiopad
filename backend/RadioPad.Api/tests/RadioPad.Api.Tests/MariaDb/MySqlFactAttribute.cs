using Xunit;

namespace RadioPad.Api.Tests.MariaDb;

/// <summary>
/// A fact that runs only when <c>RADIOPAD_TEST_MYSQL</c> holds an admin connection string to a MariaDB 10.11 server
/// (no <c>Database=</c> — each test creates and drops its own <c>radiopad_test_*</c> database). Without it the test is
/// skipped, so the normal CI job and local runs never need a database server; the 'ci-mysql' and 'mysql-migrations'
/// workflows provide one as a service container. Tests carry <c>[Trait("Category", "MySql")]</c> for filtering.
/// </summary>
public sealed class MySqlFactAttribute : FactAttribute
{
    public const string EnvVar = "RADIOPAD_TEST_MYSQL";

    public static bool Enabled => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvVar));

    public MySqlFactAttribute()
    {
        if (!Enabled)
            Skip = $"Set {EnvVar} to an admin MariaDB 10.11 connection string (without Database=) to run the MySQL tests.";
    }
}
