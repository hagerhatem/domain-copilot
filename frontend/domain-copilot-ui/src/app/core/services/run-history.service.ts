import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { RunHistoryItem } from '../models/run-history.model';

@Injectable({ providedIn: 'root' })
export class RunHistoryService {
  private readonly http = inject(HttpClient);
  private readonly apiBaseUrl = environment.apiBaseUrl;

  listHistory(): Promise<RunHistoryItem[]> {
    return firstValueFrom(this.http.get<RunHistoryItem[]>(`${this.apiBaseUrl}/runs/history`));
  }
}