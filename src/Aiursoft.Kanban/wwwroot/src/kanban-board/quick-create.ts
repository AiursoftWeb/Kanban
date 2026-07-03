// ============================================================
// quick-create.ts — Quick card creation from column header input
// ============================================================

import type { KanbanCallbacks, CardSummary } from './types';
import { renderCardIntoColumn } from './renderer';

/**
 * Set up quick-create: when user clicks "Add Card" button in a column,
 * show an inline input, type title, press Enter to create via callback.
 *
 * The module replaces each .btn-add-card with an input-less button that
 * the host page already renders.
 */
export function initQuickCreate(
  container: HTMLElement,
  callbacks: KanbanCallbacks,
): void {
  if (!callbacks.onCardCreatedQuick) return;

  // Delegate: handle clicks on .btn-add-card
  container.addEventListener('click', e => {
    const btn = (e.target as HTMLElement).closest<HTMLElement>('.btn-add-card');
    if (!btn) return;

    const columnEl = btn.closest<HTMLElement>('.kanban-column');
    if (!columnEl) return;

    const columnId = parseInt(columnEl.getAttribute('data-column-id') ?? '0', 10);
    if (!columnId) return;

    // Replace button with inline input
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
    'width:100%;padding:8px 12px;border:1px solid var(--bs-border-color);border-radius:8px;' +
    'font-size:0.85rem;margin-top:0.5rem;outline:none;box-sizing:border-box';

  let creating = false;

  const submit = async () => {
    const title = input.value.trim();
    if (!title || creating) return;

    creating = true;
    input.disabled = true;
    input.placeholder = 'Creating…';

    try {
      const result = await callbacks.onCardCreatedQuick!(columnId, title);
      if (result?.cardId) {
        // Create a minimal card summary to append locally
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
        const cardEl = renderCardIntoColumn(columnEl, newCard, true);
        // Flash animation
        cardEl.style.transition = 'box-shadow 0.3s ease';
        cardEl.style.boxShadow = '0 0 0 3px var(--bs-primary)';
        setTimeout(() => {
          cardEl.style.boxShadow = '';
        }, 1500);
      }
    } catch (err) {
      console.error('Quick create failed:', err);
    }

    // Restore button
    if (input.parentNode) {
      input.replaceWith(originalBtn);
    }
    creating = false;
  };

  input.addEventListener('keydown', e => {
    if (e.key === 'Enter') {
      e.preventDefault();
      submit();
    } else if (e.key === 'Escape') {
      input.replaceWith(originalBtn);
    }
  });

  input.addEventListener('blur', () => {
    // Small delay to allow Enter/click to fire first
    setTimeout(() => {
      if (input.parentNode && !creating) {
        input.replaceWith(originalBtn);
      }
    }, 150);
  });

  return input;
}
