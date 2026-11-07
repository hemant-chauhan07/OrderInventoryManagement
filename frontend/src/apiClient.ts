import type { AuthResponse } from './models';

const AUTH_KEY = 'oi-auth';
export class ApiError extends Error {
  constructor(public status: number, message: string) { super(message); this.name = 'ApiError'; }
}
export function readAuth(): AuthResponse | null {
  try { const value = localStorage.getItem(AUTH_KEY); return value ? JSON.parse(value) as AuthResponse : null; }
  catch { localStorage.removeItem(AUTH_KEY); return null; }
}
export function writeAuth(auth: AuthResponse | null): void {
  if (auth) localStorage.setItem(AUTH_KEY, JSON.stringify(auth)); else localStorage.removeItem(AUTH_KEY);
}
let refreshInFlight: Promise<AuthResponse | null> | null = null;
async function refreshAuth(): Promise<AuthResponse | null> {
  const current = readAuth();
  if (!current?.refreshToken) return null;
  if (!refreshInFlight) {
    refreshInFlight = fetch('/api/auth/refresh', {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ refreshToken: current.refreshToken })
    }).then(async response => {
      if (!response.ok) throw new ApiError(response.status, 'Your session expired. Please sign in again.');
      const next = await response.json() as AuthResponse; writeAuth(next); return next;
    }).catch(() => { writeAuth(null); window.dispatchEvent(new Event('oi-session-expired')); return null; }).finally(() => { refreshInFlight = null; });
  }
  return refreshInFlight;
}
async function parseError(response: Response): Promise<ApiError> {
  const body = await response.json().catch(() => null) as { detail?: string; title?: string; message?: string } | null;
  let message = body?.detail || body?.message || body?.title || `Request failed (${response.status}).`;
  if (response.status === 409) message = `Conflict: ${body?.detail || 'The requested change could not be completed. Refresh and try again.'}`;
  if (response.status === 429) message = 'Too many login attempts. Please wait a moment before trying again.';
  if (response.status === 401) message = 'Your session has expired. Please sign in again.';
  return new ApiError(response.status, message);
}
export async function apiRequest<T>(path: string, init: RequestInit = {}): Promise<T> {
  const send = (token?: string) => {
    const headers = new Headers(init.headers);
    if (init.body && !headers.has('Content-Type')) headers.set('Content-Type', 'application/json');
    if (token) headers.set('Authorization', `Bearer ${token}`);
    return fetch(path, { ...init, headers });
  };
  const initialAuth = readAuth();
  let response = await send(initialAuth?.accessToken);
  if (response.status === 401 && path !== '/api/auth/refresh') {
    const refreshed = await refreshAuth();
    if (refreshed?.accessToken) response = await send(refreshed.accessToken);
  }
  if (!response.ok) throw await parseError(response);
  if (response.status === 204) return undefined as T;
  return response.json() as Promise<T>;
}
