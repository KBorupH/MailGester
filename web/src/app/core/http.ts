import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { catchError, map, throwError } from 'rxjs';
import { Api } from './api';

/** Adds the header the server requires on mutating calls, and sends expired sessions back to the login page. */
export const apiInterceptor: HttpInterceptorFn = (req, next) => {
  const router = inject(Router);
  const isApi = req.url.startsWith('/api');
  const request = isApi ? req.clone({ setHeaders: { 'X-Requested-With': 'MailGester' } }) : req;

  return next(request).pipe(
    catchError((err: HttpErrorResponse) => {
      if (err.status === 401 && isApi && !req.url.startsWith('/api/auth/')) {
        void router.navigateByUrl('/login');
      }
      return throwError(() => err);
    })
  );
};

export const authGuard: CanActivateFn = () => {
  const router = inject(Router);
  return inject(Api)
    .authState()
    .pipe(map((state) => (state.authenticated ? true : router.createUrlTree(['/login']))));
};
