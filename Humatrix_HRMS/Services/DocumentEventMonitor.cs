// Services/Documents/DocumentEventMonitor.cs
//
// NOTE: This legacy monitor is superseded by AIEventMonitor (which is the production monitor).
// It is kept here only as a thin wrapper for any legacy code that still references it.
// Do NOT register both DocumentEventMonitor and AIEventMonitor as hosted services simultaneously.
//
// If you need periodic checks, use AIEventMonitor exclusively.

using Humatrix_HRMS.Data;
using Humatrix_HRMS.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Humatrix_HRMS.Services.Documents;

/// <summary>
/// Legacy background monitor — kept for backward compatibility only.
/// All real logic lives in <see cref="AIEventMonitor"/>.
/// Do not register this alongside AIEventMonitor.
/// </summary>
[Obsolete("Use AIEventMonitor instead. This class will be removed in a future release.")]
public class DocumentEventMonitor : BackgroundService
{
    private readonly ILogger<DocumentEventMonitor> _logger;

    public DocumentEventMonitor(ILogger<DocumentEventMonitor> logger)
    {
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogWarning(
            "DocumentEventMonitor is obsolete. Register AIEventMonitor instead. " +
            "This service will exit immediately.");

        return Task.CompletedTask;
    }
}