// ============================================================
// filters.ts — Client-side card filtering
// ============================================================

import type { BoardData, FilterState, Priority } from './types';
import { PRIORITY_VALUES } from './types';

export interface FilterInstance {
  /** Get current filter state */
  getState(): FilterState;
  /** Programmatically set filter state and apply */
  setState(state: Partial<FilterState>): void;
  /** Re-run filter on current DOM (call after data refresh) */
  apply(): void;
  /** Destroy filter UI */
  destroy(): void;
}

/**
 * Initialize the client-side filter bar.
 * Reads existing filter bar HTML (already rendered by Razor) and wires up events.
 * Filter state is kept in closure, never on DOM.
 */
export function initFilters(
  container: HTMLElement,
  data: BoardData,
  onFilterChange?: () => void,
): FilterInstance {
  const state: FilterState = {
    searchText: '',
    priorities: [],
    assigneeIds: [],
  };

  const searchInput = document.getElementById('kanbanFilterSearch') as HTMLInputElement | null;
  const filterBar = document.getElementById('kanbanFilterBar');
  const filterEmpty = document.getElementById('kanbanFilterEmpty');

  // Collect unique assignees from board data
  const assigneeMap = new Map<string, { id: string; name: string }>();
  data.columns.forEach(col => {
    col.cards.forEach(card => {
      if (card.assignee?.userId && !assigneeMap.has(card.assignee.userId)) {
        assigneeMap.set(card.assignee.userId, {
          id: card.assignee.userId,
          name: card.assignee.displayName,
        });
      }
    });
  });

  // Populate assignee filter chips
  const assigneeGroup = document.getElementById('assigneeFilterGroup');
  if (assigneeGroup && assigneeMap.size > 0) {
    assigneeMap.forEach(user => {
      const chip = document.createElement('span');
      chip.className = 'filter-chip';
      chip.setAttribute('data-filter-type', 'assignee');
      chip.setAttribute('data-filter-value', user.id);
      chip.textContent = user.name;
      assigneeGroup.appendChild(chip);
    });
  }

  // ---- Filter logic ----
  function cardMatches(cardEl: HTMLElement): boolean {
    const title = (cardEl.getAttribute('data-title') ?? '').toLowerCase();
    const description = (cardEl.getAttribute('data-description') ?? '').toLowerCase();
    const priorityStr = cardEl.getAttribute('data-priority') ?? '4';
    const priority: Priority = PRIORITY_VALUES[parseInt(priorityStr, 10)] ?? 'None';
    const assigneeId = cardEl.getAttribute('data-assigned-user-id') ?? '';

    // Search text
    if (state.searchText) {
      const q = state.searchText.toLowerCase();
      if (!title.includes(q) && !description.includes(q)) return false;
    }

    // Priority filter
    if (state.priorities.length > 0 && !state.priorities.includes(priority)) {
      return false;
    }

    // Assignee filter
    if (state.assigneeIds.length > 0 && !state.assigneeIds.includes(assigneeId)) {
      return false;
    }

    return true;
  }

  function apply(): void {
    const allCards = container.querySelectorAll<HTMLElement>('.kanban-card');
    let visibleCount = 0;

    allCards.forEach(card => {
      const match = cardMatches(card);
      if (match) {
        card.style.display = '';
        visibleCount++;
      } else {
        card.style.display = 'none';
      }
    });

    // Show/hide empty state
    if (filterEmpty) {
      filterEmpty.style.display = visibleCount === 0 ? '' : 'none';
    }

    // Update column empty placeholders
    container.querySelectorAll<HTMLElement>('.kanban-column').forEach(col => {
      const cardsContainer = col.querySelector<HTMLElement>('.column-cards');
      if (!cardsContainer) return;

      const visibleCards = cardsContainer.querySelectorAll<HTMLElement>('.kanban-card[style=""]').length;
      // Also count cards with no inline display:none
      const allColCards = cardsContainer.querySelectorAll<HTMLElement>('.kanban-card');
      let actualVisible = 0;
      allColCards.forEach(c => {
        if (c.style.display !== 'none' && c.style.display !== '') actualVisible++;
      });

      const placeholder = cardsContainer.querySelector('.column-empty-placeholder');
      if (actualVisible === 0 && !placeholder) {
        const empty = document.createElement('div');
        empty.className = 'column-empty-placeholder';
        empty.textContent = getFilterEmptyText();
        cardsContainer.appendChild(empty);
      } else if (actualVisible > 0 && placeholder) {
        placeholder.remove();
      }
    });

    onFilterChange?.();
  }

  // ---- Event bindings ----
  if (searchInput) {
    searchInput.addEventListener('input', () => {
      state.searchText = searchInput.value.trim();
      apply();
    });
  }

  // Filter chips via delegation
  if (filterBar) {
    filterBar.addEventListener('click', e => {
      const chip = (e.target as HTMLElement).closest<HTMLElement>('.filter-chip');
      if (!chip) return;

      // Clear all filter
      if (chip.id === 'filterClearAll') {
        state.priorities = [];
        state.assigneeIds = [];
        if (searchInput) {
          searchInput.value = '';
          state.searchText = '';
        }
        // Reset all chip visuals
        filterBar.querySelectorAll('.filter-chip').forEach(c => c.classList.remove('active'));
        apply();
        return;
      }

      const filterType = chip.getAttribute('data-filter-type');
      const filterValue = chip.getAttribute('data-filter-value');

      if (filterType === 'priority' && filterValue) {
        const priority: Priority = PRIORITY_VALUES[parseInt(filterValue, 10)] ?? 'None';
        const idx = state.priorities.indexOf(priority);
        if (idx >= 0) {
          state.priorities.splice(idx, 1);
          chip.classList.remove('active');
        } else {
          state.priorities.push(priority);
          chip.classList.add('active');
        }
      } else if (filterType === 'assignee' && filterValue) {
        const idx = state.assigneeIds.indexOf(filterValue);
        if (idx >= 0) {
          state.assigneeIds.splice(idx, 1);
          chip.classList.remove('active');
        } else {
          state.assigneeIds.push(filterValue);
          chip.classList.add('active');
        }
      }

      // Show/hide clear all button
      const clearBtn = document.getElementById('filterClearAll');
      if (clearBtn) {
        const hasFilters = state.priorities.length > 0 || state.assigneeIds.length > 0 || state.searchText.length > 0;
        clearBtn.classList.toggle('hidden', !hasFilters);
      }

      apply();
    });
  }

  return {
    getState: () => ({ ...state }),
    setState(newState: Partial<FilterState>) {
      if (newState.searchText !== undefined) {
        state.searchText = newState.searchText;
        if (searchInput) searchInput.value = newState.searchText;
      }
      if (newState.priorities !== undefined) state.priorities = [...newState.priorities];
      if (newState.assigneeIds !== undefined) state.assigneeIds = [...newState.assigneeIds];
      apply();
    },
    apply,
    destroy() {
      // Nothing to clean up; event listeners are on DOM elements
    },
  };
}

function getFilterEmptyText(): string {
  const el = document.querySelector('#loc-data span[data-key="no-cards-match"]');
  return el?.textContent?.trim() ?? 'No cards match the current filters.';
}
