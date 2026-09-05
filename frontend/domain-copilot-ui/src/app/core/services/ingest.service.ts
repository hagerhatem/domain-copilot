import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface IngestionResult {
  documentId: string;
  fileName: string;
  sourceKey: string;
  version: number;
  outcome: 'Created' | 'Updated' | 'Skipped' | 'Failed';
  chunkCount: number;
  contentHash: string;
  failureReason: string | null;
  failedAtStage: string | null;
  elapsed: string;
}

export interface DocumentSummary {
  documentId: string;
  fileName: string;
  version: number;
  status: string;
  failureReason: string | null;
  chunkCount: number;
  updatedAtUtc: string;
}

/**
 * Wraps IngestController (Prompt 12.5.2): POST /ingest (multipart file upload),
 * GET /ingest (list previously ingested documents).
 */
@Injectable({ providedIn: 'root' })
export class IngestService {
  private readonly http = inject(HttpClient);
  private readonly apiBaseUrl = environment.apiBaseUrl;

  uploadDocument(file: File): Promise<IngestionResult> {
    const formData = new FormData();
    formData.append('file', file);
    return firstValueFrom(this.http.post<IngestionResult>(`${this.apiBaseUrl}/ingest`, formData));
  }

  listDocuments(): Promise<DocumentSummary[]> {
    return firstValueFrom(this.http.get<DocumentSummary[]>(`${this.apiBaseUrl}/ingest`));
  }
}