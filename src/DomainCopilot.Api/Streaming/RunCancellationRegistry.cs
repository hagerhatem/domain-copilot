using System.Collections.Concurrent;

namespace DomainCopilot.Api.Streaming;

public enum RunCancellationResult { Cancelled, NotFound, Forbidden }

/// <summary>
/// Singleton registry mapping a live RunId to its owner and the CancellationTokenSource
/// driving its execution. SECURITY (Prompt 12.1 / OWASP A01 BOLA): TryCancel now
/// requires the caller's identity and enforces ownership - previously any
/// authenticated Clinician could cancel ANY other Clinician's run just by knowing
/// its RunId (RunIds are visible in each user's own stream, but nothing stopped a
/// caller from guessing/reusing one seen elsewhere, e.g. in logs).
/// </summary>
public interface IRunCancellationRegistry
{
    void Register(Guid runId, Guid ownerUserId, CancellationTokenSource cts);
    void Unregister(Guid runId);
    RunCancellationResult TryCancel(Guid runId, Guid callerUserId, bool callerIsAdmin);
}

public sealed class RunCancellationRegistry : IRunCancellationRegistry
{
    private readonly ConcurrentDictionary<Guid, (Guid OwnerUserId, CancellationTokenSource Cts)> _entries = new();

    public void Register(Guid runId, Guid ownerUserId, CancellationTokenSource cts) =>
        _entries[runId] = (ownerUserId, cts);

    public void Unregister(Guid runId) => _entries.TryRemove(runId, out _);

    public RunCancellationResult TryCancel(Guid runId, Guid callerUserId, bool callerIsAdmin)
    {
        if (!_entries.TryGetValue(runId, out var entry))
            return RunCancellationResult.NotFound;

        if (!callerIsAdmin && entry.OwnerUserId != callerUserId)
            return RunCancellationResult.Forbidden;

        try
        {
            entry.Cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            return RunCancellationResult.NotFound;
        }

        return RunCancellationResult.Cancelled;
    }
}