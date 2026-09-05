import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { PendingApprovalRun } from '../models/approval-queue.model';

@Injectable({ providedIn: 'root' })
export class ApprovalService {
  private readonly http = inject(HttpClient);
  private readonly apiBaseUrl = environment.apiBaseUrl;

  listPendingApproval(): Promise<PendingApprovalRun[]> {
    return firstValueFrom(this.http.get<PendingApprovalRun[]>(`${this.apiBaseUrl}/runs/pending-approval`));
  }

  approve(runId: string): Promise<unknown> {
    return firstValueFrom(this.http.post(`${this.apiBaseUrl}/runs/${runId}/approve`, {}));
  }

  reject(runId: string, comment: string): Promise<unknown> {
    return firstValueFrom(this.http.post(`${this.apiBaseUrl}/runs/${runId}/reject`, { comment }));
  }

  editAndApprove(runId: string, editedNoteText: string): Promise<unknown> {
    return firstValueFrom(
      this.http.post(`${this.apiBaseUrl}/runs/${runId}/edit-approve`, { editedNoteText }),
    );
  }
}