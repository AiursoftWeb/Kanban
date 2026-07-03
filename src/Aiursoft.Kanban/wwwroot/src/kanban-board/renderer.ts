// ============================================================
// renderer.ts — Pure functions: BoardData JSON → DOM elements
// No event binding, no network calls, no DOM state reading
// ============================================================

import type { BoardData, CardSummary, ColumnData } from './types';
import { PRIORITY_VALUES } from './types';
import { t } from './i18n';

// ---- Helpers ----

function escapeHtml(text: string): string {
  const div = document.createElement('div');
  div.textContent = text;
  return div.innerHTML;
}

function priorityClass(priority: string): string {
  switch (priority) {
    case 'Urgent': return 'text-danger';
    case 'High': return 'text-warning';
    case 'Medium': return 'text-info';
    case 'Low': return 'text-success';
    default: return 'text-muted';
  }
}

function priorityIcon(priority: string): string {
  switch (priority) {
    case 'Urgent': return '🔴';
    case 'High': return '🟠';
    case 'Medium': return '🟡';
    case 'Low': return '🟢';
    default: return '⚪';
  }
}

function formatDate(isoDate?: string): string {
  if (!isoDate) return '';
  const d = new Date(isoDate);
  return d.toISOString().split('T')[0] ?? isoDate;
}

function isOverdue(dueDate?: string): boolean {
  if (!dueDate) return false;
  const now = new Date();
  now.setHours(0, 0, 0, 0);
  const due = new Date(dueDate);
  due.setHours(0, 0, 0, 0);
  return due < now;
}

// ---- Card rendering ----

/**
 * Render a single card element from CardSummary data.
 * Pure function — no side effects.
 */
