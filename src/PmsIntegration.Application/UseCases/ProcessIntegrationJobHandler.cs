using Microsoft.Extensions.Logging;
using PmsIntegration.Application.Services;
using PmsIntegration.Core.Abstractions;
using PmsIntegration.Core.Contracts;
using PmsIntegration.Core.Domain;

namespace PmsIntegration.Application.UseCases;

/// <summary>
/// Orchestrates the outbound flow:
///   idempotency check → build request → call provider → classify result.
/// </summary>
public sealed class ProcessIntegrationJobHandler
{
    private static readonly TimeSpan AcquireTtl = TimeSpan.FromHours(24);
    private static readonly TimeSpan SuccessTtl = TimeSpan.FromDays(7);

    private readonly IPmsProviderFactory _providerFactory;
    private readonly IIdempotencyStore _idempotency;
    private readonly RetryClassifier _classifier;
    private readonly IAuditLogger _audit;
    private readonly ILogger<ProcessIntegrationJobHandler> _logger;

    public ProcessIntegrationJobHandler(
        IPmsProviderFactory providerFactory,
        IIdempotencyStore idempotency,
        RetryClassifier classifier,
        IAuditLogger audit,
        ILogger<ProcessIntegrationJobHandler> logger)
    {
        _providerFactory = providerFactory;
        _idempotency = idempotency;
        _classifier = classifier;
        _audit = audit;
        _logger = logger;
    }

    public async Task<IntegrationResult> HandleAsync(IntegrationJob job, CancellationToken ct = default)
    {
        var idempotencyKey =
            $"{job.HotelId}:{job.EventId}:{job.EventType}:{job.ProviderKey}";

        _audit.Log("job.processing", new
        {
            job.CorrelationId,
            job.HotelId,
            job.EventId,
            job.EventType,
            job.ProviderKey,
            job.JobId
        });

        var acquired = await _idempotency.TryAcquireAsync(idempotencyKey, AcquireTtl, ct);
        if (!acquired)
        {
            _audit.Log("job.duplicate_ignored", new { job.JobId, idempotencyKey });
            return IntegrationResult.Succeeded();
        }

        try
        {
            var provider = _providerFactory.Get(job.ProviderKey);

            var request = await provider.BuildRequestAsync(job, ct);
            var response = await provider.SendAsync(request, ct);

            if (response.IsSuccess)
            {
                await _idempotency.MarkSuccessAsync(idempotencyKey, SuccessTtl, ct);
                _audit.Log("job.success", new { job.JobId, job.ProviderKey, response.StatusCode });
                return IntegrationResult.Succeeded();
            }

            var result = _classifier.ClassifyHttpStatus(response.StatusCode);
            var action = result.Outcome == IntegrationOutcome.RetryableFailure ? "job.failed" : "job.failed";
            _audit.Log(action, new { job.JobId, response.StatusCode, result.Outcome });
            return result;
        }
        catch (Exception ex)
        {
            var result = _classifier.ClassifyException(ex);
            _audit.Log("job.failed", new { job.JobId, error = ex.Message, result.Outcome });
            return result;
        }
    }
}
