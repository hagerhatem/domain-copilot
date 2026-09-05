import { ChangeDetectionStrategy, Component, effect, inject, input, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { TraceService } from '../../core/services/trace.service';
import { RunTrace } from '../../core/models/trace.model';

@Component({
  selector: 'app-trace-viewer',
  imports: [CommonModule, FormsModule, MatCardModule, MatFormFieldModule, MatInputModule, MatButtonModule],
  templateUrl: './trace-viewer.html',
  styleUrl: './trace-viewer.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TraceViewer {
  // Optional now, not required: this screen can be reached either via a route
  // param (linked from Run Workflow/Approval Queue) or by manually typing a run id
  // in the input box below.
  readonly runId = input<string>();

  private readonly traceService = inject(TraceService);

  readonly inputRunId = signal('');
  readonly trace = signal<RunTrace | null>(null);
  readonly isLoading = signal(false);
  readonly errorMessage = signal<string | null>(null);

  constructor() {
    effect(() => {
      const id = this.runId();
      if (id) {
        this.inputRunId.set(id);
        this.load(id);
      }
    });
  }

  async loadFromInput(): Promise<void> {
    const id = this.inputRunId().trim();
    if (id) await this.load(id);
  }

  private async load(id: string): Promise<void> {
    this.isLoading.set(true);
    this.errorMessage.set(null);
    this.trace.set(null);
    try {
      this.trace.set(await this.traceService.getTrace(id));
    } catch (err) {
      this.errorMessage.set((err as Error).message ?? 'Failed to load trace.');
    } finally {
      this.isLoading.set(false);
    }
  }
}