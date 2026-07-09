// ============================================================
// types.ts — Types and date resolution logic for the Gantt module
// ============================================================

import type { CardSummary, ColumnStatus, Priority, UserSummary } from '../kanban-board/types';

export type { CardSummary, ColumnStatus, Priority, UserSummary };

/** The three Gantt display modes */
export type GanttMode = 'default' | 'planned' | 'actual';

/** A resolved card item ready to render on the Gantt timeline */
export interface GanttItem {
  cardId: number;
  title: string;
  columnId: number;
  columnName: string;
  columnStatus: ColumnStatus;
  assignee?: UserSummary;
  priority: Priority;
  start: Date;
  end: Date;
}

/** A card that could not be resolved for the current mode */
export interface UnresolvableCard {
  cardId: number;
  title: string;
  columnName: string;
  columnStatus: ColumnStatus;
  assignee?: UserSummary;
  priority: Priority;
  reason: string; // human-readable explanation
}

/**
 * Parse a date string — accepts "yyyy-MM-dd" or ISO datetime "yyyy-MM-ddTHH:mmK".
 * Returns null if parsing fails.
 */
export function parseDate(value: string | undefined | null): Date | null {
  if (!value) return null;
  const d = new Date(value);
  return Number.isNaN(d.getTime()) ? null : d;
}

/**
 * Returns [start, end] dates for a card under the given mode, or null if
 * the required dates are missing/invalid.
 */
export function resolveItemDates(
  card: CardSummary,
  mode: GanttMode,
): [Date, Date] | null {
  switch (mode) {
    case 'planned': {
      const s = parseDate(card.plannedStartDate);
      const e = parseDate(card.dueDate);
      if (!s || !e) return null;
      // allow same-day tasks (end = start when e <= s)
      return [s, e < s ? s : e];
    }
    case 'actual': {
      const s = parseDate(card.actualStartDate);
      const e = parseDate(card.actualEndDate);
      if (!s || !e) return null;
      return [s, e < s ? s : e];
    }
    default: {
      // Smart fallback: prefer actual (both must exist), then planned
      const as = parseDate(card.actualStartDate);
      const ae = parseDate(card.actualEndDate);
      if (as && ae) return [as, ae < as ? as : ae];

      const ps = parseDate(card.plannedStartDate);
      const pe = parseDate(card.dueDate);
      if (ps && pe) return [ps, pe < ps ? ps : pe];

      return null;
    }
  }
}

/** All user-visible strings needed by the Gantt module, supplied by the host page from loc-data */
export interface GanttStrings {
  cards: string;
  noDatesCards: string;
  noBoardCards: string;
  noDateCardsForMode: string;
  timeLabel: string;
  columnLabel: string;
  statusLabel: string;
  assigneeLabel: string;
  priorityLabel: string;
  statusCompleted: string;
  statusInProgress: string;
  statusNotStarted: string;
  missingPlannedBoth: string;
  missingPlannedStart: string;
  missingDueDate: string;
  missingActualBoth: string;
  missingActualStart: string;
  missingActualEnd: string;
  missingDateFallback: string;
}

/** Human-readable explanation for why a card is not shown */
export function missingReason(card: CardSummary, mode: GanttMode, s: GanttStrings): string {
  switch (mode) {
    case 'planned':
      if (!card.plannedStartDate && !card.dueDate) return s.missingPlannedBoth;
      if (!card.plannedStartDate) return s.missingPlannedStart;
      return s.missingDueDate;
    case 'actual':
      if (!card.actualStartDate && !card.actualEndDate) return s.missingActualBoth;
      if (!card.actualStartDate) return s.missingActualStart;
      return s.missingActualEnd;
    default:
      return s.missingDateFallback;
  }
}

export const PRIORITY_COLORS: Record<Priority, string> = {
  Urgent: '#ef4444',
  High: '#f97316',
  Medium: '#eab308',
  Low: '#3b82f6',
  None: '#9ca3af',
};
