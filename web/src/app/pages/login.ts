import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Api, errorText } from '../core/api';

@Component({
  selector: 'app-login',
  imports: [FormsModule],
  template: `
    <div class="page" style="max-width: 420px; padding-top: 12vh">
      <div class="card">
        <div style="text-align: center; margin-bottom: 1rem">
          <img src="logo.svg" alt="" width="72" height="72" style="display: block; margin: 0 auto .5rem" />
          <div class="wordmark">Mail<span>Gester</span></div>
          <p class="muted" style="margin: .25rem 0 0">Never miss the mail that matters.</p>
        </div>
        @if (loading()) {
          <p class="muted">Loading…</p>
        } @else if (setupRequired()) {
          <h1>Welcome</h1>
          <p class="muted">Choose an admin password for this dashboard. It protects your mail credentials, so use a long one.</p>
          <form (ngSubmit)="submit()" class="row" style="flex-direction: column; align-items: stretch">
            <label>Password (at least 10 characters)
              <input type="password" name="password" [(ngModel)]="password" autocomplete="new-password" />
            </label>
            <label>Repeat password
              <input type="password" name="repeat" [(ngModel)]="repeat" autocomplete="new-password" />
            </label>
            <button class="primary" type="submit" [disabled]="busy()">Create password</button>
          </form>
        } @else {
          <form (ngSubmit)="submit()" class="row" style="flex-direction: column; align-items: stretch">
            <label>Password
              <input type="password" name="password" [(ngModel)]="password" autocomplete="current-password" />
            </label>
            <button class="primary" type="submit" [disabled]="busy()">Sign in</button>
          </form>
        }
        @if (error()) {
          <p class="error" style="margin-top: .75rem">{{ error() }}</p>
        }
        <p class="muted" style="text-align: center; margin: 1rem 0 0; font-size: .8rem">
          Private by design: local AI, encrypted credentials.
        </p>
      </div>
    </div>
  `,
})
export class LoginPage {
  private readonly api = inject(Api);
  private readonly router = inject(Router);

  protected readonly loading = signal(true);
  protected readonly setupRequired = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal('');

  protected password = '';
  protected repeat = '';

  constructor() {
    this.api.authState().subscribe((state) => {
      if (state.authenticated) {
        void this.router.navigateByUrl('/');
        return;
      }
      this.setupRequired.set(state.setupRequired);
      this.loading.set(false);
    });
  }

  protected submit(): void {
    this.error.set('');

    if (this.setupRequired() && this.password !== this.repeat) {
      this.error.set('The passwords do not match.');
      return;
    }

    this.busy.set(true);
    const call = this.setupRequired() ? this.api.setup(this.password) : this.api.login(this.password);
    call.subscribe({
      next: () => void this.router.navigateByUrl('/'),
      error: (err) => {
        this.error.set(errorText(err));
        this.busy.set(false);
      },
    });
  }
}
