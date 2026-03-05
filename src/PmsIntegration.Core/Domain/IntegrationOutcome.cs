namespace PmsIntegration.Core.Domain;

public enum IntegrationOutcome
{
    Success,
    RetryableFailure,
    NonRetryableFailure
}
