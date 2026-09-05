import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { AuthService } from '../services/auth.service';

/**
 * Attaches the Authorization header to every HttpClient request. Does NOT cover
 * RunStreamService's SSE calls (those use fetch directly, not HttpClient - see
 * that service's docs for why: EventSource can't set custom headers, and fetch
 * with a manually-attached header was chosen specifically to keep the streaming
 * endpoint behind the same [Authorize(Roles = "Clinician")] as everything else).
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const token = inject(AuthService).getToken();

  if (!token) {
    return next(req);
  }

  return next(req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }));
};