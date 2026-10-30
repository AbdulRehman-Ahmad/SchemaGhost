import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

export interface Leaf { path: string; type: string; sample: string }
export interface Change {
  kind: string; legacyPath: string | null; modernPath: string | null;
  legacyType: string | null; modernType: string | null;
  breaking: boolean; inferred: boolean; confidence: number | null; note: string;
}
export interface CompareResponse { changes: Change[]; legacyFields: Leaf[]; modernFields: Leaf[]; unchanged: number }
export interface Suggestion {
  legacyPath: string; modernPath: string | null; transform: string; confidence: number; source: string;
  reason: string; needsReview: boolean; aiAlternativePath: string | null; aiAlternativeReason: string | null;
}
export interface AiStatus { requested: boolean; used: boolean; provider: string; model: string; message: string | null }
export interface SuggestResponse { suggestions: Suggestion[]; ai: AiStatus }
export interface Check { legacyPath: string; status: string; detail: string }
export interface Warning { legacyPath: string | null; severity: string; message: string }
export interface ValidateResponse {
  checks: Check[]; warnings: Warning[]; adapted: unknown;
  passed: number; failed: number; unmapped: number; coverage: number; safe: boolean;
}
export interface GenerateResponse { adapterCs: string; mappingJson: string; adaptedJson: string }
export interface AppConfig {
  ai: { enabled: boolean; provider: string; model: string; sendsDataToThirdParty: boolean };
  transforms: string[];
}
export interface MappingPayload {
  legacyPath: string; modernPath: string | null; transform: string; confidence: number; source: string; reason: string;
}

export function errMsg(e: unknown): string {
  if (e instanceof HttpErrorResponse) {
    const b = e.error as { error?: string; errors?: Record<string, string[]> } | null;
    if (e.status === 0) return 'Cannot reach the SchemaGhost API. Is the backend running?';
    if (e.status === 429) return 'Too many requests. Please wait a minute and try again.';
    if (b && b.errors) return Object.values(b.errors).flat().join(' ');
    if (b && b.error) return b.error;
    return 'Request failed (HTTP ' + e.status + ').';
  }
  return e instanceof Error ? e.message : 'Unexpected error.';
}

@Injectable({ providedIn: 'root' })
export class Api {
  private http = inject(HttpClient);

  config() { return firstValueFrom(this.http.get<AppConfig>('/api/config')); }
  compare(legacy: unknown, modern: unknown) {
    return firstValueFrom(this.http.post<CompareResponse>('/api/compare', { legacy, modern }));
  }
  suggest(legacy: unknown, modern: unknown, mode: string) {
    return firstValueFrom(this.http.post<SuggestResponse>('/api/suggest', { legacy, modern, mode }));
  }
  validate(legacy: unknown, modern: unknown, mappings: MappingPayload[]) {
    return firstValueFrom(this.http.post<ValidateResponse>('/api/validate', { legacy, modern, mappings }));
  }
  generate(legacy: unknown, modern: unknown, mappings: MappingPayload[]) {
    return firstValueFrom(this.http.post<GenerateResponse>('/api/generate', { legacy, modern, mappings }));
  }
  downloadZip(legacy: unknown, modern: unknown, mappings: MappingPayload[]) {
    return firstValueFrom(this.http.post('/api/download', { legacy, modern, mappings }, { responseType: 'blob' }));
  }
}
