import { Component, computed, input } from '@angular/core';
import { DatePipe } from '@angular/common';
import { Digest } from '../core/api';

/** Renders one stored digest. Shared by the dashboard and the history page. */
@Component({
  selector: 'app-digest-view',
  imports: [DatePipe],
  template: `
    <div class="row spread">
      <h2>{{ digest().createdUtc | date: 'EEE d MMM, HH:mm' }}</h2>
      <div class="row">
        @if (digest().pinged) { <span class="badge hot">Pinged</span> } @else { <span class="badge">Quiet</span> }
        @if (digest().urgency !== 'none') { <span class="badge" [class.hot]="digest().urgency === 'high'">{{ digest().urgency }} urgency</span> }
        <span class="badge">{{ digest().model }}</span>
      </div>
    </div>

    <p>{{ digest().summary }}</p>

    @if (digest().pinged && digest().pingReason) {
      <p class="muted">Ping reason: {{ digest().pingReason }}</p>
    }

    @if (digest().aiFailure) {
      <p class="error">The model failed: {{ digest().aiFailure }}</p>
    }

    @for (e of digest().fetchErrors; track e.account) {
      <p class="error">Could not read {{ e.account }}: {{ e.message }}</p>
    }

    @if (noteworthy().length) {
      <h3 style="margin-top: 1rem">Noteworthy</h3>
      @for (i of noteworthy(); track $index) {
        <div style="padding: .5rem 0; border-top: 1px solid var(--line)">
          <div><strong>{{ i.subject }}</strong></div>
          <div class="row" style="margin: .25rem 0">
            @if (i.category) { <span class="badge">{{ i.category }}</span> }
            @if (i.isJunk) { <span class="badge junk">JUNK</span> }
            @if (i.phishing) { <span class="badge hot">possible phishing</span> }
          </div>
          <div class="muted">{{ i.sender }} · {{ i.account }} · {{ i.receivedUtc | date: 'd MMM HH:mm' }}</div>
          @if (i.why) { <div>{{ i.why }}</div> }
          @if (i.action && i.action !== 'none') { <div class="muted">Suggested action: {{ i.action }}</div> }
        </div>
      }
    }

    @if (keywords().length) {
      <h3 style="margin-top: 1rem">Keyword matches</h3>
      @for (i of keywords(); track $index) {
        <div class="muted" style="padding: .15rem 0">
          @if (i.isJunk) { <span class="badge junk">JUNK</span> }
          {{ i.subject }} · {{ i.account }}
        </div>
      }
    }

    @if (hidden().length) {
      <h3 style="margin-top: 1rem">Hidden from Discord</h3>
      <p class="muted">Flagged as likely phishing or needing no action. Check here if a mail seems to be missing.</p>
      @for (i of hidden(); track $index) {
        <div style="padding: .3rem 0">
          <div class="muted">{{ i.subject }}</div>
          <div class="row" style="margin: .2rem 0">
            @if (i.isJunk) { <span class="badge junk">JUNK</span> }
            @if (i.phishing) { <span class="badge hot">possible phishing</span> }
            <span class="muted">{{ i.sender }} · {{ i.account }}</span>
          </div>
        </div>
      }
    }

    <p class="muted" style="margin-top: 1rem">
      {{ digest().mailCount }} mails from {{ digest().mailboxCount }} mailboxes, {{ digest().junkCount }} in junk.
      @if (digest().omitted) {
        <span>{{ digest().omitted }} did not fit and were not analysed.</span>
      }
    </p>
  `,
})
export class DigestView {
  readonly digest = input.required<Digest>();

  protected readonly noteworthy = computed(() => this.digest().items.filter((i) => i.kind === 'noteworthy'));
  protected readonly keywords = computed(() => this.digest().items.filter((i) => i.kind === 'keyword'));
  protected readonly hidden = computed(() => this.digest().items.filter((i) => i.kind === 'hidden'));
}
