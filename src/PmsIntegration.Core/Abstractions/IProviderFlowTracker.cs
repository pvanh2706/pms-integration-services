using PmsIntegration.Core.Contracts;

namespace PmsIntegration.Core.Abstractions;

/// <summary>
/// Callback interface injected into <see cref="PmsIntegration.Application.UseCases.ProcessIntegrationJobHandler"/>
/// by the Host layer. Decouples the Application layer from Infrastructure.Logging.
///
/// The Host provides an adapter (<see cref="PmsIntegration.Infrastructure.Logging.Flow.ProviderFlowTrackerAdapter"/>)
/// that translates these callbacks into IProviderFlowLogger calls.
/// </summary>
public interface IProviderFlowTracker
{
    /// <summary>Records a named step that completed at the current wall-clock time.</summary>
    void OnStep(string stepName);

    /// <summary>Called after the provider request has been built.</summary>
    void OnRequestBuilt(ProviderRequest request);

    /// <summary>Called after the HTTP response has been received.</summary>
    void OnResponseReceived(ProviderResponse response);
}
