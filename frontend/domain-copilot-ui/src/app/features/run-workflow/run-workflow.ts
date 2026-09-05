import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatSelectModule } from '@angular/material/select';
import { MatTabsModule } from '@angular/material/tabs';
import { ClinicalCaseService } from '../../core/services/clinical-case.service';
import { ClinicalCaseSummary } from '../../core/models/clinical-case.model';
import { RunProgress } from '../run-progress/run-progress';

@Component({
  selector: 'app-run-workflow',
  imports: [
    CommonModule,
    FormsModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatSelectModule,
    MatTabsModule,
    RunProgress,
  ],
  templateUrl: './run-workflow.html',
  styleUrl: './run-workflow.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RunWorkflow {
  private readonly clinicalCaseService = inject(ClinicalCaseService);

  readonly existingCases = signal<ClinicalCaseSummary[]>([]);
  readonly selectedCaseId = signal<string | null>(null);
  readonly activeCaseId = signal<string | null>(null);
  readonly isLoadingCases = signal(false);

  readonly caseReference = signal('');
  readonly presentingComplaint = signal('');
  readonly proposedMedication = signal('');
  readonly patientContext = signal('');
  readonly currentMedicationsInput = signal('');
  readonly isCreating = signal(false);
  readonly errorMessage = signal<string | null>(null);

  constructor() {
    this.refreshCases();
  }

  async refreshCases(): Promise<void> {
    this.isLoadingCases.set(true);
    try {
      this.existingCases.set(await this.clinicalCaseService.list());
    } catch (err) {
      this.errorMessage.set((err as Error).message ?? 'Failed to load cases.');
    } finally {
      this.isLoadingCases.set(false);
    }
  }

  selectExisting(): void {
    if (this.selectedCaseId()) {
      this.activeCaseId.set(this.selectedCaseId());
    }
  }

  async createCase(): Promise<void> {
    if (!this.caseReference().trim() || !this.presentingComplaint().trim() || !this.proposedMedication().trim()) {
      this.errorMessage.set('Case reference, presenting complaint, and proposed medication are required.');
      return;
    }

    this.isCreating.set(true);
    this.errorMessage.set(null);

    try {
      const created = await this.clinicalCaseService.create({
        caseReference: this.caseReference().trim(),
        presentingComplaint: this.presentingComplaint().trim(),
        proposedMedication: this.proposedMedication().trim(),
        patientContext: this.patientContext().trim() || null,
        currentMedications: this.currentMedicationsInput()
          .split(',')
          .map((m) => m.trim())
          .filter((m) => m.length > 0),
      });

      this.activeCaseId.set(created.id);
      await this.refreshCases();
    } catch (err) {
      this.errorMessage.set((err as Error).message ?? 'Failed to create case.');
    } finally {
      this.isCreating.set(false);
    }
  }
}