export function renderCard(card: CardSummary, canDrag: boolean): HTMLElement {
  const cardEl = document.createElement('div');
  cardEl.className = `kanban-card${canDrag ? ' can-drag' : ''}`;
  cardEl.setAttribute('data-card-id', String(card.id));
  cardEl.setAttribute('data-title', card.title);
  cardEl.setAttribute('data-description', card.description ?? '');
  cardEl.setAttribute('data-priority', String(Object.entries(PRIORITY_VALUES).find(([, v]) => v === card.priority)?.[0] ?? '4'));
  cardEl.setAttribute('data-assigned-user-id', card.assignee?.userId ?? '');
  cardEl.setAttribute('data-assigned-user-name', card.assignee?.displayName ?? '');
  if (card.dueDate) {
    cardEl.setAttribute('data-due-date', card.dueDate);
  }

  // Title
  const titleEl = document.createElement('div');
  titleEl.className = 'card-title-text';
  titleEl.textContent = card.title;
  cardEl.appendChild(titleEl);

  // Labels row
  if (card.labels.length > 0) {
    const labelsRow = document.createElement('div');
    labelsRow.className = 'card-labels';
    labelsRow.style.cssText = 'display:flex;flex-wrap:wrap;gap:4px;margin-bottom:4px';
    card.labels.forEach(label => {
      const badge = document.createElement('span');
      badge.className = 'badge';
      badge.textContent = label.name;
      badge.style.cssText = `background:${label.color};color:#fff;font-size:0.7rem;padding:2px 6px;border-radius:8px`;
      labelsRow.appendChild(badge);
    });
    cardEl.appendChild(labelsRow);
  }

  // Description preview (if present)
  if (card.description && card.description.trim()) {
    const descEl = document.createElement('div');
    descEl.className = 'card-description';
    // Strip markdown/HTML for plain text preview
    const stripped = card.description
      .replace(/[#*`>\[\]!|~]/g, '')
      .replace(/\n/g, ' ')
      .substring(0, 120);
    descEl.textContent = stripped + (card.description.length > 120 ? '…' : '');
    cardEl.appendChild(descEl);
  }

  // Footer row: priority badge, due date, assignee avatar, comment count
  const footer = document.createElement('div');
  footer.className = 'card-footer-info';
  footer.style.cssText = 'display:flex;align-items:center;justify-content:space-between;margin-top:6px;gap:6px';

  const leftGroup = document.createElement('div');
  leftGroup.style.cssText = 'display:flex;align-items:center;gap:6px';

  // Priority indicator
  if (card.priority !== 'None') {
    const prio = document.createElement('span');
    prio.className = priorityClass(card.priority);
    prio.style.cssText = 'font-size:0.75rem;font-weight:600';
    prio.textContent = priorityIcon(card.priority) + ' ' + card.priority;
    leftGroup.appendChild(prio);
  }

  // Due date
  if (card.dueDate) {
    const due = document.createElement('span');
    const overdue = card.isOverdue || isOverdue(card.dueDate);
    due.className = overdue ? 'text-danger' : 'text-muted';
    due.style.cssText = 'font-size:0.75rem';
    due.textContent = (overdue ? '⚠ ' : '📅 ') + formatDate(card.dueDate);
    leftGroup.appendChild(due);
  }

  // Recurring indicator
  if (card.isRecurring) {
    const rec = document.createElement('span');
    rec.className = 'text-muted';
    rec.style.cssText = 'font-size:0.75rem';
    rec.textContent = '🔄';
    rec.title = t('recurring', 'Recurring');
    leftGroup.appendChild(rec);
  }

  footer.appendChild(leftGroup);

  // Right side: assignee avatar + comment count
  const rightGroup = document.createElement('div');
  rightGroup.style.cssText = 'display:flex;align-items:center;gap:6px';

  if (card.commentCount > 0) {
    const comments = document.createElement('span');
    comments.className = 'text-muted';
    comments.style.cssText = 'font-size:0.75rem';
    comments.textContent = `💬 ${card.commentCount}`;
    rightGroup.appendChild(comments);
  }

  if (card.assignee) {
    const avatar = document.createElement('span');
    avatar.className = 'card-assignee-avatar';
    avatar.style.cssText =
      'display:inline-flex;align-items:center;justify-content:center;width:22px;height:22px;border-radius:50%;background:var(--bs-primary);color:#fff;font-size:0.65rem;font-weight:700';
    avatar.textContent = (card.assignee.displayName || '?')[0].toUpperCase();
    if (card.assignee.avatarUrl) {
      const img = document.createElement('img');
      img.src = card.assignee.avatarUrl;
      img.alt = card.assignee.displayName;
      img.style.cssText = 'width:22px;height:22px;border-radius:50%;object-fit:cover';
      avatar.textContent = '';
      avatar.appendChild(img);
    }
    avatar.title = card.assignee.displayName;
    rightGroup.appendChild(avatar);
  }

  footer.appendChild(rightGroup);
  cardEl.appendChild(footer);

  return cardEl;
}

// ---- Column rendering ----

/**
 * Render a single column element from ColumnData.
 * Pure function — no side effects.
 */
export function renderColumn(column: ColumnData, canEdit: boolean): HTMLElement {
  const colEl = document.createElement('div');
  colEl.className = 'kanban-column';
  colEl.setAttribute('data-column-id', String(column.id));
  colEl.setAttribute('data-column-status', String(column.status === 'NotStarted' ? 0 : column.status === 'InProgress' ? 1 : 2));

  // ---- Column header ----
  const header = document.createElement('div');
  header.className = canEdit ? 'column-header' : 'column-header column-header-readonly';

  const headerLeft = document.createElement('div');
  headerLeft.className = 'column-header-left';

  // Color dot
  const dot = document.createElement('span');
  dot.className = `column-dot ${column.dotClass}`;
  headerLeft.appendChild(dot);

  // Title
  const title = document.createElement('span');
  title.className = 'column-title';
  title.setAttribute('data-column-id', String(column.id));
  title.textContent = column.name;
  headerLeft.appendChild(title);

  // Card count
  const count = document.createElement('span');
  count.className = 'column-count';
  count.textContent = String(column.cards.length);
  headerLeft.appendChild(count);

  // Edit button
  if (canEdit) {
    const editBtn = document.createElement('button');
    editBtn.className = 'btn-edit-column-title';
    editBtn.setAttribute('data-column-id', String(column.id));
    editBtn.title = t('rename-column', 'Rename column');
    editBtn.innerHTML = '<i class="align-middle" style="width:12px;height:12px" data-lucide="pencil"></i>';
    headerLeft.appendChild(editBtn);
  }

  header.appendChild(headerLeft);

  // Header right: status select + delete button
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

  // Delete button
  const deleteBtn = document.createElement('button');
  deleteBtn.className = `btn-delete-column${canEdit ? ' can-delete' : ''}`;
  deleteBtn.setAttribute('data-column-id', String(column.id));
  deleteBtn.setAttribute('data-cards-count', String(column.cards.length));
  deleteBtn.title = t('delete-column', 'Delete column');
  if (!canEdit) deleteBtn.disabled = true;
  deleteBtn.innerHTML = '<i class="align-middle" style="width:14px;height:14px" data-lucide="trash-2"></i>';
  headerRight.appendChild(deleteBtn);

  header.appendChild(headerRight);
  colEl.appendChild(header);

  // ---- Cards container ----
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

  // ---- Add card button ----
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
 * Clears existing content first.
 */
export function renderBoard(container: HTMLElement, data: BoardData): void {
  container.innerHTML = '';

  const dotColors = ['dot-blue', 'dot-orange', 'dot-green', 'dot-purple', 'dot-pink', 'dot-teal', 'dot-amber', 'dot-indigo'];

  data.columns.forEach((col, index) => {
    const colWithDot = {
      ...col,
      dotClass: col.dotClass || dotColors[index % dotColors.length],
    };
    container.appendChild(renderColumn(colWithDot, data.canEdit));
  });
}

/**
 * Render a single card element and append it to a column's card container.
 * Used for quick-create (append without full re-render).
 */
export function renderCardIntoColumn(columnEl: HTMLElement, card: CardSummary, canDrag: boolean): HTMLElement {
  const cardsContainer = columnEl.querySelector<HTMLElement>('.column-cards');
  if (!cardsContainer) throw new Error('Column has no .column-cards container');

  // Remove empty placeholder if present
  const placeholder = cardsContainer.querySelector('.column-empty-placeholder');
  if (placeholder) placeholder.remove();

  const cardEl = renderCard(card, canDrag);
  cardsContainer.appendChild(cardEl);

  // Update column count
  const countEl = columnEl.querySelector<HTMLElement>('.column-count');
  if (countEl) {
    const currentCount = cardsContainer.querySelectorAll('.kanban-card').length;
    countEl.textContent = String(currentCount);
  }

  return cardEl;
}
