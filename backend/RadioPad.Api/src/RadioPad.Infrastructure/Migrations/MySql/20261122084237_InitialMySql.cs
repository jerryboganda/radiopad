using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RadioPad.Infrastructure.Migrations.MySql
{
    /// <inheritdoc />
    public partial class InitialMySql : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ReportId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Kind = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    Mode = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    ProviderId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    Status = table.Column<string>(type: "varchar(190)", maxLength: 190, nullable: false, collation: "utf8mb4_unicode_ci"),
                    ResultJson = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_unicode_ci"),
                    InputJson = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_unicode_ci"),
                    Error = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_unicode_ci"),
                    ErrorKind = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_unicode_ci"),
                    ProviderJobId = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_unicode_ci"),
                    Attempt = table.Column<int>(type: "int", nullable: false),
                    RetryOfJobId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    CancelRequested = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiJobs", x => x.Id);
                })
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "AiRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ReportId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    PromptVersion = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    RulebookId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    RulebookVersion = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    Provider = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    Model = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    Mode = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    ContainsPhi = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    InputHash = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    OutputHash = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    LatencyMs = table.Column<int>(type: "int", nullable: false),
                    InputTokens = table.Column<int>(type: "int", nullable: false),
                    OutputTokens = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiRequests", x => x.Id);
                })
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "AiUsageRollups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Provider = table.Column<string>(type: "varchar(190)", maxLength: 190, nullable: false, collation: "utf8mb4_unicode_ci"),
                    Model = table.Column<string>(type: "varchar(190)", maxLength: 190, nullable: false, collation: "utf8mb4_unicode_ci"),
                    RequestCount = table.Column<int>(type: "int", nullable: false),
                    InputTokens = table.Column<long>(type: "bigint", nullable: false),
                    OutputTokens = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiUsageRollups", x => x.Id);
                })
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "AuditEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    ReportId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    Action = table.Column<int>(type: "int", nullable: false),
                    DetailsJson = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    IntegrityChain = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvents", x => x.Id);
                })
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "AuditExportBundles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    ContentJsonl = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    Signature = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_unicode_ci"),
                    EventCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditExportBundles", x => x.Id);
                })
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "AuthSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    GlobalUserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantMembershipId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    TokenHash = table.Column<string>(type: "varchar(190)", maxLength: 190, nullable: false, collation: "utf8mb4_unicode_ci"),
                    Method = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    IssuedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    RevocationReason = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    DeviceFingerprintHash = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    IpHash = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    UserAgentHash = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    SessionEpochAtIssue = table.Column<int>(type: "int", nullable: false),
                    DeviceCategory = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_unicode_ci"),
                    DeviceDetail = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_unicode_ci"),
                    IpAddress = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_unicode_ci"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthSessions", x => x.Id);
                })
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "BodyParts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Code = table.Column<string>(type: "varchar(190)", maxLength: 190, nullable: false, collation: "utf8mb4_unicode_ci"),
                    Name = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    Active = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BodyParts", x => x.Id);
                })
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "CompanionSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    PairingCode = table.Column<string>(type: "varchar(190)", maxLength: 190, nullable: false, collation: "utf8mb4_unicode_ci"),
                    HostDeviceName = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    CompanionDeviceName = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_unicode_ci"),
                    CompanionTokenHash = table.Column<string>(type: "varchar(190)", maxLength: 190, nullable: true, collation: "utf8mb4_unicode_ci"),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    PairedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanionSessions", x => x.Id);
                })
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "CriticalResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ReportId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Criticality = table.Column<int>(type: "int", nullable: false),
                    FindingSummary = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CommunicatedTo = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_unicode_ci"),
                    CommunicationMethod = table.Column<int>(type: "int", nullable: true),
                    CommunicatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    AcknowledgedBy = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_unicode_ci"),
                    AcknowledgedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    DueAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    EscalatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    Notes = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CriticalResults", x => x.Id);
                })
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "DeviceAuth",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    DeviceCodeHash = table.Column<string>(type: "varchar(190)", maxLength: 190, nullable: false, collation: "utf8mb4_unicode_ci"),
                    UserCode = table.Column<string>(type: "varchar(190)", maxLength: 190, nullable: false, collation: "utf8mb4_unicode_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    Status = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    IntervalSeconds = table.Column<int>(type: "int", nullable: false),
                    LastPolledAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    DeviceFingerprint = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_unicode_ci"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceAuth", x => x.Id);
                })
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "DriftBaselines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ProviderId = table.Column<string>(type: "varchar(190)", maxLength: 190, nullable: false, collation: "utf8mb4_unicode_ci"),
                    RulebookId = table.Column<string>(type: "varchar(190)", maxLength: 190, nullable: false, collation: "utf8mb4_unicode_ci"),
                    QualityScore = table.Column<int>(type: "int", nullable: false),
                    FindingRuleIdsJson = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    CheckedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DriftBaselines", x => x.Id);
                })
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "ExternalIdentities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    GlobalUserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ProviderKey = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_bin"),
                    Issuer = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false, collation: "utf8mb4_bin"),
                    Subject = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false, collation: "utf8mb4_bin"),
                    Email = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    NormalizedEmail = table.Column<string>(type: "varchar(190)", maxLength: 190, nullable: false, collation: "utf8mb4_unicode_ci"),
                    DisplayName = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    ClaimsJson = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalIdentities", x => x.Id);
                })
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "GlobalUsers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    PrimaryEmail = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    NormalizedEmail = table.Column<string>(type: "varchar(190)", maxLength: 190, nullable: false, collation: "utf8mb4_unicode_ci"),
                    DisplayName = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    LastLoginAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GlobalUsers", x => x.Id);
                })
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "Lexicons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Term = table.Column<string>(type: "varchar(190)", maxLength: 190, nullable: false, collation: "utf8mb4_bin"),
                    Forbidden = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Replacement = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    Note = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Lexicons", x => x.Id);
                })
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "LibraryFavorites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    EntityType = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    EntityKey = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LibraryFavorites", x => x.Id);
                })
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "LibraryRecentUses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    EntityType = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    EntityKey = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LibraryRecentUses", x => x.Id);
                })
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "MagicLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TokenHash = table.Column<string>(type: "varchar(190)", maxLength: 190, nullable: false, collation: "utf8mb4_unicode_ci"),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MagicLinks", x => x.Id);
                })
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "MarketplaceListings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    PublisherTenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    PublisherUserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Name = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    Description = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    Kind = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    ArtifactBody = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    PriceCents = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    ReviewerUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    RejectionReason = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_unicode_ci"),
                    StripePriceId = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_unicode_ci"),
                    RevenueShareBps = table.Column<int>(type: "int", nullable: false),
                    SourceRulebookId = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_unicode_ci"),
                    SourceTemplateId = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_unicode_ci"),
                    Version = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    InstallCount = table.Column<int>(type: "int", nullable: false),
                    ReviewNotes = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_unicode_ci"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketplaceListings", x => x.Id);
                })
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "MarketplacePurchases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ListingId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    BuyerTenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    BuyerUserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    AmountCents = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    StripeSessionId = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_unicode_ci"),
                    StripePaymentIntentId = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_unicode_ci"),
                    PaidAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketplacePurchases", x => x.Id);
                })
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "McpToolCalls",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ToolId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ReportId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    InputHash = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    OutputHash = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    Status = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    ToolName = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    ScopeString = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    LatencyMs = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_McpToolCalls", x => x.Id);
                })
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "McpTools",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Name = table.Column<string>(type: "varchar(190)", maxLength: 190, nullable: false, collation: "utf8mb4_unicode_ci"),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Scope = table.Column<int>(type: "int", nullable: false),
                    Approved = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ApprovedBy = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    AllowedConnectorPaths = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    Version = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    ScopeString = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    ManifestJson = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    ManifestSha256 = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    ManifestSig = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    Status = table.Column<int>(type: "int", nullable: false),
                    IsBuiltIn = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_McpTools", x => x.Id);
                })
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "Modalities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Code = table.Column<string>(type: "varchar(190)", maxLength: 190, nullable: false, collation: "utf8mb4_unicode_ci"),
                    Name = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    Active = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Modalities", x => x.Id);
                })
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "NotificationPreferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    MutedCategoriesCsv = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    DndStartMinutes = table.Column<int>(type: "int", nullable: true),
                    DndEndMinutes = table.Column<int>(type: "int", nullable: true),
                    DndTimeZone = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    PushEnabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    EmailEnabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationPreferences", x => x.Id);
                })
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Category = table.Column<int>(type: "int", nullable: false),
                    Urgency = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    Body = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    LinkHref = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_unicode_ci"),
                    SourceKind = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_unicode_ci"),
                    SourceId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    RequiresAck = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ReadAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    AcknowledgedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    AcknowledgedByUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    DedupeKey = table.Column<string>(type: "varchar(190)", maxLength: 190, nullable: true, collation: "utf8mb4_unicode_ci"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                })
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "PeerReviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ReportId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ReviewerUserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    OriginalAuthorUserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    AssignedByUserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ReviewType = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Score = table.Column<int>(type: "int", nullable: false),
                    Complexity = table.Column<int>(type: "int", nullable: false),
                    DiscrepancyCategory = table.Column<int>(type: "int", nullable: false),
                    Comments = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    Blinded = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    DisputeReason = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_unicode_ci"),
                    DisputedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PeerReviews", x => x.Id);
                })
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "PromptOverrides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    RulebookId = table.Column<string>(type: "varchar(190)", maxLength: 190, nullable: false, collation: "utf8mb4_unicode_ci"),
                    BlockKey = table.Column<string>(type: "varchar(190)", maxLength: 190, nullable: false, collation: "utf8mb4_unicode_ci"),
                    Body = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ApprovedByUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromptOverrides", x => x.Id);
                })
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "PushDevices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Platform = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    Token = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false, collation: "utf8mb4_bin"),
                    RegisteredAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PushDevices", x => x.Id);
                })
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "ReportLayouts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedByUserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Name = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    Description = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_unicode_ci"),
                    LayoutJson = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    SchemaVersion = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportLayouts", x => x.Id);
                })
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "ReportLayoutUserDefaults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ReportLayoutId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportLayoutUserDefaults", x => x.Id);
                })
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "Reports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedByUserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    RulebookId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    TemplateId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    TemplatePinned = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    RulebookPinned = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    Study_AccessionNumber = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    Study_Modality = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    Study_BodyPart = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    Study_Contrast = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    Study_Age = table.Column<int>(type: "int", nullable: true),
                    Study_AgeUnit = table.Column<int>(type: "int", nullable: false),
                    Study_Gender = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    Study_Comparison = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    Study_PriorReportSummary = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    Study_PatientReference = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    Indication = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    Technique = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    Comparison = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    Findings = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    Impression = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    Recommendations = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    AiHighlightsJson = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    StructuredFieldsJson = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    ServiceRequestRef = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_unicode_ci"),
                    DepartmentTag = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_unicode_ci"),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    RadiologyId = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    PatientName = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    PatientAge = table.Column<int>(type: "int", nullable: false),
                    PatientGender = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reports", x => x.Id);
                })
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "Rulebooks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    RulebookId = table.Column<string>(type: "varchar(190)", maxLength: 190, nullable: false, collation: "utf8mb4_unicode_ci"),
                    Name = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    Version = table.Column<string>(type: "varchar(190)", maxLength: 190, nullable: false, collation: "utf8mb4_unicode_ci"),
                    Owner = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    Status = table.Column<int>(type: "int", nullable: false),
                    SourceYaml = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    CompiledJson = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    AppliesToModalities = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_unicode_ci"),
                    AppliesToBodyParts = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_unicode_ci"),
                    DepartmentTag = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_unicode_ci"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rulebooks", x => x.Id);
                })
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "ScimGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    DisplayName = table.Column<string>(type: "varchar(190)", maxLength: 190, nullable: false, collation: "utf8mb4_unicode_ci"),
                    ExternalId = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_unicode_ci"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScimGroups", x => x.Id);
                })
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "SharedMacros",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedByUserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Scope = table.Column<int>(type: "int", nullable: false),
                    Subspecialty = table.Column<string>(type: "varchar(190)", maxLength: 190, nullable: false, collation: "utf8mb4_unicode_ci"),
                    Trigger = table.Column<string>(type: "varchar(190)", maxLength: 190, nullable: false, collation: "utf8mb4_unicode_ci"),
                    Body = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    Description = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SharedMacros", x => x.Id);
                })
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "Snippets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedByUserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Name = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    Modality = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    BodyPart = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    Category = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_unicode_ci"),
                    SectionsJson = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Snippets", x => x.Id);
                })
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "StripeWebhookEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    EventId = table.Column<string>(type: "varchar(190)", maxLength: 190, nullable: false, collation: "utf8mb4_unicode_ci"),
                    EventType = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    Source = table.Column<string>(type: "varchar(190)", maxLength: 190, nullable: false, collation: "utf8mb4_unicode_ci"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StripeWebhookEvents", x => x.Id);
                })
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "TeachingCases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedByUserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Title = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    Modality = table.Column<string>(type: "varchar(190)", maxLength: 190, nullable: false, collation: "utf8mb4_unicode_ci"),
                    BodyPart = table.Column<string>(type: "varchar(190)", maxLength: 190, nullable: false, collation: "utf8mb4_unicode_ci"),
                    Diagnosis = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    TeachingPoints = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    ClinicalHistory = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    FindingsText = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    ImpressionText = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    Tags = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    Difficulty = table.Column<int>(type: "int", nullable: false),
                    SourceReportId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    Visibility = table.Column<int>(type: "int", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    ViewCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeachingCases", x => x.Id);
                })
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "Templates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TemplateId = table.Column<string>(type: "varchar(190)", maxLength: 190, nullable: false, collation: "utf8mb4_unicode_ci"),
                    Name = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    Modality = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    BodyPart = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    Contrast = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    Subspecialty = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    SectionsJson = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    Variant = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ApprovedBy = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Templates", x => x.Id);
                })
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "TenantMemberships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    GlobalUserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Status = table.Column<string>(type: "varchar(190)", maxLength: 190, nullable: false, collation: "utf8mb4_unicode_ci"),
                    IsDefault = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    JoinedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    RemovedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    SessionEpoch = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantMemberships", x => x.Id);
                })
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "Tenants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Slug = table.Column<string>(type: "varchar(190)", maxLength: 190, nullable: false, collation: "utf8mb4_unicode_ci"),
                    DisplayName = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    RequirePhiApprovedProvider = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    AllowSandboxRulebooks = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    StripeConnectAccountId = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_unicode_ci"),
                    AllowExternalMcp = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    RequireMfa = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    DraftAutoArchiveDays = table.Column<int>(type: "int", nullable: false),
                    CriticalNotificationCategoriesCsv = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.Id);
                })
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "TenantSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    HallucinationDetectionEnabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    HallucinationSeverity = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    HallucinationAllowList = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    HallucinationMinSupport = table.Column<double>(type: "double", nullable: false),
                    Plan = table.Column<int>(type: "int", nullable: false),
                    FeatureFlagsJson = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    StripeCustomerId = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    StripeSubscriptionId = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    StripeSubscriptionStatus = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    StripeCurrentPeriodEnd = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    TrialEndsAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    GracePeriodUntil = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    SuspendedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    ChargesEnabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    PayoutsEnabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IngestBearerSecret = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    FhirWebhookSecret = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    Hl7SendingFacility = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    DicomWebBaseUrl = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    DicomWebBearerSecret = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    PacsVendor = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_unicode_ci"),
                    RetentionDays = table.Column<int>(type: "int", nullable: false),
                    HashOnlyAuditMode = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    LegalHold = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    RequireMfa = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IdleTimeoutMinutes = table.Column<int>(type: "int", nullable: false),
                    MaxConcurrentSessions = table.Column<int>(type: "int", nullable: false),
                    RetentionByClassJson = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    CriticalityClassesJson = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    ScimBearerSecret = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    ScimGroupRoleMapJson = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    CmkKeyRef = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    CmkLastVerifiedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    IpAllowlistCidr = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    IpAllowlistJson = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    RequireZeroBlockers = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    WarnAsBlocker = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    AllowDangerousMcp = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    RoutingWeightsJson = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    Locale = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    RecommendedReportLayoutId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantSettings", x => x.Id);
                })
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "TenantWebhookEndpoints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Url = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    Secret = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    EventsCsv = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    Active = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    FailureCount = table.Column<int>(type: "int", nullable: false),
                    DisabledAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantWebhookEndpoints", x => x.Id);
                })
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "TrustedPluginPublishers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    PublisherName = table.Column<string>(type: "varchar(190)", maxLength: 190, nullable: false, collation: "utf8mb4_unicode_ci"),
                    Ed25519PublicKeyBase64 = table.Column<string>(type: "varchar(190)", maxLength: 190, nullable: false, collation: "utf8mb4_unicode_ci"),
                    RevokedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrustedPluginPublishers", x => x.Id);
                })
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "UserCorrections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    From = table.Column<string>(type: "varchar(190)", maxLength: 190, nullable: false, collation: "utf8mb4_bin"),
                    To = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserCorrections", x => x.Id);
                })
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "ValidationPacks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    RulebookId = table.Column<string>(type: "varchar(190)", maxLength: 190, nullable: false, collation: "utf8mb4_unicode_ci"),
                    Version = table.Column<string>(type: "varchar(190)", maxLength: 190, nullable: false, collation: "utf8mb4_unicode_ci"),
                    Name = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    ApprovedBy = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    CreatedBy = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    GoldenCasesJson = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ValidationPacks", x => x.Id);
                })
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "WebAuthnCredentials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CredentialId = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    CredentialIdHash = table.Column<string>(type: "varchar(190)", maxLength: 190, nullable: false, collation: "utf8mb4_unicode_ci"),
                    PublicKey = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    SignCount = table.Column<uint>(type: "int unsigned", nullable: false),
                    Label = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    AttestationFormat = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    LastUsedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebAuthnCredentials", x => x.Id);
                })
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "DictationAudios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ReportId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    StoragePath = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    DurationSeconds = table.Column<double>(type: "double", nullable: false),
                    TranscriptionEngine = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    Status = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    TranscribedText = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_unicode_ci"),
                    UploadedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    TranscribedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    ErrorMessage = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_unicode_ci"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DictationAudios", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DictationAudios_Reports_ReportId",
                        column: x => x.ReportId,
                        principalTable: "Reports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "RadsAssessments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ReportId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Family = table.Column<string>(type: "varchar(190)", maxLength: 190, nullable: false, collation: "utf8mb4_unicode_ci"),
                    Category = table.Column<string>(type: "varchar(190)", maxLength: 190, nullable: false, collation: "utf8mb4_unicode_ci"),
                    Score = table.Column<int>(type: "int", nullable: true),
                    IsDerived = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    LesionKey = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_unicode_ci"),
                    Rationale = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RadsAssessments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RadsAssessments_Reports_ReportId",
                        column: x => x.ReportId,
                        principalTable: "Reports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "ReportMeasurements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ReportId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Label = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    Value = table.Column<double>(type: "double", nullable: false),
                    Unit = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    SecondValue = table.Column<double>(type: "double", nullable: true),
                    ThirdValue = table.Column<double>(type: "double", nullable: true),
                    AnatomicalLocation = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    Laterality = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    Section = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    LesionKey = table.Column<string>(type: "varchar(190)", maxLength: 190, nullable: true, collation: "utf8mb4_unicode_ci"),
                    StudyReference = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_unicode_ci"),
                    Source = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportMeasurements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReportMeasurements_Reports_ReportId",
                        column: x => x.ReportId,
                        principalTable: "Reports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "ReportSignatures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ReportId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    SignedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false),
                    Note = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_unicode_ci"),
                    Hash = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportSignatures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReportSignatures_Reports_ReportId",
                        column: x => x.ReportId,
                        principalTable: "Reports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "ReportVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ReportId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    SnapshotJson = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    RulebookId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    RulebookVersion = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    AuthorUserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Action = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    IsAddendum = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReportVersions_Reports_ReportId",
                        column: x => x.ReportId,
                        principalTable: "Reports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "ScimGroupMemberships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    GroupId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScimGroupMemberships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScimGroupMemberships_ScimGroups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "ScimGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "Providers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Name = table.Column<string>(type: "varchar(190)", maxLength: 190, nullable: false, collation: "utf8mb4_unicode_ci"),
                    Adapter = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    Model = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    EndpointUrl = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    ApiKeySecretRef = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    Compliance = table.Column<int>(type: "int", nullable: false),
                    Enabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    CostPerInputKToken = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    CostPerOutputKToken = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    MaxCostPerCallUsd = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Quality = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    RetentionLabel = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    OAuthRefreshTokenEnc = table.Column<byte[]>(type: "longblob", nullable: true),
                    OAuthRefreshTokenIv = table.Column<byte[]>(type: "longblob", nullable: true),
                    OAuthRefreshTokenTag = table.Column<byte[]>(type: "longblob", nullable: true),
                    OAuthRefreshTokenWrappedDek = table.Column<byte[]>(type: "longblob", nullable: true),
                    OAuthRefreshTokenUpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    OAuthRefreshTokenExpiresAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    OAuthRefreshTokenRotationPolicy = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_unicode_ci"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Providers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Providers_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Email = table.Column<string>(type: "varchar(190)", maxLength: 190, nullable: false, collation: "utf8mb4_unicode_ci"),
                    DisplayName = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    Role = table.Column<int>(type: "int", nullable: false),
                    PasswordHash = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    MfaSecret = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci"),
                    MfaEnabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    FailedLoginCount = table.Column<int>(type: "int", nullable: false),
                    FailedLoginWindowStart = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    LockedUntil = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    SessionEpoch = table.Column<int>(type: "int", nullable: false),
                    PreferredLocale = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_unicode_ci"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Users_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateIndex(
                name: "IX_AiJobs_Status_CompletedAt",
                table: "AiJobs",
                columns: new[] { "Status", "CompletedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AiJobs_TenantId_ReportId_Status",
                table: "AiJobs",
                columns: new[] { "TenantId", "ReportId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AiJobs_TenantId_UserId_CreatedAt",
                table: "AiJobs",
                columns: new[] { "TenantId", "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AiUsageRollups_TenantId_Date_Provider_Model",
                table: "AiUsageRollups",
                columns: new[] { "TenantId", "Date", "Provider", "Model" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_Action_CreatedAt",
                table: "AuditEvents",
                columns: new[] { "Action", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_TenantId_CreatedAt",
                table: "AuditEvents",
                columns: new[] { "TenantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditExportBundles_TenantId_Date",
                table: "AuditExportBundles",
                columns: new[] { "TenantId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuthSessions_GlobalUserId_ExpiresAt",
                table: "AuthSessions",
                columns: new[] { "GlobalUserId", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AuthSessions_RevokedAt_ExpiresAt",
                table: "AuthSessions",
                columns: new[] { "RevokedAt", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AuthSessions_TenantId_UserId_ExpiresAt",
                table: "AuthSessions",
                columns: new[] { "TenantId", "UserId", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AuthSessions_TokenHash",
                table: "AuthSessions",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BodyParts_TenantId_Code",
                table: "BodyParts",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompanionSessions_CompanionTokenHash",
                table: "CompanionSessions",
                column: "CompanionTokenHash");

            migrationBuilder.CreateIndex(
                name: "IX_CompanionSessions_PairingCode",
                table: "CompanionSessions",
                column: "PairingCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompanionSessions_TenantId_UserId_Status",
                table: "CompanionSessions",
                columns: new[] { "TenantId", "UserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_CriticalResults_TenantId_ReportId",
                table: "CriticalResults",
                columns: new[] { "TenantId", "ReportId" });

            migrationBuilder.CreateIndex(
                name: "IX_CriticalResults_TenantId_Status_DueAt",
                table: "CriticalResults",
                columns: new[] { "TenantId", "Status", "DueAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DeviceAuth_DeviceCodeHash",
                table: "DeviceAuth",
                column: "DeviceCodeHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeviceAuth_UserCode",
                table: "DeviceAuth",
                column: "UserCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DictationAudios_ReportId",
                table: "DictationAudios",
                column: "ReportId");

            migrationBuilder.CreateIndex(
                name: "IX_DriftBaselines_TenantId_ProviderId_RulebookId",
                table: "DriftBaselines",
                columns: new[] { "TenantId", "ProviderId", "RulebookId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExternalIdentities_GlobalUserId_ProviderKey",
                table: "ExternalIdentities",
                columns: new[] { "GlobalUserId", "ProviderKey" });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalIdentities_NormalizedEmail",
                table: "ExternalIdentities",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalIdentities_ProviderKey_Issuer_Subject",
                table: "ExternalIdentities",
                columns: new[] { "ProviderKey", "Issuer", "Subject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GlobalUsers_NormalizedEmail",
                table: "GlobalUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "IX_Lexicons_TenantId_Term",
                table: "Lexicons",
                columns: new[] { "TenantId", "Term" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MagicLinks_TokenHash",
                table: "MagicLinks",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_McpToolCalls_TenantId_CreatedAt",
                table: "McpToolCalls",
                columns: new[] { "TenantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_McpTools_TenantId_Name",
                table: "McpTools",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Modalities_TenantId_Code",
                table: "Modalities",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationPreferences_TenantId_UserId",
                table: "NotificationPreferences",
                columns: new[] { "TenantId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_TenantId_UserId_CreatedAt",
                table: "Notifications",
                columns: new[] { "TenantId", "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_TenantId_UserId_DedupeKey",
                table: "Notifications",
                columns: new[] { "TenantId", "UserId", "DedupeKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_TenantId_UserId_ReadAt",
                table: "Notifications",
                columns: new[] { "TenantId", "UserId", "ReadAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PeerReviews_TenantId_ReportId",
                table: "PeerReviews",
                columns: new[] { "TenantId", "ReportId" });

            migrationBuilder.CreateIndex(
                name: "IX_PeerReviews_TenantId_ReviewerUserId_Status",
                table: "PeerReviews",
                columns: new[] { "TenantId", "ReviewerUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PromptOverrides_TenantId_RulebookId_BlockKey",
                table: "PromptOverrides",
                columns: new[] { "TenantId", "RulebookId", "BlockKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Providers_TenantId_Name",
                table: "Providers",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PushDevices_TenantId_UserId_LastSeenAt",
                table: "PushDevices",
                columns: new[] { "TenantId", "UserId", "LastSeenAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PushDevices_TenantId_UserId_Token",
                table: "PushDevices",
                columns: new[] { "TenantId", "UserId", "Token" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RadsAssessments_ReportId",
                table: "RadsAssessments",
                column: "ReportId");

            migrationBuilder.CreateIndex(
                name: "IX_RadsAssessments_TenantId_Family_Category",
                table: "RadsAssessments",
                columns: new[] { "TenantId", "Family", "Category" });

            migrationBuilder.CreateIndex(
                name: "IX_RadsAssessments_TenantId_ReportId",
                table: "RadsAssessments",
                columns: new[] { "TenantId", "ReportId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReportLayouts_TenantId",
                table: "ReportLayouts",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ReportLayoutUserDefaults_TenantId_UserId",
                table: "ReportLayoutUserDefaults",
                columns: new[] { "TenantId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReportMeasurements_ReportId",
                table: "ReportMeasurements",
                column: "ReportId");

            migrationBuilder.CreateIndex(
                name: "IX_ReportMeasurements_TenantId_LesionKey",
                table: "ReportMeasurements",
                columns: new[] { "TenantId", "LesionKey" });

            migrationBuilder.CreateIndex(
                name: "IX_ReportMeasurements_TenantId_ReportId",
                table: "ReportMeasurements",
                columns: new[] { "TenantId", "ReportId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReportSignatures_ReportId",
                table: "ReportSignatures",
                column: "ReportId");

            migrationBuilder.CreateIndex(
                name: "IX_ReportSignatures_TenantId_ReportId_Role",
                table: "ReportSignatures",
                columns: new[] { "TenantId", "ReportId", "Role" });

            migrationBuilder.CreateIndex(
                name: "IX_ReportVersions_ReportId",
                table: "ReportVersions",
                column: "ReportId");

            migrationBuilder.CreateIndex(
                name: "IX_Rulebooks_TenantId_RulebookId_Version",
                table: "Rulebooks",
                columns: new[] { "TenantId", "RulebookId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScimGroupMemberships_GroupId",
                table: "ScimGroupMemberships",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_ScimGroupMemberships_TenantId_GroupId_UserId",
                table: "ScimGroupMemberships",
                columns: new[] { "TenantId", "GroupId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScimGroups_TenantId_DisplayName",
                table: "ScimGroups",
                columns: new[] { "TenantId", "DisplayName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SharedMacros_TenantId_Scope_Subspecialty_Trigger",
                table: "SharedMacros",
                columns: new[] { "TenantId", "Scope", "Subspecialty", "Trigger" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StripeWebhookEvents_Source_EventId",
                table: "StripeWebhookEvents",
                columns: new[] { "Source", "EventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeachingCases_TenantId_CreatedByUserId",
                table: "TeachingCases",
                columns: new[] { "TenantId", "CreatedByUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_TeachingCases_TenantId_Modality_BodyPart",
                table: "TeachingCases",
                columns: new[] { "TenantId", "Modality", "BodyPart" });

            migrationBuilder.CreateIndex(
                name: "IX_Templates_TenantId_TemplateId",
                table: "Templates",
                columns: new[] { "TenantId", "TemplateId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantMemberships_GlobalUserId_TenantId",
                table: "TenantMemberships",
                columns: new[] { "GlobalUserId", "TenantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantMemberships_TenantId_Status",
                table: "TenantMemberships",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TenantMemberships_TenantId_UserId",
                table: "TenantMemberships",
                columns: new[] { "TenantId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_Slug",
                table: "Tenants",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantSettings_TenantId",
                table: "TenantSettings",
                column: "TenantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantWebhookEndpoints_TenantId",
                table: "TenantWebhookEndpoints",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TrustedPluginPublishers_TenantId_Ed25519PublicKeyBase64",
                table: "TrustedPluginPublishers",
                columns: new[] { "TenantId", "Ed25519PublicKeyBase64" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrustedPluginPublishers_TenantId_PublisherName",
                table: "TrustedPluginPublishers",
                columns: new[] { "TenantId", "PublisherName" });

            migrationBuilder.CreateIndex(
                name: "IX_UserCorrections_TenantId_UserId_From",
                table: "UserCorrections",
                columns: new[] { "TenantId", "UserId", "From" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_TenantId_Email",
                table: "Users",
                columns: new[] { "TenantId", "Email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ValidationPacks_TenantId_RulebookId",
                table: "ValidationPacks",
                columns: new[] { "TenantId", "RulebookId" });

            migrationBuilder.CreateIndex(
                name: "IX_ValidationPacks_TenantId_RulebookId_Version",
                table: "ValidationPacks",
                columns: new[] { "TenantId", "RulebookId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WebAuthnCredentials_TenantId_CredentialIdHash",
                table: "WebAuthnCredentials",
                columns: new[] { "TenantId", "CredentialIdHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WebAuthnCredentials_TenantId_UserId",
                table: "WebAuthnCredentials",
                columns: new[] { "TenantId", "UserId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiJobs");

            migrationBuilder.DropTable(
                name: "AiRequests");

            migrationBuilder.DropTable(
                name: "AiUsageRollups");

            migrationBuilder.DropTable(
                name: "AuditEvents");

            migrationBuilder.DropTable(
                name: "AuditExportBundles");

            migrationBuilder.DropTable(
                name: "AuthSessions");

            migrationBuilder.DropTable(
                name: "BodyParts");

            migrationBuilder.DropTable(
                name: "CompanionSessions");

            migrationBuilder.DropTable(
                name: "CriticalResults");

            migrationBuilder.DropTable(
                name: "DeviceAuth");

            migrationBuilder.DropTable(
                name: "DictationAudios");

            migrationBuilder.DropTable(
                name: "DriftBaselines");

            migrationBuilder.DropTable(
                name: "ExternalIdentities");

            migrationBuilder.DropTable(
                name: "GlobalUsers");

            migrationBuilder.DropTable(
                name: "Lexicons");

            migrationBuilder.DropTable(
                name: "LibraryFavorites");

            migrationBuilder.DropTable(
                name: "LibraryRecentUses");

            migrationBuilder.DropTable(
                name: "MagicLinks");

            migrationBuilder.DropTable(
                name: "MarketplaceListings");

            migrationBuilder.DropTable(
                name: "MarketplacePurchases");

            migrationBuilder.DropTable(
                name: "McpToolCalls");

            migrationBuilder.DropTable(
                name: "McpTools");

            migrationBuilder.DropTable(
                name: "Modalities");

            migrationBuilder.DropTable(
                name: "NotificationPreferences");

            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropTable(
                name: "PeerReviews");

            migrationBuilder.DropTable(
                name: "PromptOverrides");

            migrationBuilder.DropTable(
                name: "Providers");

            migrationBuilder.DropTable(
                name: "PushDevices");

            migrationBuilder.DropTable(
                name: "RadsAssessments");

            migrationBuilder.DropTable(
                name: "ReportLayouts");

            migrationBuilder.DropTable(
                name: "ReportLayoutUserDefaults");

            migrationBuilder.DropTable(
                name: "ReportMeasurements");

            migrationBuilder.DropTable(
                name: "ReportSignatures");

            migrationBuilder.DropTable(
                name: "ReportVersions");

            migrationBuilder.DropTable(
                name: "Rulebooks");

            migrationBuilder.DropTable(
                name: "ScimGroupMemberships");

            migrationBuilder.DropTable(
                name: "SharedMacros");

            migrationBuilder.DropTable(
                name: "Snippets");

            migrationBuilder.DropTable(
                name: "StripeWebhookEvents");

            migrationBuilder.DropTable(
                name: "TeachingCases");

            migrationBuilder.DropTable(
                name: "Templates");

            migrationBuilder.DropTable(
                name: "TenantMemberships");

            migrationBuilder.DropTable(
                name: "TenantSettings");

            migrationBuilder.DropTable(
                name: "TenantWebhookEndpoints");

            migrationBuilder.DropTable(
                name: "TrustedPluginPublishers");

            migrationBuilder.DropTable(
                name: "UserCorrections");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "ValidationPacks");

            migrationBuilder.DropTable(
                name: "WebAuthnCredentials");

            migrationBuilder.DropTable(
                name: "Reports");

            migrationBuilder.DropTable(
                name: "ScimGroups");

            migrationBuilder.DropTable(
                name: "Tenants");
        }
    }
}
