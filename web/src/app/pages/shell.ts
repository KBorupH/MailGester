import { Component, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { Api } from '../core/api';

@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  styles: `
    nav { background: var(--card); border-bottom: 1px solid var(--line); }
    nav .inner { max-width: 960px; margin: 0 auto; padding: .6rem 1rem; display: flex; gap: .25rem; align-items: center; flex-wrap: wrap; }
    .brand { font-weight: 800; margin-right: 1rem; }
    .gester { color: var(--accent); }
    a.link { text-decoration: none; color: var(--muted); padding: .35rem .7rem; border-radius: 7px; }
    a.link.active { color: var(--text); background: var(--bg); }
    .grow { flex: 1; }
  `,
  template: `
    <nav>
      <div class="inner">
        <span class="brand"><img src="logo.svg" alt="" width="28" height="28" style="vertical-align: -8px; margin-right: .4rem" />Mail<span class="gester">Gester</span></span>
        <a class="link" routerLink="/dashboard" routerLinkActive="active">Dashboard</a>
        <a class="link" routerLink="/history" routerLinkActive="active">History</a>
        <a class="link" routerLink="/accounts" routerLinkActive="active">Accounts</a>
        <a class="link" routerLink="/settings" routerLinkActive="active">Settings</a>
        <span class="grow"></span>
        <button (click)="logout()">Sign out</button>
      </div>
    </nav>
    <router-outlet />
  `,
})
export class Shell {
  private readonly api = inject(Api);
  private readonly router = inject(Router);

  protected logout(): void {
    this.api.logout().subscribe(() => void this.router.navigateByUrl('/login'));
  }
}
