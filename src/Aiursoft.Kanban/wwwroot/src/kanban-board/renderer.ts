// ============================================================
// renderer.ts — Pure functions: BoardData JSON → DOM elements
// No event binding, no network calls, no DOM state reading outside
// of the card element currently being rendered.
// ============================================================

import type {
  BoardData,
  CardLabel,
  CardSummary,
  ColumnData,
  Priority,
  UserSummary,
} from './types';
import { PRIORITY_VALUES } from './types';
import { t } from './i18n';

/**
 * Parse an ISO 8601 date or datetime string as UTC.
 * Returns a Date in local time representing that UTC moment.
 */
function parseUtcDate(value: string): Date | null {
  if (!value) return null;
  // Append Z if no timezone designator, so JS treats it as UTC.
  const normalized = /[Zz]$/.test(value) || /[+-]\d{2}:?\d{2}$/.test(value)
    ? value
    : value + 'Z';
  const d = new Date(normalized);
  return Number.isNaN(d.getTime()) ? null : d;
}

function formatDate(isoDate?: string): string {
  if (!isoDate) return '';

  // Date-only values (10 chars): extract directly — timezone-independent.
  const dateMatch = isoDate.match(/^(\d{4})-(\d{2})-(\d{2})$/);
  if (dateMatch) {
    return `${dateMatch[2]}/${dateMatch[3]}`;
  }

  // Datetime values: parse as UTC, then format as local date.
  const parsed = parseUtcDate(isoDate);
  if (!parsed) return isoDate;

  return `${String(parsed.getMonth() + 1).padStart(2, '0')}/${String(parsed.getDate()).padStart(2, '0')}`;
}

function isOverdue(dueDate?: string): boolean {
  if (!dueDate) return false;

  const now = new Date();
  now.setHours(0, 0, 0, 0);

  // Parse the due date as UTC.  A date-only string like "2025-06-15"
  // is midnight UTC; for overdue checks across timezones we strip the
  // time so that the comparison is date-based rather than datetime-based.
  const due = parseUtcDate(dueDate);
  if (!due) return false;
  due.setHours(0, 0, 0, 0);
  return due < now;
}

