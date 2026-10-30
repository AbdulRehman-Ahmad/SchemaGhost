import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import {
  Api, AiStatus, AppConfig, Change, CompareResponse, GenerateResponse, MappingPayload, Suggestion, ValidateResponse, errMsg,
} from './api';
import { SAMPLES } from './samples';

type MappingRow = Suggestion & { approved: boolean };
type Which = 'legacy' | 'modern';

function parseObj(text: string): { value: object | null; error: string | null } {
  if (!text.trim()) return { value: null, error: 'Paste a JSON object.' };
  try {
    const v: unknown = JSON.parse(text);
    if (v === null || typeof v !== 'object' || Array.isArray(v)) return { value: null, error: 'The top level must be a JSON object.' };
    return { value: v, error: null };
  } catch (e) {
    return { value: null, error: (e as Error).message };
  }
}

@Component({
  selector: 'app-workspace',
  template: `
    <div class="mb-6 flex flex-wrap items-center justify-between gap-3">
      <div>
        <h1 class="text-2xl font-bold">Workspace</h1>
        <p class="text-sm text-slate-400">Compare, map, verify, generate. Nothing is applied until you approve it.</p>
      </div>
      <div class="text-xs" aria-live="polite">
        @if (config(); as c) {
          @if (c.ai.enabled) {
            <span class="badge border-emerald-500/40 bg-emerald-500/10 text-emerald-300">AI: {{ c.ai.model }} via {{ c.ai.provider }}</span>
          } @else {
            <span class="badge border-amber-500/40 bg-amber-500/10 text-amber-300">AI not configured · heuristic only</span>
          }
        }
      </div>
    </div>

    @if (config()?.ai?.sendsDataToThirdParty) {
      <p class="mb-4 rounded-lg border border-amber-500/30 bg-amber-500/10 p-3 text-sm text-amber-200">
        Privacy: in AI modes, your JSON is sent to a third-party hosted model provider. Use "Heuristic only" to keep it on this server.
      </p>
    }

    <!-- 1. Input -->
    <section class="card p-4" aria-labelledby="s1">
      <div class="mb-3 flex flex-wrap items-center justify-between gap-3">
        <h2 id="s1" class="font-semibold"><span class="mr-2 text-violet-400">1</span>Paste the two API responses</h2>
        <div class="flex flex-wrap items-center gap-2">
          <label for="sample" class="sr-only">Load example</label>
          <select id="sample" class="field" style="width:auto" (change)="loadSample($any($event.target).value)">
            @for (s of samples; track s.id) { <option [value]="s.id">{{ s.name }}</option> }
          </select>
          <button type="button" class="btn" (click)="clearAll()">Clear</button>
        </div>
      </div>
      @if (sampleDescription(); as d) { <p class="mb-3 text-sm text-slate-400">{{ d }}</p> }

      <div class="grid gap-4 lg:grid-cols-2">
        @for (w of whichList; track w) {
          <div>
            <div class="mb-1 flex items-center justify-between">
              <label [for]="w" class="text-sm font-medium text-slate-300">
                {{ w === 'legacy' ? 'Legacy response' : 'Modern response' }}
                <span class="font-normal text-slate-500">{{ w === 'legacy' ? '(what your app expects)' : '(what the API returns now)' }}</span>
              </label>
              <div class="flex gap-2">
                <button type="button" class="btn" (click)="format(w)">Format</button>
                <label class="btn">Upload<input type="file" accept=".json,application/json" class="sr-only" (change)="onFile(w, $event)"></label>
              </div>
            </div>
            <textarea [id]="w" rows="14" spellcheck="false" class="field code" style="resize:vertical"
              [value]="text(w)" (input)="setText(w, $any($event.target).value)"></textarea>
            @if (parsed(w).error && text(w).trim()) {
              <p class="mt-1 text-xs text-rose-400" role="alert">Invalid: {{ parsed(w).error }}</p>
            } @else if (parsed(w).value) {
              <p class="mt-1 text-xs text-emerald-400">Valid JSON object</p>
            }
          </div>
        }
      </div>
      <div class="mt-4 flex items-center gap-3">
        <button type="button" class="btn btn-primary" [disabled]="!canCompare()" (click)="runCompare()">Compare schemas</button>
        @if (busy(); as b) { <span class="text-sm text-slate-400" aria-live="polite">{{ b }}…</span> }
      </div>
      @if (error(); as e) { <p class="mt-3 rounded-lg border border-rose-500/30 bg-rose-500/10 p-3 text-sm text-rose-200" role="alert">{{ e }}</p> }
    </section>

    <!-- 2. Differences -->
    @if (compare(); as cmp) {
      <section class="card mt-6 p-4" aria-labelledby="s2">
        <h2 id="s2" class="mb-1 font-semibold"><span class="mr-2 text-violet-400">2</span>What changed</h2>
        <p class="mb-3 text-sm text-slate-400">
          {{ cmp.unchanged }} field(s) unchanged. Solid badges are confirmed differences. Dashed badges are <em>inferred</em> from matching values and names, not confirmed.
        </p>
        <div class="overflow-x-auto">
          <table class="w-full">
            <thead><tr><th>Change</th><th>Legacy field</th><th>Modern field</th><th>Impact</th><th>Note</th></tr></thead>
            <tbody>
              @for (c of cmp.changes; track $index) {
                <tr>
                  <td><span class="badge" [class]="kindClass(c)">{{ kindLabel(c.kind) }}</span></td>
                  <td class="code">{{ c.legacyPath ?? '-' }} @if (c.legacyType) { <span class="text-slate-500">({{ c.legacyType }})</span> }</td>
                  <td class="code">{{ c.modernPath ?? '-' }} @if (c.modernType) { <span class="text-slate-500">({{ c.modernType }})</span> }</td>
                  <td>@if (c.breaking) { <span class="text-rose-300">Breaking</span> } @else { <span class="text-slate-400">Safe</span> }</td>
                  <td class="text-slate-400">{{ c.note }}</td>
                </tr>
              }
            </tbody>
          </table>
        </div>
        <div class="mt-4 flex flex-wrap items-center gap-3 border-t border-slate-800 pt-4">
          <label for="mode" class="text-sm text-slate-300">Suggestion engine</label>
          <select id="mode" class="field" style="width:auto" (change)="mode.set($any($event.target).value)">
            <option value="hybrid">Heuristic + Qwen3 (hybrid)</option>
            <option value="heuristic">Heuristic only (no AI)</option>
          </select>
          <button type="button" class="btn btn-primary" [disabled]="!!busy()" (click)="runSuggest()">Generate suggestions</button>
        </div>
      </section>
    }

    <!-- 3. Mapping review -->
    @if (rows().length > 0) {
      <section class="card mt-6 p-4" aria-labelledby="s3">
        <div class="mb-1 flex flex-wrap items-center justify-between gap-2">
          <h2 id="s3" class="font-semibold"><span class="mr-2 text-violet-400">3</span>Review mappings</h2>
          <div class="flex gap-2">
            <button type="button" class="btn" (click)="approveHigh()">Approve all ≥ 70%</button>
            <button type="button" class="btn" (click)="rejectAll()">Reject all</button>
          </div>
        </div>
        @if (ai(); as a) {
          <p class="mb-3 text-sm" [class]="a.used ? 'text-emerald-300' : 'text-amber-300'" aria-live="polite">
            @if (a.used) { Qwen3 suggestions used ({{ a.model }} via {{ a.provider }}). AI confidence is capped and always needs your review. }
            @else if (a.requested) { {{ a.message }} }
            @else { Heuristic engine only. No AI was used. }
          </p>
        }
        <div class="overflow-x-auto">
          <table class="w-full">
            <thead><tr><th>Approve</th><th>Legacy field</th><th>Modern source</th><th>Transform</th><th>Confidence</th><th>Source</th><th>Why</th></tr></thead>
            <tbody>
              @for (r of rows(); track r.legacyPath; let i = $index) {
                <tr>
                  <td>
                    <input type="checkbox" class="h-4 w-4 accent-violet-500" [checked]="r.approved" [disabled]="!r.modernPath"
                      [attr.aria-label]="'Approve mapping for ' + r.legacyPath" (change)="setApproved(i, $any($event.target).checked)">
                  </td>
                  <td class="code">{{ r.legacyPath }}</td>
                  <td>
                    <select class="field code" [attr.aria-label]="'Modern source for ' + r.legacyPath" (change)="setPath(i, $any($event.target).value)">
                      <option value="" [selected]="!r.modernPath">- no mapping -</option>
                      @for (p of modernPaths(); track p) { <option [value]="p" [selected]="p === r.modernPath">{{ p }}</option> }
                    </select>
                  </td>
                  <td>
                    <select class="field" [attr.aria-label]="'Transform for ' + r.legacyPath" (change)="setTransform(i, $any($event.target).value)">
                      @for (t of transforms(); track t) { <option [value]="t" [selected]="t === r.transform">{{ t }}</option> }
                    </select>
                  </td>
                  <td>
                    @if (r.modernPath) {
                      <div class="flex items-center gap-2">
                        <div class="h-1.5 w-14 rounded bg-slate-800"><div class="h-1.5 rounded" [class]="barColor(r.confidence)" [style.width.%]="r.confidence * 100"></div></div>
                        <span class="text-xs text-slate-400">{{ pct(r.confidence) }}</span>
                      </div>
                    } @else { <span class="text-slate-500">-</span> }
                  </td>
                  <td>
                    <span class="badge" [class]="sourceClass(r.source)">{{ r.source || 'manual' }}</span>
                    @if (r.needsReview && r.modernPath) { <span class="ml-1 text-xs text-amber-300">review</span> }
                  </td>
                  <td class="text-slate-400">
                    {{ r.reason }}
                    @if (r.aiAlternativePath) {
                      <div class="mt-1 text-xs text-amber-300">Qwen3 disagreed and suggested <span class="code">{{ r.aiAlternativePath }}</span>. Not applied.</div>
                    }
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
        <div class="mt-4 flex items-center gap-3 border-t border-slate-800 pt-4">
          <button type="button" class="btn btn-primary" [disabled]="!!busy()" (click)="runVerify()">Verify approved mappings</button>
          <span class="text-sm text-slate-400">{{ approvedCount() }} approved</span>
        </div>
      </section>
    }

    <!-- 4. Verification -->
    @if (verification(); as v) {
      <section class="card mt-6 p-4" aria-labelledby="s4">
        <h2 id="s4" class="mb-3 font-semibold"><span class="mr-2 text-violet-400">4</span>Verification</h2>
        <div class="mb-4 grid grid-cols-2 gap-3 md:grid-cols-4">
          <div class="rounded-lg bg-slate-900 p-3"><div class="text-xs text-slate-400">Coverage</div><div class="text-2xl font-bold">{{ pct(v.coverage) }}</div></div>
          <div class="rounded-lg bg-slate-900 p-3"><div class="text-xs text-slate-400">Passed</div><div class="text-2xl font-bold text-emerald-400">{{ v.passed }}</div></div>
          <div class="rounded-lg bg-slate-900 p-3"><div class="text-xs text-slate-400">Failed</div><div class="text-2xl font-bold" [class]="v.failed ? 'text-rose-400' : ''">{{ v.failed }}</div></div>
          <div class="rounded-lg bg-slate-900 p-3"><div class="text-xs text-slate-400">Unmapped</div><div class="text-2xl font-bold text-amber-300">{{ v.unmapped }}</div></div>
        </div>
        <p class="mb-3 text-sm" [class]="v.safe ? 'text-emerald-300' : 'text-rose-300'" role="status">
          {{ v.safe ? 'No blocking problems. Code can be generated.' : 'Blocking problems found. Fix or reject the failing mappings before generating code.' }}
        </p>
        @if (v.warnings.length) {
          <ul class="mb-4 space-y-1 text-sm">
            @for (w of v.warnings; track $index) {
              <li class="rounded-md border px-3 py-1.5" [class]="warnClass(w.severity)">
                <span class="font-semibold uppercase">{{ w.severity }}</span>
                @if (w.legacyPath) { <span class="code"> · {{ w.legacyPath }}</span> } · {{ w.message }}
              </li>
            }
          </ul>
        }
        <div class="overflow-x-auto">
          <table class="w-full">
            <thead><tr><th>Field</th><th>Result</th><th>Detail</th></tr></thead>
            <tbody>
              @for (c of v.checks; track c.legacyPath) {
                <tr>
                  <td class="code">{{ c.legacyPath }}</td>
                  <td><span class="badge" [class]="checkClass(c.status)">{{ c.status }}</span></td>
                  <td class="text-slate-400">{{ c.detail }}</td>
                </tr>
              }
            </tbody>
          </table>
        </div>
        <div class="mt-4 border-t border-slate-800 pt-4">
          <button type="button" class="btn btn-primary" [disabled]="!!busy() || !v.safe" (click)="runGenerate()">Generate adapter</button>
        </div>
      </section>
    }

    <!-- 5. Artifacts -->
    @if (artifacts(); as g) {
      <section class="card mt-6 p-4" aria-labelledby="s5">
        <div class="mb-3 flex flex-wrap items-center justify-between gap-2">
          <h2 id="s5" class="font-semibold"><span class="mr-2 text-violet-400">5</span>Generated files</h2>
          <button type="button" class="btn" (click)="downloadZip()">Download all (.zip)</button>
        </div>
        <div class="mb-2 flex flex-wrap gap-2" role="tablist">
          @for (t of tabs; track t.id) {
            <button type="button" role="tab" class="btn" [attr.aria-selected]="tab() === t.id" [class]="tab() === t.id ? 'btn-primary' : ''" (click)="tab.set(t.id)">{{ t.file }}</button>
          }
        </div>
        <div class="mb-2 flex gap-2">
          <button type="button" class="btn" (click)="copy(current(g))">{{ copied() ? 'Copied' : 'Copy' }}</button>
          <button type="button" class="btn" (click)="saveFile(currentName(), current(g))">Download file</button>
        </div>
        <pre class="code max-h-96 overflow-auto rounded-lg border border-slate-800 bg-slate-950 p-3"><code>{{ current(g) }}</code></pre>
        <p class="mt-3 text-xs text-slate-500">Review the generated adapter before using it. It reflects only the mappings you approved and verified on these samples.</p>
      </section>
    }
  `,
})
export class Workspace implements OnInit {
  private api = inject(Api);

