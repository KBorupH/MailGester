import { Component, OnDestroy, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { Api, Digest, OllamaState, Status, errorText } from '../core/api';
import { DigestView } from './digest-view';

@Component({
  selector: 'app-dashboard',
  imports: [DatePipe, RouterLink, DigestView],
  template: `
    <div class="page">
      <div class="card">
        <div class="row spread">
          <h1>Dashboard</h1>
          <button class="primary" (click)="runNow()" [disabled]="status()?.run?.running || !status()?.configured">
            {{ status()?.run?.running ? 'Running…' : 'Run now' }}
          </button>
        </div>

        @if (status(); as s) {
          @if (!s.configured) {
            <p class="warn">
              Nothing to run yet. Add a mail account under <a routerLink="/accounts">Accounts</a> and a Discord webhook under
              <a routerLink="/settings">Settings</a>.
            </p>
          }
          @if (s.run.running) {
            <p>{{ s.run.stage }}…</p>
          }
          <div class="grid">
            <div><span class="muted">Last digest</span><br />{{ s.run.lastRunUtc ? (s.run.lastRunUtc | date: 'EEE d MMM, HH:mm') : 'Never' }}</div>
            <div><span class="muted">Next digest</span><br />{{ s.run.nextRunUtc ? (s.run.nextRunUtc | date: 'EEE d MMM, HH:mm') : 'Scheduler is off' }}</div>
          </div>
          @if (s.run.lastError) {
            <p class="error" style="margin-top: .75rem">Last run failed: {{ s.run.lastError }}</p>
          }
        }
        @if (error()) { <p class="error">{{ error() }}</p> }
      </div>

      @if (ollama(); as o) {
        <div class="card">
          <div class="row spread">
            <h2>Ollama</h2>
            @if (!o.check.reachable) {
              <span class="badge hot">Not reachable</span>
            } @else if (!o.check.modelInstalled) {
              <span class="badge junk">Model missing</span>
            } @else {
              <span class="badge good">Ready</span>
            }
          </div>
          @if (!o.check.reachable) {
            <p class="muted">{{ o.check.error }}. Check the address under <a routerLink="/settings">Settings</a>.</p>
          } @else if (!o.check.modelInstalled) {
            <p class="muted">The configured model is not downloaded yet. You can pull it under <a routerLink="/settings">Settings</a>.</p>
          }
        </div>
      }

      <div class="card">
        @if (latest(); as d) {
          <app-digest-view [digest]="d" />
        } @else {
          <p class="muted">No digest has been posted yet.</p>
        }
      </div>
    </div>
  `,
})
export class DashboardPage implements OnDestroy {
  private readonly api = inject(Api);
  private readonly timer: ReturnType<typeof setInterval>;
  private wasRunning = false;

  protected readonly status = signal<Status | null>(null);
  protected readonly latest = signal<Digest | null>(null);
  protected readonly ollama = signal<OllamaState | null>(null);
  protected readonly error = signal('');

  constructor() {
    this.refresh();
    this.loadLatest();
    this.api.ollama().subscribe((o) => this.ollama.set(o));
    this.timer = setInterval(() => this.refresh(), 3000);
  }

  ngOnDestroy(): void {
    clearInterval(this.timer);
  }

  protected runNow(): void {
    this.error.set('');
    this.api.runNow().subscribe({
      next: () => this.refresh(),
      error: (err) => this.error.set(errorText(err)),
    });
  }

  private refresh(): void {
    this.api.status().subscribe((status) => {
      // A run that just finished means there is a new digest to show.
      if (this.wasRunning && !status.run.running) {
        this.loadLatest();
      }
      this.wasRunning = status.run.running;
      this.status.set(status);
    });
  }

  private loadLatest(): void {
    this.api.digests(1).subscribe((list) => {
      if (list.length === 0) {
        this.latest.set(null);
        return;
      }
      this.api.digest(list[0].id).subscribe((d) => this.latest.set(d));
    });
  }
}