function escapeAttribute(value: string): string {
  return value.replace(/"/g, '&quot;');
}

function getPriorityClass(priority: Priority): string | null {
  switch (priority) {
    case 'Urgent':
      return 'priority-urgent';
    case 'High':
      return 'priority-high';
    case 'Medium':
      return 'priority-medium';
    case 'Low':
      return 'priority-low';
    default:
      return null;
  }
}

function getPriorityLabel(priority: Priority): string {
  switch (priority) {
    case 'Urgent':
      return t('urgent', 'Urgent');
    case 'High':
      return t('high', 'High');
    case 'Medium':
      return t('medium', 'Medium');
    case 'Low':
      return t('low', 'Low');
    default:
      return t('none', 'None');
  }
}

function getRecurrenceShortLabel(interval?: number, unit?: number): string | null {
  if (!interval || !unit) return null;

  const suffixMap: Record<number, string> = {
    1: 'd',
    2: 'w',
    3: 'm',
    4: 'y',
  };

  const suffix = suffixMap[unit];
  if (!suffix) return null;

  return `${interval}${suffix}`;
}

function buildDescriptionPreview(description?: string): string {
  if (!description) return '';

  return description
    .replace(/```[\s\S]*?```/g, ' ')
    .replace(/`([^`]+)`/g, '$1')
    .replace(/!\[[^\]]*]\([^)]+\)/g, ' ')
    .replace(/\[[^\]]+]\([^)]+\)/g, '$1')
    .replace(/[#>*_~\-]/g, ' ')
    .replace(/\r?\n+/g, ' ')
    .replace(/\s+/g, ' ')
    .trim();
}

function parseCardLabels(raw: string | null): CardLabel[] {
  if (!raw) return [];

  try {
    const parsed = JSON.parse(raw) as CardLabel[];
    return Array.isArray(parsed) ? parsed : [];
  } catch {
    return [];
  }
}

function setOptionalDataAttribute(cardEl: HTMLElement, name: string, value?: string | number | null): void {
  if (value === undefined || value === null || value === '') {
    cardEl.removeAttribute(`data-${name}`);
    return;
  }

  cardEl.setAttribute(`data-${name}`, String(value));
}

function setUserDataAttributes(cardEl: HTMLElement, prefix: 'assigned-user' | 'creator-user', user?: UserSummary): void {
  setOptionalDataAttribute(cardEl, `${prefix}-id`, user?.userId);
  setOptionalDataAttribute(cardEl, `${prefix}-name`, user?.displayName);
  setOptionalDataAttribute(cardEl, `${prefix}-avatar-url`, user?.avatarUrl);
  setOptionalDataAttribute(cardEl, `${prefix}-initial`, user?.displayName?.trim().charAt(0).toUpperCase() ?? '');
}

function readCardSummaryFromElement(cardEl: HTMLElement): CardSummary {
  const priorityIndex = parseInt(cardEl.getAttribute('data-priority') ?? '4', 10);
  const labels = parseCardLabels(cardEl.getAttribute('data-labels'));
  const columnStatus = cardEl.closest<HTMLElement>('.kanban-column')?.getAttribute('data-column-status') ?? '';

  const assignedUserId = cardEl.getAttribute('data-assigned-user-id') ?? '';
  const assignedUserName = cardEl.getAttribute('data-assigned-user-name') ?? '';
  const assignedUserAvatarUrl = cardEl.getAttribute('data-assigned-user-avatar-url') ?? '';

  const creatorUserId = cardEl.getAttribute('data-creator-user-id') ?? '';
  const creatorUserName = cardEl.getAttribute('data-creator-user-name') ?? '';
  const creatorUserAvatarUrl = cardEl.getAttribute('data-creator-user-avatar-url') ?? '';

  const recurrenceInterval = parseInt(cardEl.getAttribute('data-recurrence-interval') ?? '', 10);
  const recurrenceUnit = parseInt(cardEl.getAttribute('data-recurrence-unit') ?? '', 10);
  const commentCount = parseInt(cardEl.getAttribute('data-comment-count') ?? '0', 10);
  const dueDate = cardEl.getAttribute('data-due-date') ?? undefined;

  return {
    id: parseInt(cardEl.getAttribute('data-card-id') ?? '0', 10),
    title: cardEl.getAttribute('data-title') ?? '',
    priority: PRIORITY_VALUES[priorityIndex] ?? 'None',
    dueDate,
    isOverdue: columnStatus !== '2' && ((cardEl.getAttribute('data-is-overdue') ?? 'false') === 'true' || isOverdue(dueDate)),
    plannedStartDate: cardEl.getAttribute('data-planned-start') ?? undefined,
    actualStartDate: cardEl.getAttribute('data-actual-start') ?? undefined,
    actualEndDate: cardEl.getAttribute('data-actual-end') ?? undefined,
    assignee: assignedUserId
      ? {
          userId: assignedUserId,
          displayName: assignedUserName,
          avatarUrl: assignedUserAvatarUrl || undefined,
        }
      : undefined,
    creator: creatorUserId
      ? {
          userId: creatorUserId,
          displayName: creatorUserName,
          avatarUrl: creatorUserAvatarUrl || undefined,
        }
      : undefined,
    creationTime: cardEl.getAttribute('data-creation-time') ?? undefined,
    labels,
    commentCount: Number.isNaN(commentCount) ? 0 : commentCount,
    isRecurring: !Number.isNaN(recurrenceInterval) && recurrenceInterval > 0 && !Number.isNaN(recurrenceUnit) && recurrenceUnit > 0,
    recurrenceInterval: Number.isNaN(recurrenceInterval) ? undefined : recurrenceInterval,
    recurrenceUnit: Number.isNaN(recurrenceUnit) ? undefined : recurrenceUnit,
    description: cardEl.getAttribute('data-description') ?? undefined,
  };
}

function renderAvatar(user?: UserSummary): HTMLElement | null {
  if (!user) return null;

  const avatar = document.createElement('span');
  avatar.className = 'card-assignee-avatar';
  avatar.title = user.displayName;

  if (user.avatarUrl) {
    const img = document.createElement('img');
    img.className = 'card-assignee-avatar-image';
    img.src = user.avatarUrl;
    img.alt = user.displayName;
    avatar.appendChild(img);
  } else {
    avatar.textContent = user.displayName.trim().charAt(0).toUpperCase() || '?';
  }

  return avatar;
}

function renderPriorityBadge(priority: Priority): HTMLElement | null {
  if (priority === 'None') return null;

  const className = getPriorityClass(priority);
  if (!className) return null;

  const badge = document.createElement('span');
  badge.className = `priority-badge ${className}`;
  badge.textContent = getPriorityLabel(priority);
  return badge;
}

function renderRecurrenceBadge(interval?: number, unit?: number): HTMLElement | null {
  const label = getRecurrenceShortLabel(interval, unit);
  if (!label) return null;

  const badge = document.createElement('span');
  badge.className = 'recurrence-badge';
  badge.title = t('recurring', 'Recurring');
  badge.textContent = label;
  return badge;
}

function renderLabels(labels: CardLabel[]): HTMLElement | null {
  if (labels.length === 0) return null;

  const row = document.createElement('div');
  row.className = 'card-labels';

  labels.forEach(label => {
    const chip = document.createElement('span');
    chip.className = 'card-label-chip';
    chip.setAttribute(
      'style',
      `background-color:${escapeAttribute(label.color)}22;border-color:${escapeAttribute(label.color)};color:${escapeAttribute(label.color)};`,
    );
    chip.textContent = label.name;
    row.appendChild(chip);
  });

  return row;
}

function renderBottomRow(card: CardSummary): HTMLElement | null {
  const hasDueDate = !!card.dueDate;
  const hasComments = card.commentCount > 0;
  if (!hasDueDate && !hasComments) return null;

  const row = document.createElement('div');
  row.className = 'card-footer-row';

  if (hasDueDate) {
    const due = document.createElement('div');
    due.className = `card-due-date${card.isOverdue ? ' overdue' : ''}`;
    due.textContent = formatDate(card.dueDate);
    row.appendChild(due);
  }

  if (hasComments) {
    const comments = document.createElement('span');
    comments.className = 'card-comment-count';
    comments.textContent = `${card.commentCount} ${t('comments', 'Comments')}`;
    row.appendChild(comments);
  }

  return row;
}

export function syncCardElementData(cardEl: HTMLElement, card: CardSummary, canDrag: boolean): void {
  cardEl.className = `kanban-card${canDrag ? ' can-drag' : ''}`;
  cardEl.setAttribute('data-card-id', String(card.id));
  setOptionalDataAttribute(cardEl, 'title', card.title);
  setOptionalDataAttribute(cardEl, 'description', card.description);
  setOptionalDataAttribute(
    cardEl,
    'priority',
    Object.entries(PRIORITY_VALUES).find(([, value]) => value === card.priority)?.[0] ?? '4',
  );
  setOptionalDataAttribute(cardEl, 'due-date', card.dueDate);
  setOptionalDataAttribute(cardEl, 'planned-start', card.plannedStartDate);
  setOptionalDataAttribute(cardEl, 'actual-start', card.actualStartDate);
  setOptionalDataAttribute(cardEl, 'actual-end', card.actualEndDate);
  setOptionalDataAttribute(cardEl, 'creation-time', card.creationTime);
  setOptionalDataAttribute(cardEl, 'is-overdue', card.isOverdue ? 'true' : 'false');
  setOptionalDataAttribute(cardEl, 'recurrence-interval', card.recurrenceInterval);
  setOptionalDataAttribute(cardEl, 'recurrence-unit', card.recurrenceUnit);
  setOptionalDataAttribute(cardEl, 'comment-count', card.commentCount);
  setOptionalDataAttribute(cardEl, 'labels', JSON.stringify(card.labels));

  setUserDataAttributes(cardEl, 'assigned-user', card.assignee);
  setUserDataAttributes(cardEl, 'creator-user', card.creator);
}

export function rerenderCardElement(cardEl: HTMLElement): void {
  const card = readCardSummaryFromElement(cardEl);
  const fragment = document.createDocumentFragment();

  const topRow = document.createElement('div');
  topRow.className = 'card-top-row';

  const topLeft = document.createElement('div');
  topLeft.className = 'd-flex flex-wrap align-items-center gap-2';

  const priorityBadge = renderPriorityBadge(card.priority);
  if (priorityBadge) {
    topLeft.appendChild(priorityBadge);
  }

  const recurrenceBadge = renderRecurrenceBadge(card.recurrenceInterval, card.recurrenceUnit);
  if (recurrenceBadge) {
    topLeft.appendChild(recurrenceBadge);
  }

  const topRight = document.createElement('div');
  topRight.className = 'd-flex align-items-center gap-2';

  const avatar = renderAvatar(card.assignee);
  if (avatar) {
    topRight.appendChild(avatar);
  }

  if (topLeft.childElementCount > 0 || topRight.childElementCount > 0) {
    topRow.appendChild(topLeft);
    topRow.appendChild(topRight);
    fragment.appendChild(topRow);
  }

  const title = document.createElement('div');
  title.className = 'card-title-text';
  title.textContent = card.title;
  fragment.appendChild(title);

  const descriptionPreview = buildDescriptionPreview(card.description);
  if (descriptionPreview) {
    const description = document.createElement('div');
    description.className = 'card-description';
    description.textContent = descriptionPreview.length > 180 ? `${descriptionPreview.slice(0, 177)}...` : descriptionPreview;
    fragment.appendChild(description);
  }

  const labels = renderLabels(card.labels);
  if (labels) {
    fragment.appendChild(labels);
  }

  const bottomRow = renderBottomRow(card);
  if (bottomRow) {
    fragment.appendChild(bottomRow);
  }

  cardEl.replaceChildren(fragment);
}

// ---- Card rendering ----

/**
 * Render a single card element from CardSummary data.
 */
export function renderCard(card: CardSummary, canDrag: boolean): HTMLElement {
  const cardEl = document.createElement('div');
  syncCardElementData(cardEl, card, canDrag);
  rerenderCardElement(cardEl);
  return cardEl;
}

// ---- Column rendering ----

/**
 * Render a single column element from ColumnData.
 */
export function renderColumn(column: ColumnData, canEdit: boolean): HTMLElement {
  const colEl = document.createElement('div');
  colEl.className = 'kanban-column';
  colEl.setAttribute('data-column-id', String(column.id));
  colEl.setAttribute('data-column-status', String(column.status === 'NotStarted' ? 0 : column.status === 'InProgress' ? 1 : 2));

  const header = document.createElement('div');
  header.className = canEdit ? 'column-header' : 'column-header column-header-readonly';

  const headerLeft = document.createElement('div');
  headerLeft.className = 'column-header-left';

  const dot = document.createElement('span');
  dot.className = `column-dot ${column.dotClass}`;
  headerLeft.appendChild(dot);

  const title = document.createElement('span');
  title.className = 'column-title';
  title.setAttribute('data-column-id', String(column.id));
  title.textContent = column.name;
  headerLeft.appendChild(title);

  const count = document.createElement('span');
  count.className = 'column-count';
  count.textContent = String(column.cards.length);
  headerLeft.appendChild(count);

  if (canEdit) {
    const editBtn = document.createElement('button');
    editBtn.className = 'btn-edit-column-title';
    editBtn.setAttribute('data-column-id', String(column.id));
    editBtn.title = t('rename-column', 'Rename column');
    editBtn.innerHTML = '<i class="align-middle" style="width:12px;height:12px" data-lucide="pencil"></i>';
    headerLeft.appendChild(editBtn);
  }

  header.appendChild(headerLeft);

  const headerRight = document.createElement('div');
  headerRight.className = 'd-flex align-items-center gap-1';

  const statusSelect = document.createElement('select');
  statusSelect.className = 'column-status-select';
  statusSelect.setAttribute('data-column-id', String(column.id));
  if (!canEdit) statusSelect.disabled = true;

  const statusOptions = [
    { value: '0', key: 'not-started', label: 'Not Started' },
    { value: '1', key: 'in-progress', label: 'In Progress' },
    { value: '2', key: 'completed', label: 'Completed' },
  ];
  const statusValue = column.status === 'NotStarted' ? '0' : column.status === 'InProgress' ? '1' : '2';
  statusOptions.forEach(opt => {
    const option = document.createElement('option');
    option.value = opt.value;
    option.textContent = t(opt.key, opt.label);
    if (opt.value === statusValue) option.selected = true;
    statusSelect.appendChild(option);
  });
  headerRight.appendChild(statusSelect);

  const deleteBtn = document.createElement('button');
  deleteBtn.className = `btn-delete-column${canEdit ? ' can-delete' : ''}`;
  deleteBtn.setAttribute('data-column-id', String(column.id));
  deleteBtn.setAttribute('data-cards-count', String(column.cards.length));
  deleteBtn.setAttribute('data-column-name', column.name);
  deleteBtn.title = t('delete-column', 'Delete column');
  if (!canEdit) deleteBtn.disabled = true;
  deleteBtn.innerHTML = '<i class="align-middle" style="width:14px;height:14px" data-lucide="trash-2"></i>';
  headerRight.appendChild(deleteBtn);

  header.appendChild(headerRight);
  colEl.appendChild(header);

  const cardsContainer = document.createElement('div');
  cardsContainer.className = 'column-cards';
  cardsContainer.setAttribute('data-column-id', String(column.id));

  if (column.cards.length === 0) {
    const empty = document.createElement('div');
    empty.className = 'column-empty-placeholder';
    empty.textContent = t('drop-cards-here', 'Drop cards here');
    cardsContainer.appendChild(empty);
  } else {
    column.cards.forEach(card => {
      cardsContainer.appendChild(renderCard(card, canEdit));
    });
  }

  colEl.appendChild(cardsContainer);

  if (canEdit) {
    const addBtn = document.createElement('button');
    addBtn.className = 'btn-add-card';
    addBtn.setAttribute('data-column-id', String(column.id));
    addBtn.innerHTML = `<i class="align-middle" style="width:14px;height:14px" data-lucide="plus"></i> ${t('add-card', 'Add Card')}`;
    colEl.appendChild(addBtn);
  }

  return colEl;
}

// ---- Board rendering ----

/**
 * Render the full board (all columns) into a container.
 */
export function renderBoard(container: HTMLElement, data: BoardData): void {
  container.innerHTML = '';

  const dotColors = ['dot-blue', 'dot-orange', 'dot-green', 'dot-purple', 'dot-pink', 'dot-teal', 'dot-amber', 'dot-indigo'];

  data.columns.forEach((column, index) => {
    const columnWithDot = {
      ...column,
      dotClass: column.dotClass || dotColors[index % dotColors.length],
    };
    container.appendChild(renderColumn(columnWithDot, data.canEdit));
  });
}

/**
 * Render a single card element and append it to a column's card container.
 */
export function renderCardIntoColumn(columnEl: HTMLElement, card: CardSummary, canDrag: boolean): HTMLElement {
  const cardsContainer = columnEl.querySelector<HTMLElement>('.column-cards');
  if (!cardsContainer) throw new Error('Column has no .column-cards container');

  const placeholder = cardsContainer.querySelector('.column-empty-placeholder');
  if (placeholder) placeholder.remove();

  const cardEl = renderCard(card, canDrag);
  cardsContainer.appendChild(cardEl);

  const countEl = columnEl.querySelector<HTMLElement>('.column-count');
  if (countEl) {
    countEl.textContent = String(cardsContainer.querySelectorAll('.kanban-card').length);
  }

  return cardEl;
}
