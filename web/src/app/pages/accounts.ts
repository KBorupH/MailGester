import { Component, OnDestroy, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Account, Api, SignIn, errorText } from '../core/api';

@Component({
  selector: 'app-accounts',
  imports: [DatePipe, FormsModule],
  template: `
    <div class="page">
      <h1>Accounts</h1>

      <div class="card">
        @if (accounts().length === 0) {
          <p class="muted">No accounts yet. Add one below.</p>
        }
        @for (a of accounts(); track a.id) {
          <div style="padding: .75rem 0; border-bottom: 1px solid var(--line)">
            <div class="row spread">
              <div>
                <strong>{{ a.address }}</strong>
                <span class="badge" style="margin-left: .5rem">{{ a.kind }}</span>
                @if (a.kind === 'outlook') {
                  @if (a.signedIn) { <span class="badge good">signed in</span> } @else { <span class="badge junk">needs sign in</span> }
                }
                <div class="muted">Last read: {{ a.cursor ? (a.cursor | date: 'd MMM, HH:mm') : 'never' }}</div>
              </div>
              <div class="row">
                <label class="check"><input type="checkbox" [checked]="a.enabled" (change)="toggle(a)" /> Enabled</label>
                <button (click)="test(a)">Test</button>
                @if (a.kind === 'outlook') {
                  <button (click)="signIn(a)">{{ a.signedIn ? 'Sign in again' : 'Sign in' }}</button>
                } @else {
                  <button (click)="editing.set(editing() === a.id ? null : a.id)">Change password</button>
                }
                <button class="danger" (click)="remove(a)">Remove</button>
              </div>
            </div>

            @if (messages()[a.id]; as m) {
              <p class="muted" style="margin: .5rem 0 0">{{ m }}</p>
            }

            @if (editing() === a.id) {
              <div class="row" style="margin-top: .5rem">
                <input type="password" placeholder="New app password" [(ngModel)]="newSecret" style="max-width: 320px" />
                <button class="primary" (click)="saveSecret(a)">Save</button>
              </div>
            }

            @if (signIns()[a.id]; as s) {
              @if (s.status === 'pending') {
                <div style="margin-top: .5rem">
                  @if (s.userCode) {
                    <p>Open <a [href]="s.verificationUrl" target="_blank" rel="noopener">{{ s.verificationUrl }}</a>, sign in as {{ a.address }} and enter:</p>
                    <p class="code">{{ s.userCode }}</p>
                  } @else {
                    <p class="muted">Requesting a code…</p>
                  }
                </div>
              } @else if (s.status === 'failed') {
                <p class="error">{{ s.error }}</p>
              }
            }
          </div>
        }
      </div>

      <div class="card">
        <h2>Add account</h2>
        <div class="grid">
          <label>Type
            <select [(ngModel)]="kind">
              <option value="gmail">Gmail (app password)</option>
              <option value="outlook">Outlook, Hotmail or Live</option>
            </select>
          </label>
          <label>Email address
            <input type="email" [(ngModel)]="address" placeholder="you@example.com" />
          </label>
          @if (kind === 'gmail') {
            <label>App password
              <input type="password" [(ngModel)]="appPassword" autocomplete="off" />
            </label>
          }
        </div>
        @if (kind === 'gmail') {
          <p class="muted" style="margin-top: .5rem">
            Needs 2 step verification on the Google account. Create an app password in the Google account security settings.
          </p>
        } @else {
          <p class="muted" style="margin-top: .5rem">
            After adding, use Sign in on the account. The Azure client ID must be set under Settings first.
          </p>
        }
        <div class="row" style="margin-top: .75rem">
          <button class="primary" (click)="add()" [disabled]="busy()">Add account</button>
          @if (error()) { <span class="error">{{ error() }}</span> }
        </div>
      </div>
    </div>
  `,
})
export class AccountsPage implements OnDestroy {
  private readonly api = inject(Api);
  private readonly polls = new Map<number, ReturnType<typeof setInterval>>();

  protected readonly accounts = signal<Account[]>([]);
  protected readonly messages = signal<Record<number, string>>({});
  protected readonly signIns = signal<Record<number, SignIn>>({});
  protected readonly editing = signal<number | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal('');

  protected kind = 'gmail';
  protected address = '';
  protected appPassword = '';
  protected newSecret = '';

  constructor() {
    this.load();
  }

  ngOnDestroy(): void {
    this.polls.forEach((timer) => clearInterval(timer));
  }

  protected add(): void {
    this.error.set('');
    this.busy.set(true);
    this.api.addAccount({ kind: this.kind, address: this.address, appPassword: this.appPassword, enabled: true }).subscribe({
      next: () => {
        this.address = '';
        this.appPassword = '';
        this.busy.set(false);
        this.load();
      },
      error: (err) => {
        this.error.set(errorText(err));
        this.busy.set(false);
      },
    });
  }

  protected toggle(a: Account): void {
    this.api.updateAccount(a.id, { address: a.address, enabled: !a.enabled }).subscribe({
      next: () => this.load(),
      error: (err) => this.message(a.id, errorText(err)),
    });
  }

  protected saveSecret(a: Account): void {
    this.api.updateAccount(a.id, { address: a.address, enabled: a.enabled, appPassword: this.newSecret }).subscribe({
      next: () => {
        this.newSecret = '';
        this.editing.set(null);
        this.message(a.id, 'App password updated.');
        this.load();
      },
      error: (err) => this.message(a.id, errorText(err)),
    });
  }

  protected test(a: Account): void {
    this.message(a.id, 'Testing…');
    this.api.testAccount(a.id).subscribe({
      next: (r) => this.message(a.id, r.ok ? `Connected. ${r.mailCount} mails in the last hour.` : `Failed: ${r.error}`),
      error: (err) => this.message(a.id, errorText(err)),
    });
  }

  protected signIn(a: Account): void {
    this.api.startSignIn(a.id).subscribe({
      next: (state) => {
        this.setSignIn(a.id, state);
        this.poll(a.id);
      },
      error: (err) => this.message(a.id, errorText(err)),
    });
  }

  protected remove(a: Account): void {
    if (!confirm(`Remove ${a.address}? Its mail stays untouched.`)) {
      return;
    }
    this.api.deleteAccount(a.id).subscribe(() => this.load());
  }

  private poll(id: number): void {
    clearInterval(this.polls.get(id));
    const timer = setInterval(() => {
      this.api.signInState(id).subscribe((state) => {
        this.setSignIn(id, state);
        if (state.status !== 'pending') {
          clearInterval(timer);
          this.polls.delete(id);
          this.load();
        }
      });
    }, 2000);
    this.polls.set(id, timer);
  }

  private load(): void {
    this.api.accounts().subscribe((list) => this.accounts.set(list));
  }

  private message(id: number, text: string): void {
    this.messages.update((m) => ({ ...m, [id]: text }));
  }

  private setSignIn(id: number, state: SignIn): void {
    this.signIns.update((s) => ({ ...s, [id]: state }));
  }
}
