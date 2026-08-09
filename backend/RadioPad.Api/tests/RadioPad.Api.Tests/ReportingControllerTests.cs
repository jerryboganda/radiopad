using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RadioPad.Api.Controllers;
using RadioPad.Api.Hubs;
using RadioPad.Application.Reporting.Dtos;
using RadioPad.Application.Reporting.Services;
using RadioPad.Api.Services;
using RadioPad.Infrastructure.Persistence;
using Xunit;

namespace RadioPad.Api.Tests;

public class ReportingControllerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly RadioPadDbContext _db;
    private readonly MobileReportingService _service;
    private readonly ReportingController _controller;

    public ReportingControllerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<RadioPadDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new RadioPadDbContext(options);
        _db.Database.EnsureCreated();

        var testHubContext = new TestHubContext();
        var logger = NullLogger<MobileReportingService>.Instance;

        _service = new MobileReportingService(_db, testHubContext, logger);
        _controller = new ReportingController(_service);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Close();
        _connection.Dispose();
    }

    [Fact]
    public async Task CreateReport_ReturnsCreatedAtAction_WithReportDto()
    {
        var request = new CreateReportRequestDto(
            RadiologyId: "RAD-2026-001",
            PatientName: "John Doe",
            PatientAge: 45,
            PatientGender: "Male"
        );

        var result = await _controller.CreateReport(request, CancellationToken.None);

        var createdAt = Assert.IsType<CreatedAtActionResult>(result.Result);
        var report = Assert.IsType<ReportDto>(createdAt.Value);

        Assert.NotEqual(Guid.Empty, report.Id);
        Assert.Equal("RAD-2026-001", report.RadiologyId);
        Assert.Equal("John Doe", report.PatientName);
        Assert.Equal(45, report.PatientAge);
        Assert.Equal("Male", report.PatientGender);
        Assert.Equal("Draft", report.Status);
        Assert.Empty(report.Dictations);
    }

    [Fact]
    public async Task GetReports_ReturnsAllReports_AndFiltersBySearchAndStatus()
    {
        await _controller.CreateReport(new CreateReportRequestDto("RAD-101", "Alice Smith", 30, "Female"), CancellationToken.None);
        await _controller.CreateReport(new CreateReportRequestDto("RAD-102", "Bob Johnson", 60, "Male"), CancellationToken.None);

        // Get all
        var allResult = await _controller.GetReports(search: null, status: null, CancellationToken.None);
        var okAll = Assert.IsType<OkObjectResult>(allResult.Result);
        var reportsAll = Assert.IsType<List<ReportDto>>(okAll.Value);
        Assert.Equal(2, reportsAll.Count);

        // Search filter
        var searchResult = await _controller.GetReports(search: "Alice", status: null, CancellationToken.None);
        var okSearch = Assert.IsType<OkObjectResult>(searchResult.Result);
        var reportsSearch = Assert.IsType<List<ReportDto>>(okSearch.Value);
        Assert.Single(reportsSearch);
        Assert.Equal("RAD-101", reportsSearch[0].RadiologyId);

        // Status filter
        var statusResult = await _controller.GetReports(search: null, status: "Draft", CancellationToken.None);
        var okStatus = Assert.IsType<OkObjectResult>(statusResult.Result);
        var reportsStatus = Assert.IsType<List<ReportDto>>(okStatus.Value);
        Assert.Equal(2, reportsStatus.Count);
    }

    [Fact]
    public async Task GetReportById_ReturnsOk_WhenReportExists()
    {
        var created = await _service.CreateReportAsync(new CreateReportRequestDto("RAD-200", "Charlie Brown", 50, "Male"));

        var result = await _controller.GetReportById(created.Id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var report = Assert.IsType<ReportDto>(ok.Value);
        Assert.Equal(created.Id, report.Id);
        Assert.Equal("RAD-200", report.RadiologyId);
    }

    [Fact]
    public async Task GetReportById_ReturnsNotFound_WhenReportDoesNotExist()
    {
        var result = await _controller.GetReportById(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task UploadDictation_ReturnsOkWithDictationAudioDto_WhenValid()
    {
        var created = await _service.CreateReportAsync(new CreateReportRequestDto("RAD-300", "David Miller", 40, "Male"));

        var content = Encoding.UTF8.GetBytes("fake audio content");
        using var stream = new MemoryStream(content);
        var formFile = new FormFile(stream, 0, content.Length, "file", "test_dictation.wav");

        var result = await _controller.UploadDictation(created.Id, formFile, durationSeconds: 15.5, ct: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dictation = Assert.IsType<DictationAudioDto>(ok.Value);

        Assert.Equal(created.Id, dictation.ReportId);
        Assert.Equal(15.5, dictation.DurationSeconds);
        Assert.Equal("medASR-6gram", dictation.TranscriptionEngine);
        Assert.Equal("Pending", dictation.Status);
        Assert.True(File.Exists(dictation.StoragePath));

        // Cleanup created audio file
        if (File.Exists(dictation.StoragePath))
        {
            File.Delete(dictation.StoragePath);
        }
    }

    [Fact]
    public async Task UploadDictation_ReturnsBadRequest_WhenFileIsNull()
    {
        var created = await _service.CreateReportAsync(new CreateReportRequestDto("RAD-400", "Eva Green", 35, "Female"));

        var result = await _controller.UploadDictation(created.Id, null!, durationSeconds: 5.0, ct: CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("No audio file provided.", badRequest.Value);
    }

    [Fact]
    public async Task UploadDictation_ReturnsNotFound_WhenReportDoesNotExist()
    {
        var content = Encoding.UTF8.GetBytes("fake audio content");
        using var stream = new MemoryStream(content);
        var formFile = new FormFile(stream, 0, content.Length, "file", "test_dictation.wav");

        var result = await _controller.UploadDictation(Guid.NewGuid(), formFile, durationSeconds: 5.0, ct: CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task ReportingHub_JoinAndLeaveGroup_Succeeds()
    {
        var hub = new ReportingHub();
        var mockContext = new TestHubCallerContext();
        hub.Context = mockContext;
        var mockGroups = new TestGroupManager();
        hub.Groups = mockGroups;

        await hub.JoinReportGroup("123");
        await hub.LeaveReportGroup("123");
    }

    #region Test Double Classes for SignalR

    private class TestHubContext : IHubContext<ReportingHub>
    {
        public IHubClients Clients { get; } = new TestHubClients();
        public IGroupManager Groups { get; } = new TestGroupManager();
    }

    private class TestHubClients : IHubClients
    {
        public IClientProxy All { get; } = new TestClientProxy();
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => new TestClientProxy();
        public IClientProxy Client(string connectionId) => new TestClientProxy();
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => new TestClientProxy();
        public IClientProxy Group(string groupName) => new TestClientProxy();
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => new TestClientProxy();
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => new TestClientProxy();
        public IClientProxy User(string userId) => new TestClientProxy();
        public IClientProxy Users(IReadOnlyList<string> userIds) => new TestClientProxy();
    }

    private class TestClientProxy : IClientProxy
    {
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private class TestGroupManager : IGroupManager
    {
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private class TestHubCallerContext : HubCallerContext
    {
        public override string ConnectionId => "test-conn-id";
        public override string? UserIdentifier => "test-user-id";
        public override System.Security.Claims.ClaimsPrincipal User => new System.Security.Claims.ClaimsPrincipal();
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features => new FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() { }
    }

    private class FeatureCollection : Microsoft.AspNetCore.Http.Features.IFeatureCollection
    {
        public bool IsReadOnly => false;
        public int Revision => 0;
        public object? this[Type key] { get => null; set { } }
        public TFeature? Get<TFeature>() => default;
        public void Set<TFeature>(TFeature? instance) { }
        public IEnumerator<KeyValuePair<Type, object>> GetEnumerator() => new List<KeyValuePair<Type, object>>().GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    #endregion
}
