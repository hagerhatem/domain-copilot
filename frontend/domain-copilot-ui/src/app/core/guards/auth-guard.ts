// import { inject } from '@angular/core';
// import { CanActivateFn, Router } from '@angular/router';
// import { AuthService } from '../services/auth.service';

// export const authGuard: CanActivateFn = () => {
//   const auth = inject(AuthService);
//   const router = inject(Router);

//   return auth.isAuthenticated() ? true : router.createUrlTree(['/login']);
// };
import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../services/auth.service';

export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (auth.isAuthenticated()) return true;

  // Preserves the originally-requested URL as a query param so Login can redirect
  // back to it after a successful sign-in, instead of always landing on /workflow.
  return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
};