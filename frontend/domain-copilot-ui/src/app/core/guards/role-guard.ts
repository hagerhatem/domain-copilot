import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../services/auth.service';

/**
 * Reads required roles from route.data['roles'] (e.g. { roles: ['Clinician'] }).
 * Assumes authGuard already ran (route config puts authGuard first) - this guard
 * only checks role membership, not authentication itself.
 */
export const roleGuard: CanActivateFn = (route) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  const requiredRoles = (route.data['roles'] as string[] | undefined) ?? [];
  const hasAccess = requiredRoles.length === 0 || requiredRoles.some((role) => auth.hasRole(role));

  return hasAccess ? true : router.createUrlTree(['/login']);
};