  readonly samples = SAMPLES;
  readonly whichList: Which[] = ['legacy', 'modern'];
  readonly tabs = [
    { id: 'adapter' as const, file: 'CompatibilityAdapter.cs' },
    { id: 'mapping' as const, file: 'mapping.json' },
    { id: 'adapted' as const, file: 'adapted.json' },
  ];

  config = signal<AppConfig | null>(null);
  legacyText = signal('');
  modernText = signal('');
  sampleId = signal('default');
  busy = signal<string | null>(null);
  error = signal<string | null>(null);
  compare = signal<CompareResponse | null>(null);
  mode = signal('hybrid');
  ai = signal<AiStatus | null>(null);
  rows = signal<MappingRow[]>([]);
  verification = signal<ValidateResponse | null>(null);
  artifacts = signal<GenerateResponse | null>(null);
  tab = signal<'adapter' | 'mapping' | 'adapted'>('adapter');
  copied = signal(false);

  private legacyParsed = computed(() => parseObj(this.legacyText()));
  private modernParsed = computed(() => parseObj(this.modernText()));
  canCompare = computed(() => !!this.legacyParsed().value && !!this.modernParsed().value && !this.busy());
  sampleDescription = computed(() => this.samples.find((s) => s.id === this.sampleId())?.description ?? '');
  modernPaths = computed(() => (this.compare()?.modernFields ?? []).map((f) => f.path));
  transforms = computed(() => this.config()?.transforms ?? ['none', 'toString', 'toInt', 'toDouble', 'toBool']);
  approvedCount = computed(() => this.rows().filter((r) => r.approved && r.modernPath).length);

