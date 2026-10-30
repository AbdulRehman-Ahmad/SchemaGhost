import { Component, signal } from '@angular/core';
import { Landing } from './landing';
import { Workspace } from './workspace';

@Component({
  selector: 'app-root',
  imports: [Landing, Workspace],
  template: `
    <header class="sticky top-0 z-10 border-b border-slate-800 bg-slate-950/85 backdrop-blur">
      <div class="mx-auto flex max-w-6xl items-center justify-between px-4 py-3">
        <button type="button" class="flex items-center gap-2 font-semibold" (click)="view.set('landing')" aria-label="SchemaGhost home">
          <span class="grid h-7 w-7 place-items-center rounded-md bg-violet-600 text-sm">S</span>
          <span>SchemaGhost</span>
        </button>
        <nav class="flex items-center gap-2" aria-label="Main">
          <a class="btn" href="https://github.com/AbdulRehman-Ahmad/SchemaGhost" target="_blank" rel="noopener">GitHub</a>
          <button type="button" class="btn btn-primary" (click)="view.set('workspace')">Open workspace</button>
        </nav>
      </div>
    </header>
    <main class="mx-auto max-w-6xl px-4 py-8">
      @if (view() === 'landing') {
        <app-landing (start)="view.set('workspace')" />
      } @else {
        <app-workspace />
      }
    </main>
    <footer class="mx-auto max-w-6xl px-4 pb-10 pt-4 text-xs text-slate-500">
      Open source. Built for the DEV Hacktoberfest 2026 challenge, "Ghost in the Machine". Suggestions are aids for review, not guarantees.
    </footer>
  `,
})
export class App {
  view = signal<'landing' | 'workspace'>('landing');
}
