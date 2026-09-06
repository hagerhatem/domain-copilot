import { Routes } from '@angular/router';
import { authGuard } from './core/guards/auth-guard';
import { roleGuard } from './core/guards/role-guard';

export const routes: Routes = [
  { path: 'login', loadComponent: () => import('./features/login/login').then((m) => m.Login) },
  {
    path: 'ingest',
    canActivate: [authGuard],
    loadComponent: () => import('./features/ingest/ingest').then((m) => m.Ingest),
  },
  {
    path: 'ask',
    canActivate: [authGuard],
    loadComponent: () => import('./features/ask/ask').then((m) => m.Ask),
  },
  {
    path: 'workflow',
    canActivate: [authGuard],
    loadComponent: () => import('./features/run-workflow/run-workflow').then((m) => m.RunWorkflow),
  },
  {
    path: 'approvals',
    canActivate: [authGuard, roleGuard],
    data: { roles: ['Clinician'] },
    loadComponent: () => import('./features/approval-queue/approval-queue').then((m) => m.ApprovalQueue),
  },
  {
    path: 'history',
    canActivate: [authGuard, roleGuard],
    data: { roles: ['Clinician'] },
    loadComponent: () => import('./features/run-history/run-history').then((m) => m.RunHistory),
  },
//   {
//     path: 'trace/:runId',
//     canActivate: [authGuard],
//     loadComponent: () => import('./features/trace-viewer/trace-viewer').then((m) => m.TraceViewer),
//   },
  {
  path: 'trace',
  canActivate: [authGuard],
  loadComponent: () => import('./features/trace-viewer/trace-viewer').then((m) => m.TraceViewer),
},
{
  path: 'trace/:runId',
  canActivate: [authGuard],
  loadComponent: () => import('./features/trace-viewer/trace-viewer').then((m) => m.TraceViewer),
},
  { path: '', pathMatch: 'full', redirectTo: 'login' },
  { path: '**', redirectTo: 'login' },
];