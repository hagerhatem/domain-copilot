import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { catchError, throwError } from 'rxjs';

/**
 * Global fallback: shows a snackbar for any failed HttpClient request so errors
 * are never silent. Does NOT prevent the error from propagating - components that
 * already handle specific error cases (e.g. AskService's 409 InsufficientEvidence,
 * which is an expected, meaningfully-different-UI outcome, not a failure) still
 * get the error via their own catch block and render their own UI for it. This
 * interceptor's snackbar and a component's own handling are not mutually
 * exclusive; a 409 the component handles will also flash a generic snackbar
 * unless that component's own service call already produced a typed result the
 * component checks for something other than a thrown error before re-throwing -
 * see AskService.ask(), which resolves 409 into a { kind: 'refused' } value rather
 * than letting it reach here as a thrown error, precisely to avoid that.
 */
export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  const snackBar = inject(MatSnackBar);

  return next(req).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse) {
        const message = extractMessage(error);
        snackBar.open(message, 'Dismiss', { duration: 6000 });
      }
      return throwError(() => error);
    }),
  );
};

function extractMessage(error: HttpErrorResponse): string {
  if (error.status === 0) return 'Network error — is the server running?';
  if (typeof error.error === 'string' && error.error.trim()) return error.error;
  if (error.error?.message) return error.error.message as string;
  if (error.status === 401) return 'Session expired — please sign in again.';
  if (error.status === 403) return "You don't have permission to do that.";
  return `Request failed (${error.status}).`;
}