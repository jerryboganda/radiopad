using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RadioPad.Api.Hubs;
using RadioPad.Application.Reporting.Dtos;
using RadioPad.Application.Reporting.Services;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;

using RadioPad.Infrastructure.Transcription;

namespace RadioPad.Api.Services;

public class MobileReportingService : IReportingService
{
    private readonly RadioPadDbContext _db;
    private readonly IHubContext<ReportingHub> _hubContext;
    private readonly ILogger<MobileReportingService> _logger;
    private readonly IServiceProvider? _serviceProvider;
    private readonly TranscriptionOrchestrator? _orchestrator;

    public MobileReportingService(
        RadioPadDbContext db,
        IHubContext<ReportingHub> hubContext,
        ILogger<MobileReportingService> logger,
        IServiceProvider? serviceProvider = null,
        TranscriptionOrchestrator? orchestrator = null)
    {
        _db = db;
        _hubContext = hubContext;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _orchestrator = orchestrator;
    }

    public async Task<ReportDto> CreateReportAsync(CreateReportRequestDto dto, Guid tenantId, Guid createdByUserId, CancellationToken ct = default)
    {
        var report = new Report
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CreatedByUserId = createdByUserId,
            RadiologyId = dto.RadiologyId,
            PatientName = dto.PatientName,
            PatientAge = dto.PatientAge,
            PatientGender = dto.PatientGender,
            CreatedAt = DateTimeOffset.UtcNow,
            Status = ReportStatus.Draft,
            Study = new StudyContext
            {
                Modality = dto.Modality ?? "",
                BodyPart = dto.BodyPart ?? "",
                Age = dto.PatientAge,
                Gender = dto.PatientGender,
            },
        };

        _db.Reports.Add(report);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Created report {ReportId} with RadiologyId {RadiologyId} for Tenant {TenantId}", report.Id, report.RadiologyId, tenantId);

        return MapToDto(report);
    }

    public async Task<List<ReportDto>> GetReportsAsync(Guid tenantId, string? search = null, string? status = null, CancellationToken ct = default)
    {
        IQueryable<Report> query = _db.Reports.Include(r => r.Dictations).Where(r => r.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchPattern = $"%{search.Trim()}%";
            query = query.Where(r => EF.Functions.Like(r.RadiologyId, searchPattern) 
                                  || EF.Functions.Like(r.PatientName, searchPattern));
        }

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<ReportStatus>(status, true, out var statusEnum))
        {
            query = query.Where(r => r.Status == statusEnum);
        }

        var reports = await query
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);

        return reports.Select(MapToDto).ToList();
    }

    public async Task<ReportDto?> GetReportByIdAsync(Guid id, Guid tenantId, CancellationToken ct = default)
    {
        // Tenant filter is part of the WHERE, not a post-hoc check: a cross-tenant id must look
        // exactly like "does not exist" (404), never leak that a report with that id exists
        // elsewhere.
        var report = await _db.Reports
            .Include(r => r.Dictations)
            .FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenantId, ct);

        return report is null ? null : MapToDto(report);
    }

    public async Task<DictationAudioDto> AddDictationAudioAsync(
        Guid reportId,
        Guid tenantId,
        Stream audioStream, 
        string fileName, 
        double durationSeconds, 
        CancellationToken ct = default)
    {
        var report = await _db.Reports.FirstOrDefaultAsync(r => r.Id == reportId && r.TenantId == tenantId, ct);
        if (report is null)
        {
            throw new KeyNotFoundException($"Report with Id '{reportId}' was not found.");
        }

        var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "uploads", "dictations", reportId.ToString());
        Directory.CreateDirectory(uploadsFolder);

        var safeFileName = $"{Guid.NewGuid()}_{Path.GetFileName(fileName)}";
        var filePath = Path.Combine(uploadsFolder, safeFileName);

        using (var destinationStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await audioStream.CopyToAsync(destinationStream, ct);
        }

        var dictation = new DictationAudio
        {
            Id = Guid.NewGuid(),
            ReportId = reportId,
            StoragePath = filePath,
            DurationSeconds = durationSeconds,
            TranscriptionEngine = "medASR-6gram",
            Status = "Pending",
            TranscribedText = null,
            UploadedAt = DateTimeOffset.UtcNow
        };

        _db.DictationAudios.Add(dictation);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Added dictation audio {DictationId} for Report {ReportId}", dictation.Id, reportId);

        var dto = MapToDictationDto(dictation);

        try
        {
            await _hubContext.Clients.Group($"report-{reportId}")
                .SendAsync("DictationUploaded", dto, cancellationToken: ct);
            await _hubContext.Clients.All
                .SendAsync("DictationUploaded", dto, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to broadcast DictationUploaded event via SignalR hub");
        }

        if (_serviceProvider != null)
        {
            // Prefer a FRESH DI scope (own DbContext) over reusing the request-scoped
            // _orchestrator/_db: this task is fire-and-forget and can still be running after the
            // HTTP request finishes and its scope (and _db) gets disposed, which would otherwise
            // risk an ObjectDisposedException mid-transcription.
            var sp = _serviceProvider;
            var dId = dictation.Id;
            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = sp.CreateScope();
                    var orch = scope.ServiceProvider.GetService<TranscriptionOrchestrator>();
                    if (orch != null)
                    {
                        await orch.ProcessDictationAsync(dId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed background transcription for dictation {DictationId}", dId);
                }
            });
        }
        else if (_orchestrator != null)
        {
            // No IServiceProvider was supplied (e.g. a unit test constructing the service
            // directly) — fall back to the already-injected orchestrator/DbContext.
            var orch = _orchestrator;
            var dId = dictation.Id;
            _ = Task.Run(async () =>
            {
                try
                {
                    await orch.ProcessDictationAsync(dId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed background transcription for dictation {DictationId}", dId);
                }
            });
        }

        return dto;
    }

    private static ReportDto MapToDto(Report report)
    {
        var dictations = report.Dictations ?? new List<DictationAudio>();
        return new ReportDto(
            report.Id,
            report.RadiologyId,
            report.PatientName,
            report.PatientAge,
            report.PatientGender,
            report.CreatedAt,
            report.Status.ToString(),
            dictations.OrderBy(d => d.UploadedAt).Select(MapToDictationDto).ToList(),
            report.Study?.Modality ?? "",
            report.Study?.BodyPart ?? ""
        );
    }

    private static DictationAudioDto MapToDictationDto(DictationAudio audio)
    {
        return new DictationAudioDto(
            audio.Id,
            audio.ReportId,
            audio.StoragePath,
            audio.DurationSeconds,
            audio.TranscriptionEngine,
            audio.Status,
            audio.TranscribedText,
            audio.UploadedAt,
            audio.TranscribedAt,
            audio.ErrorMessage
        );
    }
}
