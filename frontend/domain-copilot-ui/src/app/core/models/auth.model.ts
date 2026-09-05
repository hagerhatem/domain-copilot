/**
 * Shape of the JWT payload issued by POST /auth/login (see Program.cs's
 * /auth/login handler: NameIdentifier, Email, and zero-or-more Role claims). JWT
 * claim URIs are the verbose .NET ClaimTypes.* URIs, not short names - this is
 * exactly what the token contains on the wire, not a simplified guess.
 */
export interface DecodedJwtPayload {
  'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier': string;
  'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress'?: string;
  'http://schemas.microsoft.com/ws/2008/06/identity/claims/role'?: string | string[];
  exp: number;
}

export interface CurrentUser {
  userId: string;
  email: string | null;
  roles: string[];
}

export interface LoginRequest {
  email: string;
  password: string;
}

export interface LoginResponse {
  token: string;
}