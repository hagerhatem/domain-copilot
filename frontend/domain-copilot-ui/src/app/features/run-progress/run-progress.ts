import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, input, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RunStreamService } from '../../core/services/run-stream.service';
import {
  AgentProgressEvent,
  AgentProgressEventType,
  AgentRole,
} from '../../core/models/agent-progress-event.model';
import { RouterLink } from '@angular/router';

type AgentChipStatus = 'pending' | 'running' | 'succeeded' | 'refused' | 'failed';

const PIPELINE_AGENTS: AgentRole[] = [
  AgentRole.GuidelineResearcher,
  AgentRole.SafetyChecker,
  AgentRole.DocumentationDrafter,
];

const AGENT_LABELS: Record<AgentRole, string> = {
  [AgentRole.Orchestrator]: 'Orchestrator',
  [AgentRole.GuidelineResearcher]: 'Guideline Researcher',
  [AgentRole.SafetyChecker]: 'Safety Checker',
  [AgentRole.DocumentationDrafter]: 'Documentation Drafter',
};

const EMPTY_GUID = '00000000-0000-0000-0000-000000000000';

@Component({
  selector: 'app-run-progress',
  imports: [CommonModule,RouterLink],
  templateUrl: './run-progress.html',
  styleUrl: './run-progress.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RunProgress {
  readonly clinicalCaseId = input.required<string>();

  private readonly runStream = inject(RunStreamService);
  private readonly destroyRef = inject(DestroyRef);

  readonly events = signal<AgentProgressEvent[]>([]);
  readonly runId = signal<string | null>(null);
  readonly isRunning = signal(false);
  readonly isCancelling = signal(false);
  readonly errorMessage = signal<string | null>(null);

  readonly agentChips = computed(() =>
    PIPELINE_AGENTS.map((role) => ({
      role,
      label: AGENT_LABELS[role],
      status: this.computeChipStatus(role, this.events()),
    })),
  );

  /**
   * "Streaming token text": no agent in this codebase calls
   * ILLMProvider.StreamAsync yet - there is no real token-level text to stream.
   * This renders each progress event's Message as it arrives, the closest honest
   * approximation available today.
   */
  readonly progressLog = computed(() => this.events().map((e) => e.Message));

    /**
   * There is no distinct "AwaitingApproval" AgentProgressEventType - the backend's
   * PipelineOrchestrator reports AgentProgressEventType.RunCompleted at the exact
   * moment a run succeeds and moves to AgentRunStatus.AwaitingApproval (there is no
   * "fully done, no approval needed" success state in this pipeline - every
   * successful run requires clinician approval). RunCompleted IS the
   * awaiting-approval signal here, not a separate "finished" state - documented
   * explicitly since the event type name alone doesn't convey that.
   */
  readonly isAwaitingApproval = computed(() =>
    this.events().some((e) => e.EventType === AgentProgressEventType.RunCompleted),
  );

  start(): void {
    this.events.set([]);
    this.runId.set(null);
    this.errorMessage.set(null);
    this.isRunning.set(true);

    const subscription = this.runStream.streamRunProgress(this.clinicalCaseId()).subscribe({
      next: (event) => {
        this.events.update((current) => [...current, event]);
        if (!this.runId() && event.RunId && event.RunId !== EMPTY_GUID) {
          this.runId.set(event.RunId);
        }
      },
      error: (err: Error) => {
        this.errorMessage.set(err.message ?? 'Stream failed.');
        this.isRunning.set(false);
      },
      complete: () => this.isRunning.set(false),
    });

    this.destroyRef.onDestroy(() => subscription.unsubscribe());
  }

  async cancel(): Promise<void> {
    const id = this.runId();
    if (!id) return;

    this.isCancelling.set(true);
    try {
      await this.runStream.cancelRun(id);
    } catch (err) {
      this.errorMessage.set((err as Error).message ?? 'Cancel failed.');
    } finally {
      this.isCancelling.set(false);
    }
  }

  private computeChipStatus(role: AgentRole, events: AgentProgressEvent[]): AgentChipStatus {
    const relevant = events.filter((e) => e.Role === role);
    if (relevant.length === 0) return 'pending';

    const last = relevant[relevant.length - 1];
    if (last.EventType === AgentProgressEventType.AgentFinished) {
      if (last.Message.includes('Failed')) return 'failed';
      if (last.Message.includes('Refused')) return 'refused';
      return 'succeeded';
    }
    if (last.EventType === AgentProgressEventType.AgentStarted) return 'running';
    return 'pending';
  }
}