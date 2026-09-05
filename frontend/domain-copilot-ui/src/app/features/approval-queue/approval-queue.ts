import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatChipsModule } from '@angular/material/chips';
import { ApprovalService } from '../../core/services/approval.service';
import { PendingApprovalRun } from '../../core/models/approval-queue.model';
import { RouterLink } from '@angular/router';

interface RunUiState {
  isEditing: boolean;
  editedText: string;
  rejectComment: string;
  actionError: string | null;
}

@Component({
  selector: 'app-approval-queue',
  imports: [
    CommonModule,
    FormsModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatExpansionModule,
    MatChipsModule,
    RouterLink
  ],
  templateUrl: './approval-queue.html',
  styleUrl: './approval-queue.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ApprovalQueue {
  private readonly approvalService = inject(ApprovalService);

  readonly runs = signal<PendingApprovalRun[]>([]);
  readonly isLoading = signal(false);
  readonly loadError = signal<string | null>(null);
  readonly uiState = signal<Record<string, RunUiState>>({});

  constructor() {
    this.refresh();
  }

  async refresh(): Promise<void> {
    this.isLoading.set(true);
    this.loadError.set(null);
    try {
      const runs = await this.approvalService.listPendingApproval();
      this.runs.set(runs);

      const state: Record<string, RunUiState> = {};
      for (const run of runs) {
        state[run.runId] = {
          isEditing: false,
          editedText: this.fullDraftText(run),
          rejectComment: '',
          actionError: null,
        };
      }
      this.uiState.set(state);
    } catch (err) {
      this.loadError.set((err as Error).message ?? 'Failed to load approval queue.');
    } finally {
      this.isLoading.set(false);
    }
  }

  fullDraftText(run: PendingApprovalRun): string {
    return [
      run.draftSubjective ? `SUBJECTIVE:\n${run.draftSubjective}` : '',
      run.draftAssessmentAndPlan ? `ASSESSMENT AND PLAN:\n${run.draftAssessmentAndPlan}` : '',
    ]
      .filter(Boolean)
      .join('\n\n');
  }

  state(runId: string): RunUiState {
    return this.uiState()[runId];
  }

  toggleEdit(runId: string): void {
    this.patchState(runId, { isEditing: !this.state(runId).isEditing });
  }

  updateEditedText(runId: string, value: string): void {
    this.patchState(runId, { editedText: value });
  }

  updateRejectComment(runId: string, value: string): void {
    this.patchState(runId, { rejectComment: value });
  }

  async approve(runId: string): Promise<void> {
    await this.runAction(runId, () => this.approvalService.approve(runId));
  }

  async reject(runId: string): Promise<void> {
    const comment = this.state(runId).rejectComment.trim();
    if (!comment) {
      this.patchState(runId, { actionError: 'A rejection comment is required.' });
      return;
    }
    await this.runAction(runId, () => this.approvalService.reject(runId, comment));
  }

  async editAndApprove(runId: string): Promise<void> {
    const editedText = this.state(runId).editedText.trim();
    if (!editedText) {
      this.patchState(runId, { actionError: 'Edited note text is required.' });
      return;
    }
    await this.runAction(runId, () => this.approvalService.editAndApprove(runId, editedText));
  }

  private async runAction(runId: string, action: () => Promise<unknown>): Promise<void> {
    this.patchState(runId, { actionError: null });
    try {
      await action();
      // On success, the run leaves AwaitingApproval - reload the queue so it disappears.
      await this.refresh();
    } catch (err) {
      this.patchState(runId, { actionError: (err as Error).message ?? 'Action failed.' });
    }
  }

  private patchState(runId: string, patch: Partial<RunUiState>): void {
    this.uiState.update((current) => ({
      ...current,
      [runId]: { ...current[runId], ...patch },
    }));
  }
}