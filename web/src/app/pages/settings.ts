import { Component, OnDestroy, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Observable, switchMap } from 'rxjs';
import { Api, OllamaState, Settings, errorText } from '../core/api';

@Component({
  selector: 'app-settings',
  imports: [FormsModule],
  template: `
    <div class="page">
      <h1>Settings</h1>

      @if (form) {
        <div class="card">
          <h2>Schedule</h2>
          <div class="grid">
            <label class="check"><input type="checkbox" [(ngModel)]="form.digest.schedulerEnabled" /> Run the daily digest</label>
            <label>Run at (local to the time zone)
              <input type="time" [(ngModel)]="form.digest.runAt" />
            </label>
            <label>Time zone
              <input [(ngModel)]="form.digest.timeZone" placeholder="Europe/Copenhagen" />
            </label>
            <label>First run lookback (hours)
              <input type="number" [(ngModel)]="form.digest.firstRunLookbackHours" />
            </label>
            <label>Max catch up after downtime (days)
              <input type="number" [(ngModel)]="form.digest.maxCatchUpDays" />
            </label>
            <label>Keep history (days)
              <input type="number" [(ngModel)]="form.digest.historyRetentionDays" />
            </label>
          </div>
          <p class="muted" style="margin-top: .5rem">
            If the PC was off at the scheduled time, the digest runs as soon as the app starts again.
          </p>
        </div>

        <div class="card">
          <h2>What the AI gets</h2>
          <div class="grid" style="margin-bottom: .75rem">
            <label>Language of the digest
              <input [(ngModel)]="form.digest.language" />
            </label>
            <label>Snippet length (characters)
              <input type="number" [(ngModel)]="form.digest.snippetChars" />
            </label>
            <label>Max mails per folder
              <input type="number" [(ngModel)]="form.digest.maxMailsPerFolder" />
            </label>
          </div>
          <label>About you and what matters
            <textarea [(ngModel)]="form.digest.userContext" placeholder="For example: I am job searching and want recruiter mail surfaced, even from junk."></textarea>
          </label>
          <label style="margin-top: .75rem">Priority keywords, one per line
            <textarea [(ngModel)]="keywordsText"></textarea>
          </label>
          <label class="check" style="margin-top: .75rem">
            <input type="checkbox" [(ngModel)]="form.digest.pingOnJunkKeywordHit" />
            Always ping when a keyword matches in a junk folder, even if the AI stays quiet
          </label>
        </div>

        <div class="card">
          <h2>Ollama</h2>
          <div class="grid">
            <label>Address
              <input [(ngModel)]="form.ollama.baseUrl" placeholder="http://ollama:11434" />
            </label>
            <label>Model
              <input [(ngModel)]="form.ollama.model" list="installed-models" />
              <datalist id="installed-models">
                @for (m of (ollama()?.check?.models ?? []); track m) { <option [value]="m"></option> }
              </datalist>
            </label>
            <label>Context size (tokens)
              <input type="number" [(ngModel)]="form.ollama.numCtx" />
            </label>
            <label>Temperature
              <input type="number" step="0.1" [(ngModel)]="form.ollama.temperature" />
            </label>
            <label>Timeout (minutes)
              <input type="number" [(ngModel)]="form.ollama.timeoutMinutes" />
            </label>
          </div>
          <div class="row" style="margin-top: .75rem">
            <button (click)="checkOllama()">Save and check</button>
            <button (click)="pullModel()" [disabled]="ollama()?.pull?.running">Save and download model</button>
          </div>
          @if (ollama(); as o) {
            @if (!o.check.reachable) {
              <p class="error" style="margin-top: .5rem">Not reachable: {{ o.check.error }}</p>
            } @else {
              <p class="ok" style="margin-top: .5rem">Reachable, {{ o.check.models.length }} models installed.</p>
              @if (!o.check.modelInstalled) { <p class="warn">The configured model is not downloaded yet.</p> }
            }
            @if (o.pull.running) {
              <p>Downloading: {{ o.pull.status }} {{ o.pull.percent }}%</p>
            } @else if (o.pull.error) {
              <p class="error">Download failed: {{ o.pull.error }}</p>
            }
          }
          <p class="muted" style="margin-top: .5rem">
            The compose file starts an Ollama container named ollama. To use another Ollama, remove that service and enter its address here.
          </p>
        </div>

        <div class="card">
          <h2>Discord</h2>
          <div class="grid">
            <label>Webhook URL
              <input type="password" [(ngModel)]="webhook" autocomplete="off"
                     [placeholder]="form.discord.webhookConfigured ? 'Stored. Enter a new one to replace it.' : 'https://discord.com/api/webhooks/...'" />
            </label>
            <label>Role ID to ping
              <input [(ngModel)]="form.discord.pingRoleId" placeholder="Digits only" />
            </label>
          </div>
          <div class="row" style="margin-top: .75rem">
            <button (click)="testDiscord()">Save and send test ping</button>
            @if (discordResult()) { <span>{{ discordResult() }}</span> }
          </div>
        </div>

        <div class="card">
          <h2>Outlook sign in</h2>
          <div class="grid">
            <label>Azure application (client) ID
              <input [(ngModel)]="form.graph.clientId" placeholder="00000000-0000-0000-0000-000000000000" />
            </label>
            <label>Authority
              <input [(ngModel)]="form.graph.authority" />
            </label>
          </div>
          <p class="muted" style="margin-top: .5rem">
            Register a free app in the Microsoft Entra admin center (App registrations, New registration). Choose accounts in any
            organizational directory and personal Microsoft accounts, turn on Allow public client flows under Authentication, add the
            delegated Microsoft Graph permission Mail.Read, then paste the Application (client) ID here.
          </p>
        </div>

        <div class="savebar row">
          <button class="primary" (click)="save()" [disabled]="saving()">Save settings</button>
          @if (message()) { <span [class.error]="messageIsError()" [class.ok]="!messageIsError()">{{ message() }}</span> }
        </div>

        <div class="card" style="margin-top: 1.5rem">
          <h2>Admin password</h2>
          <div class="grid">
            <label>Current password
              <input type="password" [(ngModel)]="currentPassword" autocomplete="current-password" />
            </label>
            <label>New password (at least 10 characters)
              <input type="password" [(ngModel)]="newPassword" autocomplete="new-password" />
            </label>
          </div>
          <div class="row" style="margin-top: .75rem">
            <button (click)="changePassword()">Change password</button>
            @if (passwordMessage()) { <span>{{ passwordMessage() }}</span> }
          </div>
        </div>
      } @else {
        <p class="muted">Loading…</p>
      }
    </div>
  `,
})
export class SettingsPage implements OnDestroy {
  private readonly api = inject(Api);
  private pullTimer: ReturnType<typeof setInterval> | undefined;

