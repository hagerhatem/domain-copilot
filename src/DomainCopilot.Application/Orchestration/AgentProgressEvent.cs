using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DomainCopilot.Domain.Entities;

namespace DomainCopilot.Application.Orchestration;

/// <summary>
/// A single live-progress event emitted while PipelineOrchestrator executes a run
/// (Prompt 10.1, FR-6). KNOWN LIMITATION: there is no per-tool-call hook anywhere in
/// this codebase yet (e.g. SafetyCheckerAgent calls IDrugInteractionLookup and
/// IHybridRetrievalService directly, with no wrapper that reports a "tool called"
/// event). "Tool called" granularity is therefore approximated at the AGENT-STEP
/// level (AgentStarted/AgentFinished per role), not per individual tool invocation
/// inside a step. Likewise, no agent currently calls ILLMProvider.StreamAsync (all
/// use CompleteAsync), so there is no token-level TokenChunk event type here yet -
/// adding real token streaming requires changing GuidelineResearcherAgent and
/// DocumentationDrafterAgent to use StreamAsync, which is separate follow-up work.
/// </summary>
public enum AgentProgressEventType
{
    RunStarted,
    AgentStarted,
    AgentFinished,
    RunRefused,
    RunDegraded,
    RunFailed,
    RunCompleted
}

public sealed record AgentProgressEvent(
    Guid RunId,
    AgentProgressEventType EventType,
    AgentRole? Role,
    string Message,
    DateTimeOffset Timestamp);