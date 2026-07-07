// ============================================================
// column-editor.ts — Inline column editing (rename, delete, status)
// ============================================================

import type { KanbanCallbacks } from './types';

/**
 * Initialize inline column editing via event delegation on the container.
 * Handles: column rename (click title → input → blur/Enter),
 *          column status change (select → callback),
 *          column delete (click button → confirm → callback)
 */
export function initColumnEditor(
  container: HTMLElement,
  callbacks: KanbanCallbacks,
): void {
  // ---- Column rename ----
  container.addEventListener('click', e => {
    if (!callbacks.onColumnRenamed) return;

    const btn = (e.target as HTMLElement).closest<HTMLElement>('.btn-edit-column-title');
    if (!btn) return;

    const columnEl = btn.closest<HTMLElement>('.kanban-column');
    if (!columnEl) return;

    const columnId = parseInt(btn.getAttribute('data-column-id') ?? '0', 10);
    if (!columnId) return;

    const titleSpan = columnEl.querySelector<HTMLElement>('.column-title');
    if (!titleSpan || titleSpan.querySelector('input')) return; // already editing

    const currentName = titleSpan.textContent?.trim() ?? '';

    // Replace span with input
    const input = document.createElement('input');
    input.type = 'text';
    input.className = 'column-title-input';
    input.value = currentName;
    input.style.width = '140px';

    const finish = async () => {
      const newName = input.value.trim();
      if (newName && newName !== currentName) {
        try {
          await callbacks.onColumnRenamed!(columnId, newName);
          titleSpan.textContent = newName;
        } catch (err) {
          console.error('Column rename failed:', err);
        }
      }
      // Restore span
      if (input.parentNode) {
        input.replaceWith(titleSpan);
      }
    };

    input.addEventListener('keydown', e => {
      if (e.key === 'Enter') {
        e.preventDefault();
        finish();
      } else if (e.key === 'Escape') {
        input.value = currentName;
        input.replaceWith(titleSpan);
      }
    });

    input.addEventListener('blur', () => {
      setTimeout(() => {
        if (input.parentNode) {
          input.replaceWith(titleSpan);
        }
      }, 150);
    });

    titleSpan.replaceWith(input);
    input.focus();
    input.select();
  });

  // ---- Column status change ----
  container.addEventListener('change', e => {
    if (!callbacks.onColumnStatusChanged) return;

    const select = (e.target as HTMLElement).closest<HTMLElement>('.column-status-select');
    if (!select) return;

    const columnEl = select.closest<HTMLElement>('.kanban-column');
    if (!columnEl) return;

    const columnId = parseInt((select as HTMLSelectElement).getAttribute('data-column-id') ?? '0', 10);
    const newStatus = (select as HTMLSelectElement).value;

    if (columnId) {
      callbacks.onColumnStatusChanged(columnId, newStatus).catch(err => {
        console.error('Column status change failed:', err);
      });
    }
  });

  // ---- Column delete ----
  container.addEventListener('click', e => {
    if (!callbacks.onColumnDeleted) return;

    const btn = (e.target as HTMLElement).closest<HTMLElement>('.btn-delete-column.can-delete');
    if (!btn) return;

    const columnEl = btn.closest<HTMLElement>('.kanban-column');
    if (!columnEl) return;

    const columnId = parseInt(btn.getAttribute('data-column-id') ?? '0', 10);
    if (!columnId) return;

    callbacks.onColumnDeleted(columnId).catch(err => {
      console.error('Column delete failed:', err);
    });
  });
}
