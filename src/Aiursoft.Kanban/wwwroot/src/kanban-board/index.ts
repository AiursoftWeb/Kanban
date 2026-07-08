// ============================================================
// index.ts — KanbanBoard() entry point
// Assembles all sub-modules into a working kanban board
// ============================================================

import type {
  KanbanBoardOptions,
  KanbanBoardInstance,
  BoardData,
} from './types';

import { renderBoard } from './renderer';
export { rerenderCardElement, syncCardElementData } from './renderer';
import { initDragDrop } from './drag-drop';
import { initQuickCreate } from './quick-create';
import { initColumnEditor } from './column-editor';
import { initFilters } from './filters';
import { initMobile } from './mobile';
import { scrollToCard } from './scroll-restore';

// Import styles (Vite bundles them into the output)
import './styles/board.css';
import './styles/card.css';
import './styles/drag-drop.css';
import './styles/filters.css';
import './styles/mobile.css';

/**
 * Create and render a Kanban board.
 *
 * Usage (in Index.cshtml):
 *   import { KanbanBoard } from '/dist/kanban-board.js';
 *   const board = KanbanBoard({
 *     container: '#kanban-root',
 *     data: JSON.parse(boardJson),
 *     callbacks: { onCardClicked: id => location.href = `/Cards/${id}`, ... },
 *   });
 */
export function KanbanBoard(options: KanbanBoardOptions): KanbanBoardInstance {
  const container =
    typeof options.container === 'string'
      ? (document.querySelector(options.container) as HTMLElement)
      : options.container;

  if (!container) {
    throw new Error('KanbanBoard: container element not found');
  }

  const { data, callbacks, highlightCardId } = options;

  // ---- State (in closure only) ----
  let currentData = data;
  let dragDropInstances: ReturnType<typeof initDragDrop> | null = null;
  let filterInstance: ReturnType<typeof initFilters> | null = null;
  let mobileInstance: ReturnType<typeof initMobile> | null = null;
  let destroyed = false;

  // ---- Internal: wire up all sub-modules ----
  function setup(): void {
    if (destroyed) return;

    // 1. Render board from JSON
    renderBoard(container, currentData);

    // 2. Initialize drag & drop
    dragDropInstances = initDragDrop(container, callbacks);

    // 3. Initialize add card action
    initQuickCreate(container, callbacks);

    // 4. Initialize column inline editor (rename, delete, status)
    initColumnEditor(container, callbacks);

    // 5. Initialize client-side filters
    filterInstance = initFilters(container, currentData);

    // 6. Initialize mobile column switcher
    mobileInstance = initMobile(container);

    // 7. Card click → navigate to detail page
    if (callbacks.onCardClicked) {
      container.addEventListener('click', e => {
        const cardEl = (e.target as HTMLElement).closest<HTMLElement>('.kanban-card');
        if (!cardEl) return;

        // Don't navigate if we were dragging
        if (cardEl.classList.contains('sortable-chosen')) return;

        const cardId = parseInt(cardEl.getAttribute('data-card-id') ?? '0', 10);
        if (cardId && callbacks.onCardClicked) {
          callbacks.onCardClicked(cardId);
        }
      });
    }

    // 8. Highlight card on return from detail page
    if (highlightCardId) {
      scrollToCard(container, highlightCardId);
    }
  }

  // ---- Public API ----
  function refresh(newData: BoardData): void {
    if (destroyed) return;
    currentData = newData;

    // Destroy old drag-drop instances
    if (dragDropInstances) {
      dragDropInstances.destroy();
      dragDropInstances = null;
    }

    // Re-render and re-wire
    renderBoard(container, currentData);
    dragDropInstances = initDragDrop(container, callbacks);
    initQuickCreate(container, callbacks);
    initColumnEditor(container, callbacks);

    // Re-run filters with new data
    if (filterInstance) {
      filterInstance.destroy();
    }
    filterInstance = initFilters(container, currentData);

    // Re-init mobile
    if (mobileInstance) {
      mobileInstance.destroy();
    }
    mobileInstance = initMobile(container);
  }

  function destroy(): void {
    destroyed = true;

    if (dragDropInstances) {
      dragDropInstances.destroy();
      dragDropInstances = null;
    }

    if (filterInstance) {
      filterInstance.destroy();
      filterInstance = null;
    }

    if (mobileInstance) {
      mobileInstance.destroy();
      mobileInstance = null;
    }

    container.innerHTML = '';
  }

  // ---- Initial render ----
  setup();

  return { refresh, destroy };
}

// Re-export types for consumers
export type {
  KanbanBoardOptions,
  KanbanBoardInstance,
  BoardData,
  ColumnData,
  CardSummary,
  CardLabel,
  UserSummary,
  KanbanCallbacks,
  FilterState,
} from './types';