  ngOnInit(): void {
    this.loadSample('default');
    this.api.config().then((c) => this.config.set(c)).catch(() => this.config.set(null));
  }

  text(w: Which): string { return w === 'legacy' ? this.legacyText() : this.modernText(); }
  parsed(w: Which) { return w === 'legacy' ? this.legacyParsed() : this.modernParsed(); }
  setText(w: Which, v: string): void {
    (w === 'legacy' ? this.legacyText : this.modernText).set(v);
    this.resetResults();
  }

  private resetResults(): void {
    this.compare.set(null); this.rows.set([]); this.ai.set(null);
    this.verification.set(null); this.artifacts.set(null); this.error.set(null);
  }

  loadSample(id: string): void {
    const s = this.samples.find((x) => x.id === id);
    if (!s) return;
    this.sampleId.set(id);
    this.legacyText.set(JSON.stringify(s.legacy, null, 2));
    this.modernText.set(JSON.stringify(s.modern, null, 2));
    this.resetResults();
  }

  clearAll(): void { this.legacyText.set(''); this.modernText.set(''); this.sampleId.set(''); this.resetResults(); }

  format(w: Which): void {
    const p = this.parsed(w);
    if (p.value) this.setText(w, JSON.stringify(p.value, null, 2));
  }

  onFile(w: Which, ev: Event): void {
    const input = ev.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;
    if (file.size > 900_000) { this.error.set('File is too large (limit about 900 KB).'); return; }
    file.text().then((t) => { this.setText(w, t); this.format(w); });
    input.value = '';
  }