  protected form: Settings | null = null;
  protected keywordsText = '';
  protected webhook = '';
  protected currentPassword = '';
  protected newPassword = '';

  protected readonly saving = signal(false);
  protected readonly message = signal('');
  protected readonly messageIsError = signal(false);
  protected readonly ollama = signal<OllamaState | null>(null);
  protected readonly discordResult = signal('');
  protected readonly passwordMessage = signal('');

  constructor() {
    this.api.settings().subscribe((s) => this.apply(s));
    this.api.ollama().subscribe((o) => this.ollama.set(o));
  }

  ngOnDestroy(): void {
    clearInterval(this.pullTimer);
  }

  protected save(): void {
    this.saving.set(true);
    this.persist().subscribe({
      next: () => this.report('Saved.', false),
      error: (err) => this.report(errorText(err), true),
    });
  }

  protected checkOllama(): void {
    this.persist()
      .pipe(switchMap(() => this.api.ollama()))
      .subscribe({
        next: (o) => this.ollama.set(o),
        error: (err) => this.report(errorText(err), true),
      });
  }

  protected pullModel(): void {
    this.persist()
      .pipe(switchMap(() => this.api.pullModel()))
      .subscribe({
        next: () => this.watchPull(),
        error: (err) => this.report(errorText(err), true),
      });
  }

  protected testDiscord(): void {
    this.discordResult.set('Sending…');
    this.persist()
      .pipe(switchMap(() => this.api.testDiscord()))
      .subscribe({
        next: (r) => this.discordResult.set(r.ok ? 'Sent. Check Discord.' : `Failed: ${r.error}`),
        error: (err) => this.discordResult.set(errorText(err)),
      });
  }

  protected changePassword(): void {
    this.api.changePassword(this.currentPassword, this.newPassword).subscribe({
      next: () => {
        this.currentPassword = '';
        this.newPassword = '';
        this.passwordMessage.set('Password changed.');
      },
      error: (err) => this.passwordMessage.set(errorText(err)),
    });
  }

  private watchPull(): void {
    clearInterval(this.pullTimer);
    this.pullTimer = setInterval(() => {
      this.api.ollama().subscribe((o) => {
        this.ollama.set(o);
        if (!o.pull.running) {
          clearInterval(this.pullTimer);
        }
      });
    }, 1500);
  }

  /** Saves the form, so the check and test buttons always act on what is on screen. */
  private persist(): Observable<Settings> {
    const form = this.form!;
    form.digest.priorityKeywords = this.keywordsText.split('\n').map((k) => k.trim()).filter((k) => k.length > 0);
    form.discord.webhookUrl = this.webhook.trim() || null;

    return this.api.saveSettings(form).pipe(
      switchMap((saved) => {
        this.apply(saved);
        return [saved];
      })
    );
  }

  private apply(settings: Settings): void {
    this.form = settings;
    this.keywordsText = settings.digest.priorityKeywords.join('\n');
    this.webhook = '';
  }

  private report(text: string, isError: boolean): void {
    this.saving.set(false);
    this.messageIsError.set(isError);
    this.message.set(text);
  }
}
