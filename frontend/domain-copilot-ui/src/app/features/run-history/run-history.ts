import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { RouterLink } from '@angular/router';
import { RunHistoryService } from '../../core/services/run-history.service';
import { RunHistoryItem } from '../../core/models/run-history.model';

@Component({
  selector: 'app-run-history',
  imports: [CommonModule, MatCardModule, MatChipsModule, RouterLink],
  templateUrl: './run-history.html',
  styleUrl: './run-history.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RunHistory {
  private readonly runHistoryService = inject(RunHistoryService);

  readonly runs = signal<RunHistoryItem[]>([]);
  readonly isLoading = signal(false);
  readonly loadError = signal<string | null>(null);

  constructor() {
    this.refresh();
  }

  async refresh(): Promise<void> {
    this.isLoading.set(true);
    this.loadError.set(null);
    try {
      const runs = await this.runHistoryService.listHistory();
      this.runs.set(runs);
    } catch (err) {
      this.loadError.set((err as Error).message ?? 'Failed to load run history.');
    } finally {
      this.isLoading.set(false);
    }
  }
}