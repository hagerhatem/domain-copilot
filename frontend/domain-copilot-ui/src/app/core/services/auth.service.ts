import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { CurrentUser, DecodedJwtPayload, LoginRequest, LoginResponse } from '../models/auth.model';
import { environment } from '../../../environments/environment';

const STORAGE_KEY = 'domain-copilot.access_token';
const ROLE_CLAIM = 'http://schemas.microsoft.com/ws/2008/06/identity/claims/role';
const NAME_ID_CLAIM = 'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier';
const EMAIL_CLAIM = 'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress';

/**
 * Replaces the Phase 10.2 AuthTokenService placeholder. Talks to the real
 * POST /auth/login (no /api prefix - see Program.cs) endpoint, decodes the
 * returned JWT client-side (base64, no signature verification - the backend is
 * the only party that needs to verify the signature; the frontend only reads
 * claims to drive UI state like route guards and role-based nav), and exposes
 * the current user as a signal.
 *
 * TODO: no /auth/register or /auth/refresh endpoint exists in the backend yet -
 * only the two seeded demo accounts (clinician@demo.local / admin@demo.local,
 * password Demo#12345) can log in until Identity's user-management surface is
 * built out further (see ApprovalController's backend XML docs on this gap).
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);

  private readonly apiBaseUrl = environment.apiBaseUrl;

  private readonly currentUserSignal = signal<CurrentUser | null>(this.readUserFromStorage());
  readonly currentUser = this.currentUserSignal.asReadonly();
  readonly isAuthenticated = computed(() => this.currentUserSignal() !== null);

  async login(request: LoginRequest): Promise<void> {
    const response = await firstValueFrom(
      this.http.post<LoginResponse>(`${this.apiBaseUrl}/auth/login`, request),
    );

    localStorage.setItem(STORAGE_KEY, response.token);
    this.currentUserSignal.set(this.decodeUser(response.token));
  }

  logout(): void {
    localStorage.removeItem(STORAGE_KEY);
    this.currentUserSignal.set(null);
    this.router.navigateByUrl('/login');
  }

  /** Used by authInterceptor and by RunStreamService (which cannot use HttpClient - see that class's docs). */
  getToken(): string | null {
    return localStorage.getItem(STORAGE_KEY);
  }

  hasRole(role: string): boolean {
    return this.currentUserSignal()?.roles.includes(role) ?? false;
  }

  private readUserFromStorage(): CurrentUser | null {
    const token = localStorage.getItem(STORAGE_KEY);
    return token ? this.decodeUser(token) : null;
  }

  private decodeUser(token: string): CurrentUser | null {
    try {
      const payloadSegment = token.split('.')[1];
      const payload: DecodedJwtPayload = JSON.parse(atob(payloadSegment.replace(/-/g, '+').replace(/_/g, '/')));

      // Expired tokens are treated as "not logged in" rather than throwing -
      // callers just see isAuthenticated() as false.
      if (payload.exp * 1000 < Date.now()) {
        localStorage.removeItem(STORAGE_KEY);
        return null;
      }

      const rawRoles = payload[ROLE_CLAIM];
      const roles = Array.isArray(rawRoles) ? rawRoles : rawRoles ? [rawRoles] : [];

      return {
        userId: payload[NAME_ID_CLAIM],
        email: payload[EMAIL_CLAIM] ?? null,
        roles,
      };
    } catch {
      localStorage.removeItem(STORAGE_KEY);
      return null;
    }
  }
}