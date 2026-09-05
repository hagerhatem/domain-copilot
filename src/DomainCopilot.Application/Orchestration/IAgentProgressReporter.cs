using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DomainCopilot.Application.Orchestration;

/// <summary>
/// Sink for AgentProgressEvent, injected into PipelineOrchestrator. Kept as a plain
/// synchronous Report call (not async, not IAsyncEnumerable) so PipelineOrchestrator
/// itself stays a normal request/response method - the actual SSE/IAsyncEnumerable
/// mechanics live entirely in the Api layer's ChannelAgentProgressReporter, which is
/// the only implementation that does anything with these events today.
/// </summary>
public interface IAgentProgressReporter
{
    void Report(AgentProgressEvent progressEvent);
}

/// <summary>Default no-op sink, so PipelineOrchestrator's new constructor parameter is non-breaking for every existing caller/test.</summary>
public sealed class NullAgentProgressReporter : IAgentProgressReporter
{
    public static readonly NullAgentProgressReporter Instance = new();
    private NullAgentProgressReporter() { }
    public void Report(AgentProgressEvent progressEvent) { }
}