// ============================================================
// quick-create.ts — Quick card creation from column header input
// ============================================================

import type { KanbanCallbacks, CardSummary } from './types';
import { renderCardIntoColumn } from './renderer';

export function initQuickCreate(
  container: HTMLElement,
  callbacks: KanbanCallbacks,
): void {
  if (!callbacks.onCardCreatedQuick) return;

  container.addEventListener('click', e => {
    const btn = (e.target as HTMLElement).closest<HTMLElement>('.btn-add-card');
    if (!btn) return;

    const columnEl = btn.closest<HTMLElement>('.kanban-column');
    if (!columnEl) return;

    const columnId = parseInt(columnEl.getAttribute('data-column-id') ?? '0', 10);
    if (!columnId) return;

    const input = createQuickCreateInput(columnId, columnEl, btn, callbacks);
    btn.replaceWith(input);
    input.focus();
  });
}

function createQuickCreateInput(
  columnId: number,
  columnEl: HTMLElement,
  originalBtn: HTMLElement,
  callbacks: KanbanCallbacks,
): HTMLInputElement {
  const input = document.createElement('input');
  input.type = 'text';
  input.className = 'quick-create-input';
  input.placeholder = 'Type title + Enter…';
  input.style.cssText =
    'width:100%;padding:8px 12px;border:1px solid var(--bs-primary);border-radius:8px;' +
    'font-size:0.85rem;margin-top:0.5rem;outline:none;box-sizing:border-box';

  let creating = false;

  async function doSubmit() {
    const title = input.value.trim();
    if (!title || creating) return;

    creating = true;
    input.disabled = true;

    try {
      const result = await callbacks.onCardCreatedQuick!(columnId, title);
      if (result && typeof result.cardId === 'number' && result.cardId > 0) {
        const newCard: CardSummary = {
          id: result.cardId,
          title,
          priority: 'None',
          isOverdue: false,
          labels: [],
          commentCount: 0,
          isRecurring: false,
          creationTime: new Date().toISOString(),
        };
        renderCardIntoColumn(columnEl, newCard, true);
      }
    } catch (err) {
      console.error('Quick create failed:', err);
    } finally {
      if (input.parentNode) {
        input.replaceWith(originalBtn);
      }
      creating = false;
    }
  }

  input.addEventListener('keydown', e => {
    if (e.key === 'Enter') {
      e.preventDefault();
      doSubmit();
    } else if (e.key === 'Escape') {
      input.replaceWith(originalBtn);
    }
  });

  input.addEventListener('blur', () => {
    setTimeout(() => {
      if (input.parentNode && !creating) {
        input.replaceWith(originalBtn);
      }
    }, 200);
  });

  return input;
}
