import { Routes } from '@angular/router';
import { authGuard } from './core/http';
import { LoginPage } from './pages/login';
import { Shell } from './pages/shell';
import { DashboardPage } from './pages/dashboard';
import { HistoryPage } from './pages/history';
import { AccountsPage } from './pages/accounts';
import { SettingsPage } from './pages/settings';

export const routes: Routes = [
  { path: 'login', component: LoginPage },
  {
    path: '',
    component: Shell,
    canActivate: [authGuard],
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
      { path: 'dashboard', component: DashboardPage },
      { path: 'history', component: HistoryPage },
      { path: 'accounts', component: AccountsPage },
      { path: 'settings', component: SettingsPage },
    ],
  },
  { path: '**', redirectTo: '' },
];
