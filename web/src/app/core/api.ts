import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface DigestOptions {
  schedulerEnabled: boolean;
  timeZone: string;
  runAt: string;
  firstRunLookbackHours: number;
  maxCatchUpDays: number;
  historyRetentionDays: number;
  snippetChars: number;
  maxMailsPerFolder: number;
  language: string;
  userContext: string;
  priorityKeywords: string[];
  pingOnJunkKeywordHit: boolean;
}

export interface OllamaOptions {
  baseUrl: string;
  model: string;
  numCtx: number;
  temperature: number;
  timeoutMinutes: number;
}

export interface DiscordSettings {
  webhookUrl: string | null;
  pingRoleId: string;
  webhookConfigured: boolean;
}

export interface GraphOptions {
  clientId: string;
  authority: string;
}

export interface Settings {
  digest: DigestOptions;
  ollama: OllamaOptions;
  discord: DiscordSettings;
  graph: GraphOptions;
}

export interface Account {
  id: number;
  kind: 'gmail' | 'outlook';
  address: string;
  enabled: boolean;
  hasSecret: boolean;
  signedIn: boolean;
  cursor: string | null;
}

export interface AccountRequest {
  kind?: string;
  address?: string;
  appPassword?: string;
  enabled: boolean;
}

export interface SignIn {
  status: 'pending' | 'done' | 'failed';
  userCode: string | null;
  verificationUrl: string | null;
  error: string | null;
}

export interface RunState {
  running: boolean;
  stage: string;
  startedUtc: string | null;
  lastError: string | null;
  lastRunUtc: string | null;
  nextRunUtc: string | null;
}

export interface Status {
  run: RunState;
  configured: boolean;
}

export interface FetchError {
  account: string;
  message: string;
}

export interface DigestItem {
  kind: 'noteworthy' | 'keyword' | 'hidden';
  account: string;
  folder: string;
  isJunk: boolean;
  sender: string;
  subject: string;
  receivedUtc: string;
  category: string | null;
  why: string | null;
  action: string | null;
  phishing: boolean;
}

export interface Digest {
  id: number;
  createdUtc: string;
  windowFromUtc: string;
  windowToUtc: string;
  mailCount: number;
  junkCount: number;
  omitted: number;
  mailboxCount: number;
  pinged: boolean;
  pingReason: string;
  urgency: string;
  summary: string;
  aiFailure: string | null;
  model: string;
  fetchErrors: FetchError[];
  items: DigestItem[];
}

export interface OllamaState {
  check: { reachable: boolean; error: string | null; models: string[]; modelInstalled: boolean };
  pull: { running: boolean; status: string; percent: number; error: string | null };
}

export interface TestResult {
  ok: boolean;
  error: string | null;
  mailCount?: number;
}

/** Thin typed wrapper over the server API. Every call goes through the interceptor, which adds the CSRF header. */
@Injectable({ providedIn: 'root' })
export class Api {
  private readonly http = inject(HttpClient);

  authState(): Observable<{ setupRequired: boolean; authenticated: boolean }> {
    return this.http.get<{ setupRequired: boolean; authenticated: boolean }>('/api/auth/state');
  }
  setup(password: string) { return this.http.post<void>('/api/auth/setup', { password }); }
  login(password: string) { return this.http.post<void>('/api/auth/login', { password }); }
  logout() { return this.http.post<void>('/api/auth/logout', {}); }
  changePassword(currentPassword: string, newPassword: string) {
    return this.http.post<void>('/api/auth/password', { currentPassword, newPassword });
  }

  settings() { return this.http.get<Settings>('/api/settings'); }
  saveSettings(settings: Settings) { return this.http.put<Settings>('/api/settings', settings); }

  accounts() { return this.http.get<Account[]>('/api/accounts'); }
  addAccount(request: AccountRequest) { return this.http.post<Account>('/api/accounts', request); }
  updateAccount(id: number, request: AccountRequest) { return this.http.put<Account>(`/api/accounts/${id}`, request); }
  deleteAccount(id: number) { return this.http.delete<void>(`/api/accounts/${id}`); }
  startSignIn(id: number) { return this.http.post<SignIn>(`/api/accounts/${id}/signin`, {}); }
  signInState(id: number) { return this.http.get<SignIn>(`/api/accounts/${id}/signin`); }
  testAccount(id: number) { return this.http.post<TestResult>(`/api/accounts/${id}/test`, {}); }

  status() { return this.http.get<Status>('/api/status'); }
  runNow() { return this.http.post<void>('/api/run', {}); }

  digests(take = 20, skip = 0) { return this.http.get<Digest[]>('/api/digests', { params: { take, skip } }); }
  digest(id: number) { return this.http.get<Digest>(`/api/digests/${id}`); }

  ollama() { return this.http.get<OllamaState>('/api/ollama'); }
  pullModel() { return this.http.post<void>('/api/ollama/pull', {}); }
  testDiscord() { return this.http.post<TestResult>('/api/discord/test', {}); }
}

/** Pulls the server's error text out of a failed response, falling back to a generic message. */
export function errorText(err: unknown): string {
  const body = (err as { error?: { error?: string; errors?: string[] } })?.error;
  return body?.errors?.join(' ') ?? body?.error ?? 'Something went wrong. Try again.';
}
