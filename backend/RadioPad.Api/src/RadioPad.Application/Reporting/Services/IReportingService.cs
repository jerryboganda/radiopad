using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RadioPad.Application.Reporting.Dtos;

namespace RadioPad.Application.Reporting.Services;

public interface IReportingService
{
    Task<ReportDto> CreateReportAsync(CreateReportRequestDto dto, CancellationToken ct = default);
    Task<List<ReportDto>> GetReportsAsync(string? search = null, string? status = null, CancellationToken ct = default);
    Task<ReportDto?> GetReportByIdAsync(Guid id, CancellationToken ct = default);
    Task<DictationAudioDto> AddDictationAudioAsync(Guid reportId, Stream audioStream, string fileName, double durationSeconds, CancellationToken ct = default);
}
