import { Component, output } from '@angular/core';

@Component({
  selector: 'app-landing',
  template: `
    <section class="py-10 text-center">
      <p class="mx-auto mb-4 inline-block rounded-full border border-violet-500/40 bg-violet-500/10 px-3 py-1 text-xs text-violet-300">
        Ghost in the Machine · open-source AI · local-first
      </p>
      <h1 class="mx-auto max-w-3xl text-4xl font-bold leading-tight tracking-tight md:text-5xl">
        The API changed. The maintainer is gone. <span class="text-violet-400">Your integration doesn't have to stay broken.</span>
      </h1>
      <p class="mx-auto mt-5 max-w-2xl text-slate-400">
        Paste the JSON your old app expects and the JSON the API returns now. SchemaGhost finds what changed, suggests how fields correspond,
        verifies every mapping, and generates a C# compatibility adapter you can review and ship.
      </p>
      <div class="mt-8 flex justify-center gap-3">
        <button type="button" class="btn btn-primary" style="padding: .7rem 1.4rem; font-size: 1rem" (click)="start.emit()">Try live demo</button>
        <a class="btn" style="padding: .7rem 1.4rem; font-size: 1rem" href="https://github.com/AbdulRehman-Ahmad/SchemaGhost" target="_blank" rel="noopener">View on GitHub</a>
      </div>
    </section>

    <section class="mt-10" aria-labelledby="how">
      <h2 id="how" class="mb-4 text-xl font-semibold">How it works</h2>
      <ol class="grid gap-4 md:grid-cols-4">
        @for (s of steps; track s.title; let i = $index) {
          <li class="card p-4">
            <div class="mb-2 text-sm text-violet-400">Step {{ i + 1 }}</div>
            <div class="font-semibold">{{ s.title }}</div>
            <p class="mt-1 text-sm text-slate-400">{{ s.text }}</p>
          </li>
        }
      </ol>
    </section>

    <section class="mt-12" aria-labelledby="features">
      <h2 id="features" class="mb-4 text-xl font-semibold">Built to be trusted, not just impressive</h2>
      <div class="grid gap-4 md:grid-cols-3">
        @for (f of features; track f.title) {
          <div class="card p-4">
            <div class="font-semibold">{{ f.title }}</div>
            <p class="mt-1 text-sm text-slate-400">{{ f.text }}</p>
          </div>
        }
      </div>
    </section>

    <section class="card mt-12 p-5 text-sm text-slate-400">
      <strong class="text-slate-200">Honest limits.</strong>
      SchemaGhost cannot recover data the new API no longer returns. Arrays are copied as whole values, and AI confidence is not proof of
      correctness, so every AI suggestion needs your review. Benchmark results come from a small hand-made case set and are not production accuracy.
    </section>
  `,
})
export class Landing {
  start = output<void>();

  steps = [
    { title: 'Compare', text: 'Detect removed, added, moved and retyped fields between the old and new response.' },
    { title: 'Suggest', text: 'Deterministic matching first. A Qwen3 model helps only with unresolved fields.' },
    { title: 'Review & verify', text: 'You approve each mapping. Every path, type and conversion is checked.' },
    { title: 'Generate', text: 'Download a reviewable C# adapter, a mapping profile and the adapted JSON.' },
  ];
  features = [
    { title: 'AI proposes, code decides', text: 'Model output is forced into a fixed schema. Invented paths are discarded and no model-written code is ever executed.' },
    { title: 'Unsafe conversions are refused', text: 'Turning "active" into true, or 250.75 into an integer, is flagged and blocks code generation.' },
    { title: 'Works without AI', text: 'The heuristic engine runs on its own. If the AI provider is down, you still get results and a clear message.' },
  ];
}