  private async run<T>(label: string, work: () => Promise<T>): Promise<T | null> {
    this.busy.set(label); this.error.set(null);
    try { return await work(); }
    catch (e) { this.error.set(errMsg(e)); return null; }
    finally { this.busy.set(null); }
  }

  async runCompare(): Promise<void> {
    const l = this.legacyParsed().value, m = this.modernParsed().value;
    if (!l || !m) return;
    this.rows.set([]); this.verification.set(null); this.artifacts.set(null);
    const r = await this.run('Comparing', () => this.api.compare(l, m));
    if (r) this.compare.set(r);
  }

  async runSuggest(): Promise<void> {
    const l = this.legacyParsed().value, m = this.modernParsed().value;
    if (!l || !m) return;
    this.verification.set(null); this.artifacts.set(null);
    const r = await this.run(this.mode() === 'hybrid' ? 'Asking Qwen3 and the heuristic engine' : 'Matching fields', () => this.api.suggest(l, m, this.mode()));
    if (!r) return;
    this.ai.set(r.ai);
    this.rows.set(r.suggestions.map((s) => ({ ...s, approved: !!s.modernPath && !s.needsReview })));
  }

  private patch(i: number, p: Partial<MappingRow>): void {
    this.rows.update((rs) => rs.map((r, j) => (j === i ? { ...r, ...p } : r)));
    this.verification.set(null); this.artifacts.set(null);
  }
  setApproved(i: number, v: boolean): void { this.patch(i, { approved: v }); }
  setTransform(i: number, t: string): void { this.patch(i, { transform: t, source: 'manual' }); }
  setPath(i: number, path: string): void {
    const row = this.rows()[i];
    if (!path) { this.patch(i, { modernPath: null, approved: false, source: 'manual', transform: 'none', confidence: 0, reason: 'No mapping chosen.' }); return; }
    const cmp = this.compare();
    const lt = cmp?.legacyFields.find((f) => f.path === row.legacyPath)?.type ?? 'null';
    const mt = cmp?.modernFields.find((f) => f.path === path)?.type ?? 'null';
    this.patch(i, { modernPath: path, source: 'manual', transform: this.infer(lt, mt), confidence: 1, needsReview: false, reason: 'Chosen manually.', aiAlternativePath: null });
  }
  private infer(lt: string, mt: string): string {
    if (lt === mt || lt === 'null') return 'none';
    return lt === 'string' ? 'toString' : lt === 'number' ? 'toDouble' : lt === 'boolean' ? 'toBool' : 'none';
  }
  approveHigh(): void { this.rows.update((rs) => rs.map((r) => ({ ...r, approved: !!r.modernPath && r.confidence >= 0.7 }))); this.verification.set(null); this.artifacts.set(null); }
  rejectAll(): void { this.rows.update((rs) => rs.map((r) => ({ ...r, approved: false }))); this.verification.set(null); this.artifacts.set(null); }

