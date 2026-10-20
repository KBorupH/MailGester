import { Component, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { Api, Digest } from '../core/api';
import { DigestView } from './digest-view';

const PageSize = 30;

@Component({
  selector: 'app-history',
  imports: [DatePipe, DigestView],
  template: `
    <div class="page">
      <h1>History</h1>

      @if (loaded() && list().length === 0) {
        <div class="card"><p class="muted">No digests yet.</p></div>
      }

      @if (selected(); as d) {
        <div class="card">
          <button (click)="selected.set(null)" style="margin-bottom: .75rem">← Back to the list</button>
          <app-digest-view [digest]="d" />
        </div>
      } @else if (list().length) {
        <div class="card">
          <table>
            <thead>
              <tr><th>When</th><th>Summary</th><th>Mails</th><th></th></tr>
            </thead>
            <tbody>
              @for (d of list(); track d.id) {
                <tr style="cursor: pointer" (click)="open(d.id)">
                  <td style="white-space: nowrap">{{ d.createdUtc | date: 'd MMM, HH:mm' }}</td>
                  <td>{{ d.summary.length > 140 ? d.summary.slice(0, 140) + '…' : d.summary }}</td>
                  <td>{{ d.mailCount }}</td>
                  <td>
                    @if (d.pinged) { <span class="badge hot">Pinged</span> }
                    @if (d.fetchErrors.length || d.aiFailure) { <span class="badge junk">Problem</span> }
                  </td>
                </tr>
              }
            </tbody>
          </table>
          @if (more()) {
            <button style="margin-top: .75rem" (click)="loadMore()">Load more</button>
          }
        </div>
      }
    </div>
  `,
})
export class HistoryPage {
  private readonly api = inject(Api);

  protected readonly list = signal<Digest[]>([]);
  protected readonly selected = signal<Digest | null>(null);
  protected readonly loaded = signal(false);
  protected readonly more = signal(false);

  constructor() {
    this.loadMore();
  }

  protected open(id: number): void {
    this.api.digest(id).subscribe((d) => this.selected.set(d));
  }

  protected loadMore(): void {
    this.api.digests(PageSize, this.list().length).subscribe((page) => {
      this.list.update((current) => [...current, ...page]);
      this.more.set(page.length === PageSize);
      this.loaded.set(true);
    });
  }
}
