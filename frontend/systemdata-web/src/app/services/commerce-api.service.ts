import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { environment } from '../../environments/environment';

export interface QuarantineRow {
  id: number;
  processDate: string | null;
  merchantName: string | null;
  documentNumber: string | null;
  reason: string;
  quarantinedAt: string;
}

@Injectable({ providedIn: 'root' })
export class CommerceApiService {
  private readonly http = inject(HttpClient);
  private readonly base = environment.apiUrl;

  upload(file: File) {
    const data = new FormData();
    data.append('file', file, file.name);
    return this.http.post<{ inserted: number; message: string; fileName: string }>(`${this.base}/upload`, data);
  }

  process(processDate: string) {
    return this.http.post<{ quarantined: number; message: string }>(`${this.base}/process`, { processDate });
  }

  quarantine() {
    return this.http.get<QuarantineRow[]>(`${this.base}/quarantine`);
  }
}
