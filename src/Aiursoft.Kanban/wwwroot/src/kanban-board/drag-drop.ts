// ============================================================
// drag-drop.ts — SortableJS integration for cards and columns
// ============================================================

import Sortable from 'sortablejs';
import type { KanbanCallbacks } from './types';

export interface DragDropInstances {
  columnSortable: Sortable | null;
  cardSortables: Sortable[];
  destroy(): void;
}

/**
 * Initialize SortableJS for column reordering and card dragging within and across columns.
 *
 * @param container  The .kanban-container element
 * @param callbacks  Host-provided callbacks
 * @returns Object with destroy() to clean up
 */
export function initDragDrop(
  container: HTMLElement,
  callbacks: KanbanCallbacks,
): DragDropInstances {
  const cardSortables: Sortable[] = [];

  // ---- Column reordering ----
  const columnSortable = callbacks.onColumnReordered
    ? new Sortable(container, {
        animation: 200,
        easing: 'cubic-bezier(0.25, 0.46, 0.45, 0.94)',
        handle: '.column-header',
        draggable: '.kanban-column',
        ghostClass: 'sortable-ghost',
        chosenClass: 'sortable-chosen',
        dragClass: 'sortable-drag',
        onEnd(evt) {
          const columnEl = evt.item as HTMLElement;
          const columnId = parseInt(columnEl.getAttribute('data-column-id') ?? '0', 10);
          const newOrder = evt.newIndex ?? 0;
          if (columnId && callbacks.onColumnReordered) {
            callbacks.onColumnReordered(columnId, newOrder).catch(err => {
              console.error('onColumnReordered failed:', err);
            });
          }
        },
      })
    : null;

  // ---- Card sorting within each column ----
  const cardColumns = container.querySelectorAll<HTMLElement>('.column-cards');
  cardColumns.forEach(cardsEl => {
    if (!callbacks.onCardMoved) return;

    const columnId = parseInt(cardsEl.getAttribute('data-column-id') ?? '0', 10);
    const sortable = new Sortable(cardsEl, {
      group: 'kanban-cards',
      animation: 200,
      easing: 'cubic-bezier(0.25, 0.46, 0.45, 0.94)',
      draggable: '.kanban-card.can-drag',
      ghostClass: 'sortable-ghost',
      chosenClass: 'sortable-chosen',
      onEnd(evt) {
        const cardEl = evt.item as HTMLElement;
        const cardId = parseInt(cardEl.getAttribute('data-card-id') ?? '0', 10);
        const toColumnEl = evt.to.closest<HTMLElement>('.kanban-column');
        const toColumnId = parseInt(toColumnEl?.getAttribute('data-column-id') ?? '0', 10);
        const newOrder = evt.newIndex ?? 0;

        if (cardId && toColumnId && callbacks.onCardMoved) {
          callbacks.onCardMoved(cardId, toColumnId, newOrder).then(() => {
            // Update column counts after successful move
            updateAllColumnCounts(container);
          }).catch(err => {
            console.error('onCardMoved failed:', err);
          });
        }
      },
      // Highlight target column on drag over
      onMove(evt) {
        const toColumnEl = evt.to.closest<HTMLElement>('.kanban-column');
        // Remove highlight from all columns
        container.querySelectorAll('.kanban-column.drag-over').forEach(el => el.classList.remove('drag-over'));
        if (toColumnEl) {
          toColumnEl.classList.add('drag-over');
        }
      },
      onStart() {
        // Remove all drag-over highlights when drag starts
        container.querySelectorAll('.kanban-column.drag-over').forEach(el => el.classList.remove('drag-over'));
      },
    });
    cardSortables.push(sortable);
  });

  return {
    columnSortable,
    cardSortables,
    destroy() {
      if (columnSortable) columnSortable.destroy();
      cardSortables.forEach(s => s.destroy());
    },
  };
}

/**
 * Re-count cards in each column and update .column-count badges.
 */
function updateAllColumnCounts(container: HTMLElement): void {
  const columns = container.querySelectorAll<HTMLElement>('.kanban-column');
  columns.forEach(col => {
    const count = col.querySelectorAll('.kanban-card').length;
    const countEl = col.querySelector<HTMLElement>('.column-count');
    if (countEl) {
      countEl.textContent = String(count);
    }
    // Remove empty placeholder or add it back
    const cardsContainer = col.querySelector<HTMLElement>('.column-cards');
    if (cardsContainer) {
      const placeholder = cardsContainer.querySelector('.column-empty-placeholder');
      if (count === 0 && !placeholder) {
        const empty = document.createElement('div');
        empty.className = 'column-empty-placeholder';
        empty.textContent = getDropPlaceholderText();
        cardsContainer.appendChild(empty);
      } else if (count > 0 && placeholder) {
        placeholder.remove();
      }
    }
  });
}

function getDropPlaceholderText(): string {
  const el = document.querySelector('#loc-data span[data-key="drop-cards-here"]');
  return el?.textContent?.trim() ?? 'Drop cards here';
}
