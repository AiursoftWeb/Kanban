// ============================================================
// quick-create.ts — Quick card creation from column header input
// ============================================================

import type { KanbanCallbacks, CardSummary } from './types';
import { renderCardIntoColumn } from './renderer';
import { t } from './i18n';

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
  input.placeholder = t('what-needs-to-be-done', 'What needs to be done?');

  let creating = false;

  async function doSubmit() {
    const title = input.value.trim();
    if (!title || creating) return;

    creating = true;
    input.disabled = true;

    try {
      const card = await callbacks.onCardCreatedQuick!(columnId, title);
      if (card && typeof card.id === 'number' && card.id > 0) {
        renderCardIntoColumn(columnEl, card, true);
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
