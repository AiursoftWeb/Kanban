import type { BoardData, CardSummary, FilterState } from './types';

export type FilterCard = Pick<CardSummary, 'title' | 'description' | 'priority' | 'assignee'>;

export function matchesCardFilters(card: FilterCard, state: FilterState): boolean {
  const query = state.searchText.trim().toLowerCase();
  return (!query || card.title.toLowerCase().includes(query) || (card.description ?? '').toLowerCase().includes(query))
    && (!state.priorities.length || state.priorities.includes(card.priority))
    && (!state.assigneeIds.length || state.assigneeIds.includes(card.assignee?.userId ?? ''));
}

export function filterBoardData(data: BoardData, state: FilterState): BoardData {
  return { ...data, columns: data.columns.map(column => ({
    ...column, cards: column.cards.filter(card => matchesCardFilters(card, state)),
  })) };
}
