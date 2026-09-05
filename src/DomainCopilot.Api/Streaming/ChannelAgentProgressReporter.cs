using System.Threading.Channels;
using DomainCopilot.Application.Orchestration;

namespace DomainCopilot.Api.Streaming;

/// <summary>
/// Request-scoped IAgentProgressReporter backed by an unbounded Channel. Registered
/// as Scoped (see Program.cs), so within one HTTP request PipelineOrchestrator (via
/// RunClinicalWorkflowUseCase) and RunsController resolve the SAME instance - the
/// orchestrator writes events, the controller reads them and writes SSE frames.
///
/// If nothing ever reads this channel (e.g. a future non-streaming caller of
/// RunClinicalWorkflowUseCase that doesn't care about progress), TryWrite still
/// succeeds and events simply accumulate in memory until the request scope ends and
/// this instance is garbage collected - acceptable for a single run's worth of
/// events, not a long-lived leak.
/// </summary>
public sealed class ChannelAgentProgressReporter : IAgentProgressReporter
{
    private readonly Channel<AgentProgressEvent> _channel = Channel.CreateUnbounded<AgentProgressEvent>();

    public ChannelReader<AgentProgressEvent> Reader => _channel.Reader;

    public void Report(AgentProgressEvent progressEvent) => _channel.Writer.TryWrite(progressEvent);

    /// <summary>Signals no more events will be written - lets the controller's ReadAllAsync loop end naturally on success.</summary>
    public void Complete(Exception? error = null) => _channel.Writer.TryComplete(error);
}