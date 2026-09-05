import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { AskService } from '../../core/services/ask.service';
import { Citation, InsufficientEvidenceDto } from '../../core/models/ask.model';

@Component({
  selector: 'app-ask',
  imports: [CommonModule, FormsModule, MatCardModule, MatFormFieldModule, MatInputModule, MatButtonModule],
  templateUrl: './ask.html',
  styleUrl: './ask.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Ask {
  private readonly askService = inject(AskService);

  readonly question = signal('');
  readonly isSubmitting = signal(false);
  readonly citations = signal<Citation[] | null>(null);
  readonly refusal = signal<InsufficientEvidenceDto | null>(null);
  readonly errorMessage = signal<string | null>(null);

  async submit(): Promise<void> {
    if (!this.question().trim()) return;

    this.isSubmitting.set(true);
    this.citations.set(null);
    this.refusal.set(null);
    this.errorMessage.set(null);

    try {
      const result = await this.askService.ask(this.question().trim());
      if (result.kind === 'answer') {
        this.citations.set(result.response.citations);
      } else {
        this.refusal.set(result.refusal);
      }
    } catch (err) {
      this.errorMessage.set((err as Error).message ?? 'Request failed.');
    } finally {
      this.isSubmitting.set(false);
    }
  }
}