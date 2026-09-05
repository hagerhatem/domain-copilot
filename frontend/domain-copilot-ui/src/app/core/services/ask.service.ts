import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AskRequest, AskResponse, InsufficientEvidenceDto } from '../models/ask.model';

export type AskResult =
  | { kind: 'answer'; response: AskResponse }
  | { kind: 'refused'; refusal: InsufficientEvidenceDto };

@Injectable({ providedIn: 'root' })
export class AskService {
  private readonly http = inject(HttpClient);
  private readonly apiBaseUrl = environment.apiBaseUrl;

  async ask(question: string): Promise<AskResult> {
    try {
      const response = await firstValueFrom(
        this.http.post<AskResponse>(`${this.apiBaseUrl}/ask`, { question } satisfies AskRequest),
      );
      return { kind: 'answer', response };
    } catch (err) {
      // 409 = InsufficientEvidenceError, per AskController's Conflict(...) response -
      // a distinct, expected outcome, not a request failure.
      if (err instanceof HttpErrorResponse && err.status === 409) {
        return { kind: 'refused', refusal: err.error as InsufficientEvidenceDto };
      }
      throw err;
    }
  }
}