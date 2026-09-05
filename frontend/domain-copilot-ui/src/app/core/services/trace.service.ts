import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { RunTrace } from '../models/trace.model';

@Injectable({ providedIn: 'root' })
export class TraceService {
  private readonly http = inject(HttpClient);
  private readonly apiBaseUrl = environment.apiBaseUrl;

  getTrace(runId: string): Promise<RunTrace> {
    return firstValueFrom(this.http.get<RunTrace>(`${this.apiBaseUrl}/runs/${runId}/trace`));
  }
}