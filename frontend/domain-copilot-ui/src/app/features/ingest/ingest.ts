import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatTableModule } from '@angular/material/table';
import { DocumentSummary, IngestionResult, IngestService } from '../../core/services/ingest.service';

@Component({
  selector: 'app-ingest',
  imports: [CommonModule, MatCardModule, MatButtonModule, MatTableModule],
  templateUrl: './ingest.html',
  styleUrl: './ingest.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Ingest {
  private readonly ingestService = inject(IngestService);

  readonly isUploading = signal(false);
  readonly lastResult = signal<IngestionResult | null>(null);
  readonly errorMessage = signal<string | null>(null);
  readonly documents = signal<DocumentSummary[]>([]);

  readonly displayedColumns = ['fileName', 'version', 'status', 'chunkCount', 'updatedAtUtc'];

  constructor() {
    this.refreshList();
  }

  async onFileSelected(event: Event): Promise<void> {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (!file) return;

    this.isUploading.set(true);
    this.errorMessage.set(null);
    this.lastResult.set(null);

    try {
      const result = await this.ingestService.uploadDocument(file);
      this.lastResult.set(result);
      await this.refreshList();
    } catch (err) {
      this.errorMessage.set((err as Error).message ?? 'Upload failed.');
    } finally {
      this.isUploading.set(false);
      (event.target as HTMLInputElement).value = '';
    }
  }

  private async refreshList(): Promise<void> {
    try {
      this.documents.set(await this.ingestService.listDocuments());
    } catch {
      // Non-fatal: the upload area still works even if the list fails to load.
    }
  }
}