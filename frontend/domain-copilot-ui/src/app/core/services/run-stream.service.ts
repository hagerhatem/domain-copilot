import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { AgentProgressEvent, TERMINAL_EVENT_TYPES } from '../models/agent-progress-event.model';
import { AuthService } from './auth.service';
import { environment } from '../../../environments/environment';

/**
 * Consumes GET /runs/{clinicalCaseId}/stream via fetch + ReadableStream rather than
 * the native EventSource API. EventSource cannot send a custom Authorization
 * header at all, and this endpoint requires [Authorize(Roles = "Clinician")] (see
 * RunsController's XML docs on the backend) - fetch lets us attach a real Bearer
 * token instead of falling back to a token-in-query-string workaround.
 */
@Injectable({ providedIn: 'root' })
export class RunStreamService {
    private readonly apiBaseUrl = environment.apiBaseUrl;
  private readonly authToken = inject(AuthService);
  
  /**
   * Streams live progress for a run started against the given clinical case. The
   * returned Observable completes when the backend sends a terminal event
   * (RunRefused/RunDegraded/RunFailed/RunCompleted) or the stream naturally ends,
   * and errors if the HTTP request itself fails (network error, non-2xx status).
   * Unsubscribing aborts the underlying fetch, closing the connection so the
   * backend's linked CancellationToken stops in-progress agent/LLM work
   * server-side (see RunsController.Stream's XML docs) - this is the "closing the
   * client connection actually cancels work" path; the explicit Cancel button uses
   * cancelRun() below instead, which works even if the stream stays subscribed.
   */
  streamRunProgress(clinicalCaseId: string): Observable<AgentProgressEvent> {
    return new Observable<AgentProgressEvent>((subscriber) => {
      const abortController = new AbortController();

      (async () => {
        try {
          const token = this.authToken.getToken();
          const response = await fetch(`${this.apiBaseUrl}/runs/${clinicalCaseId}/stream`, {
            method: 'GET',
            headers: {
              Accept: 'text/event-stream',
              ...(token ? { Authorization: `Bearer ${token}` } : {}),
            },
            signal: abortController.signal,
          });

          if (!response.ok || !response.body) {
            subscriber.error(new Error(`Stream request failed with status ${response.status}.`));
            return;
          }

          const reader = response.body.getReader();
          const decoder = new TextDecoder();
          let buffer = '';

          while (true) {
            const { done, value } = await reader.read();
            if (done) break;

            buffer += decoder.decode(value, { stream: true });

            // SSE frames are separated by a blank line. A frame may contain
            // multiple "data:" lines per the spec (not produced by this backend
            // today, but joined correctly regardless).
            let frameEnd: number;
            while ((frameEnd = buffer.indexOf('\n\n')) !== -1) {
              const frame = buffer.slice(0, frameEnd);
              buffer = buffer.slice(frameEnd + 2);

              const dataLines = frame
                .split('\n')
                .filter((line) => line.startsWith('data:'))
                .map((line) => line.slice(5).trimStart());

              if (dataLines.length === 0) continue;

              const event: AgentProgressEvent = JSON.parse(dataLines.join('\n'));
              subscriber.next(event);

              if (TERMINAL_EVENT_TYPES.has(event.EventType)) {
                subscriber.complete();
                return;
              }
            }
          }

          subscriber.complete();
        } catch (err) {
          if ((err as DOMException)?.name === 'AbortError') {
            subscriber.complete(); // expected on unsubscribe/teardown, not a real error
            return;
          }
          subscriber.error(err);
        }
      })();

      return () => abortController.abort();
    });
  }

  /**
   * Prompt 10.2's Cancel button target. runId is the backend-assigned AgentRun id
   * (captured from the first AgentProgressEvent with a non-empty RunId) - NOT the
   * clinicalCaseId used to start the stream. See RunsController's DELETE endpoint.
   */
  async cancelRun(runId: string): Promise<void> {
    const token = this.authToken.getToken();
    const response = await fetch(`${this.apiBaseUrl}/runs/${runId}`, {
      method: 'DELETE',
      headers: {
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
      },
    });

    if (!response.ok && response.status !== 404) {
      throw new Error(`Cancel request failed with status ${response.status}.`);
    }
  }
}