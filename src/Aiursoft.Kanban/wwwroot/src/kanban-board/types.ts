// ============================================================
// types.ts — All TypeScript interfaces for the KanbanBoard module
// ============================================================

/** Priority levels matching the C# CardPriority enum */
export type Priority = 'Urgent' | 'High' | 'Medium' | 'Low' | 'None';

/** Column status matching the C# ColumnStatus enum */
export type ColumnStatus = 'NotStarted' | 'InProgress' | 'Completed';

/** Recurrence unit matching the C# RecurrenceUnit enum */
export type RecurrenceUnit = 'Days' | 'Weeks' | 'Months' | 'Years';

/** A label attached to a card */
export interface CardLabel {
  id: number;
  name: string;
  color: string;
}

/** Summary of a user (assignee or creator) */
export interface UserSummary {
  userId: string;
  displayName: string;
  avatarUrl?: string;
}

/** Summary data for a single card on the board */
export interface CardSummary {
  id: number;
  title: string;
  priority: Priority;
  dueDate?: string;         // ISO 8601 date string
  isOverdue: boolean;
  plannedStartDate?: string;
  actualStartDate?: string;
  actualEndDate?: string;
  assignee?: UserSummary;
  creator?: UserSummary;
  creationTime: string;     // ISO 8601 datetime
  labels: CardLabel[];
  commentCount: number;
  isRecurring: boolean;
  recurrenceInterval?: number;
  recurrenceUnit?: number;
  description?: string;     // plain text summary for card preview
}

/** Data for a single column on the board */
export interface ColumnData {
  id: number;
  name: string;
  color: string;
  dotClass: string;         // CSS class for the colored dot (dot-blue, dot-orange, etc.)
  status: ColumnStatus;
  order: number;
  cards: CardSummary[];
}

/** Full board data passed to the module */
export interface BoardData {
  id: number;
  name: string;
  canEdit: boolean;
  columns: ColumnData[];
}

/** Active filter state (all in module closure, never on DOM) */
export interface FilterState {
  searchText: string;
  priorities: Priority[];
  assigneeIds: string[];
}

/** Callbacks the module invokes — host page handles all side effects */
export interface KanbanCallbacks {
  /** Card clicked → navigate to /Cards/{id} */
  onCardClicked?: (cardId: number) => void;

  /** Card dragged to new position → POST /Kanban/MoveCard */
  onCardMoved?: (cardId: number, targetColumnId: number, newOrder: number) => Promise<void>;

  /** Column dragged to new position → POST /Kanban/MoveColumn */
  onColumnReordered?: (columnId: number, newOrder: number) => Promise<void>;

  /** Quick-create: user types title in column header input + Enter */
  onCardCreatedQuick?: (columnId: number, title: string) => Promise<CardSummary | null>;

  /** Column title renamed inline */
  onColumnRenamed?: (columnId: number, newName: string) => Promise<void>;

  /** Column status dropdown changed */
  onColumnStatusChanged?: (columnId: number, newStatus: string) => Promise<void>;

  /** Column deleted (with confirmation already handled by module) */
  onColumnDeleted?: (columnId: number) => Promise<void>;

  /** "Add Column" button clicked */
  onCreateColumn?: () => void;
}

/** Options passed to KanbanBoard() */
export interface KanbanBoardOptions {
  /** Container element or selector */
  container: HTMLElement | string;

  /** Full board data as JSON */
  data: BoardData;

  /** Callbacks for side effects */
  callbacks: KanbanCallbacks;

  /** Optional: card ID to highlight and scroll to on initial render */
  highlightCardId?: number;
}

/** Instance returned by KanbanBoard() */
export interface KanbanBoardInstance {
  /** Refresh the board with new data (keeps Sortable instances) */
  refresh(data: BoardData): void;

  /** Destroy the board, removing all event listeners */
  destroy(): void;
}

/** Internal drag event data from SortableJS */
export interface DragEventData {
  cardId: number;
  fromColumnId: number;
  toColumnId: number;
  newOrder: number;
}

/** Priority value mapping (matches data-priority attribute values) */
export const PRIORITY_VALUES: Record<number, Priority> = {
  0: 'Urgent',
  1: 'High',
  2: 'Medium',
  3: 'Low',
  4: 'None',
};

export const PRIORITY_ORDER: Priority[] = ['Urgent', 'High', 'Medium', 'Low', 'None'];

/** Column status mapping (matches data-column-status attribute values) */
export const COLUMN_STATUS_VALUES: Record<number, ColumnStatus> = {
  0: 'NotStarted',
  1: 'InProgress',
  2: 'Completed',
};

/** Standard dot color classes for columns */
export const DOT_COLORS = [
  'dot-blue',
  'dot-orange',
  'dot-green',
  'dot-purple',
  'dot-pink',
  'dot-teal',
  'dot-amber',
  'dot-indigo',
];
