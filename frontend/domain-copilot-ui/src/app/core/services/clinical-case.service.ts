import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ClinicalCaseSummary, CreateClinicalCaseRequest } from '../models/clinical-case.model';

@Injectable({ providedIn: 'root' })
export class ClinicalCaseService {
  private readonly http = inject(HttpClient);
  private readonly apiBaseUrl = environment.apiBaseUrl;

  create(request: CreateClinicalCaseRequest): Promise<ClinicalCaseSummary> {
    return firstValueFrom(this.http.post<ClinicalCaseSummary>(`${this.apiBaseUrl}/clinical-cases`, request));
  }

  list(): Promise<ClinicalCaseSummary[]> {
    return firstValueFrom(this.http.get<ClinicalCaseSummary[]>(`${this.apiBaseUrl}/clinical-cases`));
  }
}