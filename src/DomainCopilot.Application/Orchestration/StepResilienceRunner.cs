using DomainCopilot.Application.Agents;

namespace DomainCopilot.Application.Orchestration;

/// <summary>
/// Runs a single agent step with a per-attempt timeout and exponential-backoff retry
/// on transient failures (FR-5). Only AgentOutcome.Failed is treated as retryable -
/// Refused is a correct, non-transient outcome (retrying can't change what evidence
/// exists) and Success obviously needs no retry. Retrying a Refused outcome would
/// just burn tokens/time re-asking a question the corpus genuinely can't answer,
/// which is exactly the waste FR-5's controls are supposed to prevent, not cause.
/// </summary>
internal static class StepResilienceRunner
{
    public static async Task<AgentResult<TOutput>> RunAsync<TOutput>(
        Func<CancellationToken, Task<AgentResult<TOutput>>> stepAction,
        OrchestratorOptions options,
        CancellationToken ct)
    {
        string? lastTimeoutMessage = null;

        for (var attempt = 1; attempt <= options.MaxAttemptsPerAgent; attempt++)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(options.PerStepTimeout);

            try
            {
                var result = await stepAction(timeoutCts.Token);

                if (result.Outcome != AgentOutcome.Failed || attempt == options.MaxAttemptsPerAgent)
                {
                    // Success/Refused: done, no retry needed. Failed on the last
                    // attempt: exhausted retries, return the failure as-is.
                    return result;
                }
                // Failed with attempts remaining: fall through to backoff and retry.
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // The per-step timeout fired (not the caller's own cancellation) -
                // treated as a transient failure, eligible for retry.
                lastTimeoutMessage = $"Step timed out after {options.PerStepTimeout} on attempt {attempt}.";

                if (attempt == options.MaxAttemptsPerAgent)
                {
                    return AgentResult<TOutput>.Failed(lastTimeoutMessage);
                }
            }

            var backoff = TimeSpan.FromMilliseconds(
                options.InitialRetryBackoff.TotalMilliseconds * Math.Pow(2, attempt - 1));
            await Task.Delay(backoff, ct);
        }

        // Unreachable given the loop above always returns by its final iteration -
        // kept only so every code path has an explicit return value.
        return AgentResult<TOutput>.Failed(lastTimeoutMessage ?? "Exhausted retry attempts with no result.");
    }
}
