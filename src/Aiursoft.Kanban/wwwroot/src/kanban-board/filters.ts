// ============================================================
// filters.ts — Client-side card filtering
// ============================================================

import type { BoardData, ColumnStatus, FilterState, Priority } from './types';
import { COLUMN_STATUS_VALUES, PRIORITY_VALUES } from './types';
import { filterBoardData, matchesCardFilters } from './filter-state';

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
  container: HTMLElement | null,
  data: BoardData,
  onFilterChange?: (state: FilterState) => void,
): FilterInstance {
  const listeners = new AbortController();
  const state: FilterState = {
    searchText: '',
    priorities: [],
    assigneeIds: [],
    statuses: [],
  };

  const searchInput = document.getElementById('kanbanFilterSearch') as HTMLInputElement | null;
  const filterBar = document.getElementById('kanbanFilterBar');
  const filterEmpty = document.getElementById('kanbanFilterEmpty');

  const assigneeGroup = document.getElementById('assigneeFilterGroup');
  function populateAssigneeFilterChips(): void {
    if (!assigneeGroup) return;

    const selected = new Set(state.assigneeIds);
    const assigneeMap = new Map<string, { id: string; name: string }>();

    container?.querySelectorAll<HTMLElement>('.kanban-card').forEach(card => {
      const id = card.getAttribute('data-assigned-user-id') ?? '';
      const name = card.getAttribute('data-assigned-user-name') ?? '';
      const initial = card.getAttribute('data-assigned-user-initial') ?? '';
      if (!id || assigneeMap.has(id)) return;

      assigneeMap.set(id, {
        id,
        name: name || initial || id,
      });
    });

    if (!container) {
      for (const column of data.columns) {
        for (const card of column.cards) {
          if (card.assignee) assigneeMap.set(card.assignee.userId, {
            id: card.assignee.userId, name: card.assignee.displayName,
          });
        }
      }
    }

    assigneeGroup.querySelectorAll('.filter-chip[data-filter-type="assignee"]').forEach(chip => chip.remove());
    assigneeMap.forEach(user => {
      const chip = document.createElement('button');
      chip.type = 'button';
      chip.className = 'filter-chip';
      chip.setAttribute('data-filter-type', 'assignee');
      chip.setAttribute('data-filter-value', user.id);
      chip.textContent = user.name;
      chip.classList.toggle('active', selected.has(user.id));
      assigneeGroup.appendChild(chip);
    });

    state.assigneeIds = state.assigneeIds.filter(id => assigneeMap.has(id));
  }
  populateAssigneeFilterChips();

  // ---- Filter logic ----
  function cardMatches(cardEl: HTMLElement): boolean {
    const title = (cardEl.getAttribute('data-title') ?? '').toLowerCase();
    const description = (cardEl.getAttribute('data-description') ?? '').toLowerCase();
    const priorityStr = cardEl.getAttribute('data-priority') ?? '4';
    const priority: Priority = PRIORITY_VALUES[parseInt(priorityStr, 10)] ?? 'None';
    const assigneeId = cardEl.getAttribute('data-assigned-user-id') ?? '';

    const statusValue = cardEl.closest<HTMLElement>('.kanban-column')?.dataset.columnStatus;
    const status = statusValue === undefined ? undefined : COLUMN_STATUS_VALUES[Number(statusValue)];
    if (state.statuses.length && (!status || !state.statuses.includes(status))) return false;

    return matchesCardFilters({
      title, description, priority,
      assignee: assigneeId ? { userId: assigneeId, displayName: '' } : undefined,
    }, state);
  }

  function apply(): void {
    const hasFilters = !!(state.searchText || state.priorities.length || state.assigneeIds.length || state.statuses.length);
    document.getElementById('filterClearAll')?.classList.toggle('hidden', !hasFilters);
    filterBar?.querySelectorAll<HTMLElement>('[data-filter-type]').forEach(chip => {
      const value = chip.dataset.filterValue ?? '';
      const active = chip.dataset.filterType === 'priority'
        ? state.priorities.includes(PRIORITY_VALUES[Number(value)])
        : chip.dataset.filterType === 'status'
          ? state.statuses.includes(value as ColumnStatus)
          : state.assigneeIds.includes(value);
      chip.classList.toggle('active', active);
      chip.setAttribute('aria-pressed', String(active));
    });
    if (!container) {
      onFilterChange?.(state);
      return;
    }
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

      // Also count cards with no inline display:none
      const allColCards = cardsContainer.querySelectorAll<HTMLElement>('.kanban-card');
      let actualVisible = 0;
      allColCards.forEach(c => {
        if (c.style.display !== 'none') actualVisible++;
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

    onFilterChange?.(state);
  }

  // ---- Event bindings ----
  if (searchInput) {
    searchInput.addEventListener('input', () => {
      state.searchText = searchInput.value.trim();
      apply();
    }, { signal: listeners.signal });
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
        state.statuses = [];
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
      } else if (filterType === 'status' && filterValue
        && Object.values(COLUMN_STATUS_VALUES).includes(filterValue as ColumnStatus)) {
        const status = filterValue as ColumnStatus;
        const idx = state.statuses.indexOf(status);
        if (idx >= 0) state.statuses.splice(idx, 1);
        else state.statuses.push(status);
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

      apply();
    }, { signal: listeners.signal });
  }

  const handleExternalApply = () => {
    populateAssigneeFilterChips();
    apply();
  };
  document.addEventListener('kanban:filters-apply', handleExternalApply as EventListener);

  return {
    getState: () => ({ ...state, priorities: [...state.priorities], assigneeIds: [...state.assigneeIds], statuses: [...state.statuses] }),
    setState(newState: Partial<FilterState>) {
      if (newState.searchText !== undefined) {
        state.searchText = newState.searchText;
        if (searchInput) searchInput.value = newState.searchText;
      }
      if (newState.priorities !== undefined) state.priorities = [...newState.priorities];
      if (newState.assigneeIds !== undefined) state.assigneeIds = [...newState.assigneeIds];
      if (newState.statuses !== undefined) state.statuses = [...newState.statuses];
      apply();
    },
    apply,
    destroy() {
      listeners.abort();
      document.removeEventListener('kanban:filters-apply', handleExternalApply as EventListener);
    },
  };
}

function getFilterEmptyText(): string {
  const el = document.querySelector('#loc-data span[data-key="no-cards-match"]');
  return el?.textContent?.trim() ?? 'No cards match the current filters.';
}

/** The same controls and matching rules, applied before rendering a data-driven view. */
export function initDataFilters(data: BoardData, render: (filtered: BoardData) => void): FilterInstance {
  return initFilters(null, data, state => render(filterBoardData(data, state)));
}
