// ============================================================
// quick-create.ts — Add card button handling
// ============================================================

import type { KanbanCallbacks } from './types';

export function initQuickCreate(
  container: HTMLElement,
  callbacks: KanbanCallbacks,
): void {
  if (!callbacks.onAddCardRequested) return;

  container.addEventListener('click', e => {
    const btn = (e.target as HTMLElement).closest<HTMLElement>('.btn-add-card');
    if (!btn) return;

    const columnEl = btn.closest<HTMLElement>('.kanban-column');
    if (!columnEl) return;

    const columnId = parseInt(columnEl.getAttribute('data-column-id') ?? '0', 10);
    if (!columnId) return;

    callbacks.onAddCardRequested?.(columnId);
  });
}
