import type { CardSummary } from './types';

export interface CardDates {
  source: 'actual' | 'planned';
  start?: string;
  end?: string;
}

export type DateDisplayMode = 'default' | 'planned' | 'actual' | 'all';

export interface CardDateRow extends CardDates {
  source: 'actual' | 'planned';
}

/** Keep each pair together. Complete actual dates win, then complete planned dates. */
export function resolveDefaultCardDates(card: Pick<CardSummary,
  'actualStartDate' | 'actualEndDate' | 'plannedStartDate' | 'dueDate'>): CardDates {
  const actual = { source: 'actual' as const, start: valid(card.actualStartDate), end: valid(card.actualEndDate) };
  const planned = { source: 'planned' as const, start: valid(card.plannedStartDate), end: valid(card.dueDate) };
  if (actual.start && actual.end) return actual;
  if (planned.start && planned.end) return planned;
  return actual.start || actual.end ? actual : planned;
}

export function resolveCardDateRows(
  card: Pick<CardSummary, 'actualStartDate' | 'actualEndDate' | 'plannedStartDate' | 'dueDate'>,
  mode: DateDisplayMode,
): CardDateRow[] {
  const planned = { source: 'planned' as const, start: valid(card.plannedStartDate), end: valid(card.dueDate) };
  const actual = { source: 'actual' as const, start: valid(card.actualStartDate), end: valid(card.actualEndDate) };

  if (mode === 'planned') return [planned];
  if (mode === 'actual') return [actual];
  if (mode === 'all') return [planned, actual];
  return [resolveDefaultCardDates(card)];
}

function valid(value?: string): string | undefined {
  return value && !Number.isNaN(new Date(value).getTime()) ? value : undefined;
}