  private payload(): MappingPayload[] {
    return this.rows().filter((r) => r.approved && r.modernPath).map((r) => ({
      legacyPath: r.legacyPath, modernPath: r.modernPath, transform: r.transform,
      confidence: r.confidence, source: r.source, reason: r.reason,
    }));
  }

  async runVerify(): Promise<void> {
    const l = this.legacyParsed().value, m = this.modernParsed().value;
    if (!l || !m) return;
    this.artifacts.set(null);
    const r = await this.run('Verifying', () => this.api.validate(l, m, this.payload()));
    if (r) this.verification.set(r);
  }

  async runGenerate(): Promise<void> {
    const l = this.legacyParsed().value, m = this.modernParsed().value;
    if (!l || !m) return;
    this.busy.set('Generating'); this.error.set(null);
    try { this.artifacts.set(await this.api.generate(l, m, this.payload())); }
    catch (e) {
      if (e instanceof HttpErrorResponse && e.status === 422 && e.error?.validation) this.verification.set(e.error.validation as ValidateResponse);
      this.error.set(errMsg(e));
    } finally { this.busy.set(null); }
  }

  async downloadZip(): Promise<void> {
    const l = this.legacyParsed().value, m = this.modernParsed().value;
    if (!l || !m) return;
    const blob = await this.run('Preparing zip', () => this.api.downloadZip(l, m, this.payload()));
    if (blob) this.saveBlob('schemaghost-output.zip', blob);
  }

