/**
 * Mirrors DomainCopilot.Application.Orchestration.AgentProgressEvent /
 * AgentProgressEventType and DomainCopilot.Domain.Entities.AgentRole exactly as
 * serialized by RunsController.Stream's `JsonSerializer.Serialize(progressEvent)`
 * call - that call passes NO JsonSerializerOptions, so the wire format is .NET's
 * plain default: PascalCase property names, and enums as raw INTEGERS (not
 * strings). If the backend is ever changed to use JsonStringEnumConverter or a
 * camelCase naming policy, this file and RunStreamService's parsing must change
 * together.
 */
export enum AgentRole {
  Orchestrator = 0,
  GuidelineResearcher = 1,
  SafetyChecker = 2,
  DocumentationDrafter = 3,
}

export enum AgentProgressEventType {
  RunStarted = 0,
  AgentStarted = 1,
  AgentFinished = 2,
  RunRefused = 3,
  RunDegraded = 4,
  RunFailed = 5,
  RunCompleted = 6,
}

export interface AgentProgressEvent {
  RunId: string;
  EventType: AgentProgressEventType;
  Role: AgentRole | null;
  Message: string;
  Timestamp: string;
}

/** Event types after which the backend's SSE channel closes on its own. */
export const TERMINAL_EVENT_TYPES: ReadonlySet<AgentProgressEventType> = new Set([
  AgentProgressEventType.RunRefused,
  AgentProgressEventType.RunDegraded,
  AgentProgressEventType.RunFailed,
  AgentProgressEventType.RunCompleted,
]);