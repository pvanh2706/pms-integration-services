using PmsIntegration.Application.Services;
using PmsIntegration.Core.Abstractions;
using PmsIntegration.Core.Contracts;
using PmsIntegration.Core.Domain;

namespace PmsIntegration.Application.UseCases;

/// <summary>
/// Orchestrates the outbound flow:
///   idempotency check → build request → call provider → classify result.
///
/// The optional <see cref="IProviderFlowTracker"/> parameter on HandleAsync
/// allows the Host layer to attach a flow-logger without introducing an
/// infrastructure dependency here.
/// </summary>
public sealed class ProcessIntegrationJobHandler
{
    private static readonly TimeSpan AcquireTtl = TimeSpan.FromHours(24);
    private static readonly TimeSpan SuccessTtl = TimeSpan.FromDays(7);

    private readonly IPmsProviderFactory _providerFactory;
    private readonly IIdempotencyStore _idempotency;
    private readonly RetryClassifier _classifier;
    private readonly IAuditLogger _audit;

    public ProcessIntegrationJobHandler(
        IPmsProviderFactory providerFactory,
        IIdempotencyStore idempotency,
        RetryClassifier classifier,
        IAuditLogger audit)
    {
        _providerFactory = providerFactory;
        _idempotency = idempotency;
        _classifier = classifier;
        _audit = audit;
    }

    /// <summary>
    /// Processes an integration job.
    /// </summary>
    /// <param name="job">The job to process.</param>
    /// <param name="flowTracker">
    /// Optional callback object used by the Host (consumer) to record
    /// fine-grained provider steps into the PROVIDER_FLOW log document.
    /// Kept as a plain interface in Core.Abstractions to avoid a dependency
    /// on PmsIntegration.Infrastructure from the Application layer.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<IntegrationResult> HandleAsync(
        IntegrationJob job,
        IProviderFlowTracker? flowTracker = null,
        CancellationToken ct = default)
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

        // Resolve provider first — an unregistered provider key is a configuration error,
        // not a transient failure, so it deserves its own audit action.
        IPmsProvider provider;
        try
        {
            provider = _providerFactory.Get(job.ProviderKey);
        }
        catch (InvalidOperationException ex)
        {
            _audit.Log("job.provider_not_registered", new
            {
                job.JobId,
                job.ProviderKey,
                job.CorrelationId,
                error = ex.Message
            });
            return IntegrationResult.NonRetryableFailed("PROVIDER_NOT_REGISTERED", ex.Message);
        }

        try
        {
            // ── Build request ─────────────────────────────────────────────
            var request = await provider.BuildRequestAsync(job, ct);
            flowTracker?.OnRequestBuilt(request);

            // ── Send request ──────────────────────────────────────────────
            var response = await provider.SendAsync(request, ct);
            flowTracker?.OnResponseReceived(response);

            if (response.IsSuccess)
            {
                await _idempotency.MarkSuccessAsync(idempotencyKey, SuccessTtl, ct);
                _audit.Log("job.success", new { job.JobId, job.ProviderKey, response.StatusCode });
                return IntegrationResult.Succeeded();
            }

            var result = _classifier.ClassifyHttpStatus(response.StatusCode);
            // RetryableFailure and PermanentFailure get distinct audit actions.
            var action = result.Outcome == IntegrationOutcome.RetryableFailure
                ? "job.retryable_failed"
                : "job.failed";
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