  current(g: GenerateResponse): string { return this.tab() === 'adapter' ? g.adapterCs : this.tab() === 'mapping' ? g.mappingJson : g.adaptedJson; }
  currentName(): string { return this.tabs.find((t) => t.id === this.tab())?.file ?? 'file.txt'; }
  saveFile(name: string, content: string): void { this.saveBlob(name, new Blob([content], { type: 'text/plain;charset=utf-8' })); }
  private saveBlob(name: string, blob: Blob): void {
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url; a.download = name; a.click();
    URL.revokeObjectURL(url);
  }
  copy(text: string): void {
    navigator.clipboard.writeText(text).then(() => { this.copied.set(true); setTimeout(() => this.copied.set(false), 1500); });
  }

  pct(x: number): string { return Math.round(x * 100) + '%'; }
  barColor(c: number): string { return c >= 0.8 ? 'bg-emerald-500' : c >= 0.5 ? 'bg-amber-400' : 'bg-rose-500'; }
  kindLabel(k: string): string {
    return ({ removed: 'Removed', added: 'Added', typeChanged: 'Type changed', renamedGuess: 'Possible rename (inferred)' } as Record<string, string>)[k] ?? k;
  }
  kindClass(c: Change): string {
    if (c.inferred) return 'border-amber-400/60 border-dashed text-amber-300 bg-amber-400/5';
    return c.kind === 'removed' ? 'border-rose-500/40 text-rose-300 bg-rose-500/10'
      : c.kind === 'added' ? 'border-emerald-500/40 text-emerald-300 bg-emerald-500/10'
      : 'border-sky-500/40 text-sky-300 bg-sky-500/10';
  }
  sourceClass(s: string): string {
    return s === 'qwen3' ? 'border-violet-500/40 text-violet-300 bg-violet-500/10'
      : s === 'heuristic' ? 'border-slate-600 text-slate-300 bg-slate-800' : 'border-sky-500/40 text-sky-300 bg-sky-500/10';
  }
  warnClass(s: string): string {
    return s === 'error' ? 'border-rose-500/40 bg-rose-500/10 text-rose-200'
      : s === 'warning' ? 'border-amber-500/40 bg-amber-500/10 text-amber-200' : 'border-slate-700 bg-slate-900 text-slate-300';
  }
  checkClass(s: string): string {
    return s === 'pass' ? 'border-emerald-500/40 text-emerald-300 bg-emerald-500/10'
      : s === 'fail' ? 'border-rose-500/40 text-rose-300 bg-rose-500/10' : 'border-slate-600 text-slate-400 bg-slate-800';
  }
}
