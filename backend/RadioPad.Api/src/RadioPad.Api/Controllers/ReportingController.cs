using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RadioPad.Application.Reporting.Dtos;
using RadioPad.Application.Reporting.Services;

namespace RadioPad.Api.Controllers;

[ApiController]
[Route("api/v1/reporting/reports")]
public class ReportingController : ControllerBase
{
    private readonly IReportingService _reportingService;

    public ReportingController(IReportingService reportingService)
    {
        _reportingService = reportingService;
    }

    [HttpPost]
    public async Task<ActionResult<ReportDto>> CreateReport([FromBody] CreateReportRequestDto dto, CancellationToken ct)
    {
        var report = await _reportingService.CreateReportAsync(dto, ct);
        return CreatedAtAction(nameof(GetReportById), new { id = report.Id }, report);
    }

    [HttpGet]
    public async Task<ActionResult<List<ReportDto>>> GetReports([FromQuery] string? search, [FromQuery] string? status, CancellationToken ct)
    {
        var reports = await _reportingService.GetReportsAsync(search, status, ct);
        return Ok(reports);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ReportDto>> GetReportById(Guid id, CancellationToken ct)
    {
        var report = await _reportingService.GetReportByIdAsync(id, ct);
        if (report is null)
        {
            return NotFound();
        }
        return Ok(report);
    }

    [HttpPost("{id:guid}/dictations")]
    public async Task<ActionResult<DictationAudioDto>> UploadDictation(
        Guid id, 
        IFormFile file, 
        [FromForm] double durationSeconds = 0, 
        CancellationToken ct = default)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest("No audio file provided.");
        }

        try
        {
            await using var stream = file.OpenReadStream();
            var dictation = await _reportingService.AddDictationAudioAsync(id, stream, file.FileName, durationSeconds, ct);
            return Ok(dictation);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
    }
}
