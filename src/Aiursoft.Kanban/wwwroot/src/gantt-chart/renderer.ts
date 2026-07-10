// ============================================================
// renderer.ts — Pure HTML/CSS Gantt chart renderer
// ============================================================

import type { BoardData } from '../kanban-board/types';
import type { GanttMode, GanttItem, UnresolvableCard, GanttStrings } from './types';
import { resolveItemDates, missingReason, PRIORITY_COLORS } from './types';

const DAY_PX = 28; // pixels per day column

// ---- Public render entry ----

export function renderGantt(
  container: HTMLElement,
  data: BoardData,
  mode: GanttMode,
  s: GanttStrings,
): void {
  container.innerHTML = '';

  const boardId = data.id;
  const { items, unresolvable } = collectItems(data, mode, s);

  if (items.length === 0 && unresolvable.length === 0) {
    container.appendChild(buildEmptyState(s.noBoardCards));
    return;
  }

  if (items.length === 0) {
    container.appendChild(buildEmptyState(s.noDateCardsForMode));
    // still show the no-dates section below
    const wrapper = document.createElement('div');
    wrapper.className = 'gantt-wrapper';
    wrapper.appendChild(buildNoDatesSection(unresolvable, s));
    container.appendChild(wrapper);
    return;
  }

  // Compute timeline bounds (add 2-day padding on each side)
  const minDate = new Date(Math.min(...items.map(i => i.start.getTime())));
  const maxDate = new Date(Math.max(...items.map(i => i.end.getTime())));
  minDate.setDate(minDate.getDate() - 2);
  maxDate.setDate(maxDate.getDate() + 3);

  // Ensure today is always visible
  const today = new Date();
  today.setHours(0, 0, 0, 0);
  if (today < minDate) minDate.setTime(today.getTime() - 2 * 86400000);
  if (today > maxDate) maxDate.setTime(today.getTime() + 3 * 86400000);

  const days = buildDayArray(minDate, maxDate);
  const totalWidth = days.length * DAY_PX;

  const wrapper = document.createElement('div');
  wrapper.className = 'gantt-wrapper';

  const table = document.createElement('div');
  table.className = 'gantt-table';

  // === Sidebar ===
  const sidebar = buildSidebar(items, s);

  // === Timeline ===
  const timeline = document.createElement('div');
  timeline.className = 'gantt-timeline';
  timeline.style.width = `${totalWidth}px`;
  timeline.style.minWidth = `${totalWidth}px`;
  timeline.style.position = 'relative';

  const header = buildTimelineHeader(days);
  const body = buildTimelineBody(items, days, minDate, totalWidth, s);

  timeline.appendChild(header);
  timeline.appendChild(body);

  table.appendChild(sidebar);
  table.appendChild(timeline);
  wrapper.appendChild(table);

  if (unresolvable.length > 0) {
    wrapper.appendChild(buildNoDatesSection(unresolvable, s));
  }

  container.appendChild(wrapper);

  // Attach tooltip after rendering
  attachTooltip(container, s);
}

// ---- Item collection ----

function collectItems(
  data: BoardData,
  mode: GanttMode,
  s: GanttStrings,
): { items: GanttItem[]; unresolvable: UnresolvableCard[] } {
  const items: GanttItem[] = [];
  const unresolvable: UnresolvableCard[] = [];

  for (const col of data.columns) {
    for (const card of col.cards) {
      const dates = resolveItemDates(card, mode);
      if (dates) {
        items.push({
          cardId: card.id,
          boardId: data.id,
          title: card.title,
          columnId: col.id,
          columnName: col.name,
          columnStatus: col.status,
          assignee: card.assignee,
          priority: card.priority,
          start: dates[0],
          end: dates[1],
        });
      } else {
        unresolvable.push({
          cardId: card.id,
          boardId: data.id,
          title: card.title,
          columnName: col.name,
          columnStatus: col.status,
          assignee: card.assignee,
          priority: card.priority,
          reason: missingReason(card, mode, s),
        });
      }
    }
  }

  // Sort items by start date ascending, then by end date descending (longer bar first)
  items.sort((a, b) => {
    const diff = a.start.getTime() - b.start.getTime();
    return diff !== 0 ? diff : b.end.getTime() - a.end.getTime();
  });

  return { items, unresolvable };
}

// ---- Day array ----

interface DayInfo {
  date: Date;
  dayIndex: number; // 0-based offset from minDate
  isWeekend: boolean;
  isToday: boolean;
  label: string;   // "5", "10", etc. — show every 5th
}

function buildDayArray(minDate: Date, maxDate: Date): DayInfo[] {
  const days: DayInfo[] = [];
  const today = new Date();
  today.setHours(0, 0, 0, 0);

  const current = new Date(minDate);
  current.setHours(0, 0, 0, 0);
  let idx = 0;

  while (current <= maxDate) {
    const dow = current.getDay();
    const d = new Date(current);
    days.push({
      date: d,
      dayIndex: idx,
      isWeekend: dow === 0 || dow === 6,
      isToday: d.toDateString() === today.toDateString(),
      label: d.getDate() === 1 || d.getDate() % 5 === 0 ? String(d.getDate()) : '',
    });
    current.setDate(current.getDate() + 1);
    idx++;
  }
  return days;
}

// ---- Navigation helper ----

function navigateToCard(cardId: number, boardId: number): void {
  window.location.href = `/Cards/${cardId}?returnBoardId=${boardId}`;
}

// ---- Sidebar ----

function buildSidebar(items: GanttItem[], s: GanttStrings): HTMLElement {
  const sidebar = document.createElement('div');
  sidebar.className = 'gantt-sidebar';

  const header = document.createElement('div');
  header.className = 'gantt-sidebar-header';
  header.textContent = s.cards;
  sidebar.appendChild(header);

  for (const item of items) {
    const row = document.createElement('div');
    row.className = 'gantt-sidebar-row';
    row.dataset.ganttCardId = String(item.cardId);
    row.addEventListener('click', () => navigateToCard(item.cardId, item.boardId));

    const dot = document.createElement('div');
    dot.className = 'gantt-priority-dot';
    dot.style.background = PRIORITY_COLORS[item.priority];

    const title = document.createElement('div');
    title.className = 'gantt-card-title';
    title.textContent = item.title;
    title.title = item.title;

    const meta = document.createElement('div');
    meta.className = 'gantt-card-meta';

    const badge = document.createElement('span');
    badge.className = 'gantt-column-badge';
    badge.textContent = item.columnName;
    badge.title = item.columnName;
    meta.appendChild(badge);

    if (item.assignee) {
      meta.appendChild(buildAssigneeAvatar(item.assignee));
    }

    row.appendChild(dot);
    row.appendChild(title);
    row.appendChild(meta);
    sidebar.appendChild(row);
  }

  return sidebar;
}

function buildAssigneeAvatar(assignee: { displayName: string; avatarUrl?: string }): HTMLElement {
  if (assignee.avatarUrl) {
    const wrap = document.createElement('div');
    wrap.className = 'gantt-assignee-avatar';
    wrap.title = assignee.displayName;
    const img = document.createElement('img');
    img.src = assignee.avatarUrl;
    img.alt = assignee.displayName;
    wrap.appendChild(img);
    return wrap;
  }

  const fallback = document.createElement('div');
  fallback.className = 'gantt-assignee-fallback';
  fallback.title = assignee.displayName;
  fallback.textContent = assignee.displayName.charAt(0).toUpperCase();
  return fallback;
}

// ---- Timeline Header ----

function buildTimelineHeader(days: DayInfo[]): HTMLElement {
  const header = document.createElement('div');
  header.className = 'gantt-timeline-header';

  // Month row
  const monthRow = document.createElement('div');
  monthRow.className = 'gantt-month-row';

  // Group days by month — use browser locale for display
  const monthGroups: { label: string; count: number }[] = [];
  let curMonth = '';
  for (const day of days) {
    const label = day.date.toLocaleDateString(undefined, { year: 'numeric', month: 'long' });
    if (label !== curMonth) {
      monthGroups.push({ label, count: 1 });
      curMonth = label;
    } else {
      monthGroups[monthGroups.length - 1].count++;
    }
  }

  for (const mg of monthGroups) {
    const cell = document.createElement('div');
    cell.className = 'gantt-month-cell';
    cell.style.width = `${mg.count * DAY_PX}px`;
    cell.style.minWidth = `${mg.count * DAY_PX}px`;
    cell.textContent = mg.label;
    monthRow.appendChild(cell);
  }

  // Day row
  const dayRow = document.createElement('div');
  dayRow.className = 'gantt-day-row';
  for (const day of days) {
    const cell = document.createElement('div');
    cell.className = 'gantt-day-cell';
    cell.style.width = `${DAY_PX}px`;
    cell.style.minWidth = `${DAY_PX}px`;
    if (day.isWeekend) cell.classList.add('is-weekend');
    if (day.isToday) cell.classList.add('is-today');
    if (day.label) cell.textContent = day.label;
    dayRow.appendChild(cell);
  }

  header.appendChild(monthRow);
  header.appendChild(dayRow);
  return header;
}

// ---- Timeline Body ----

function buildTimelineBody(
  items: GanttItem[],
  days: DayInfo[],
  minDate: Date,
  totalWidth: number,
  s: GanttStrings,
): HTMLElement {
  const body = document.createElement('div');
  body.className = 'gantt-body';
  body.style.width = `${totalWidth}px`;
  body.style.minWidth = `${totalWidth}px`;
  body.style.position = 'relative';

  const minTime = minDate.getTime();
  const DAY_MS = 86400000;

  // Weekend column shading
  for (const day of days) {
    if (day.isWeekend) {
      const shade = document.createElement('div');
      shade.className = 'gantt-row-bg-weekend';
      shade.style.left = `${day.dayIndex * DAY_PX}px`;
      shade.style.width = `${DAY_PX}px`;
      body.appendChild(shade);
    }
  }

  // Today line
  const today = new Date();
  today.setHours(0, 0, 0, 0);
  const todayOffset = (today.getTime() - minTime) / DAY_MS;
  if (todayOffset >= 0 && todayOffset <= days.length) {
    const todayLine = document.createElement('div');
    todayLine.className = 'gantt-today-line';
    todayLine.style.left = `${todayOffset * DAY_PX + DAY_PX / 2}px`;
    body.appendChild(todayLine);
  }

  // Card rows
  for (const item of items) {
    const row = document.createElement('div');
    row.className = 'gantt-row';
    row.dataset.ganttCardId = String(item.cardId);
    row.addEventListener('click', () => navigateToCard(item.cardId, item.boardId));

    const startOffset = (item.start.getTime() - minTime) / DAY_MS;
    const duration = Math.max(1, (item.end.getTime() - item.start.getTime()) / DAY_MS + 1);
    const left = startOffset * DAY_PX;
    const width = duration * DAY_PX - 2; // 1px gap

    const bar = document.createElement('div');
    bar.className = `gantt-bar gantt-bar--${statusClass(item.columnStatus)}`;
    bar.style.left = `${left}px`;
    bar.style.width = `${width}px`;

    // Store tooltip data (resolved, localized strings)
    bar.dataset.ganttTitle = item.title;
    bar.dataset.ganttStart = formatDate(item.start);
    bar.dataset.ganttEnd = formatDate(item.end);
    bar.dataset.ganttColumn = item.columnName;
    bar.dataset.ganttStatus = humanStatus(item.columnStatus, s);
    bar.dataset.ganttAssignee = item.assignee?.displayName ?? '';
    bar.dataset.ganttPriority = item.priority;

    const inner = document.createElement('div');
    inner.className = 'gantt-bar-inner';
    if (width > 60) {
      inner.textContent = item.title;
    }
    bar.appendChild(inner);
    row.appendChild(bar);
    body.appendChild(row);
  }

  return body;
}

// ---- Tooltip ----

function attachTooltip(container: HTMLElement, s: GanttStrings): void {
  let tooltip: HTMLElement | null = null;

  container.addEventListener('mouseover', e => {
    const bar = (e.target as HTMLElement).closest<HTMLElement>('[data-gantt-title]');
    if (!bar) return;

    if (!tooltip) {
      tooltip = document.createElement('div');
      tooltip.className = 'gantt-tooltip';
      document.body.appendChild(tooltip);
    }

    const title = bar.dataset.ganttTitle ?? '';
    const start = bar.dataset.ganttStart ?? '';
    const end = bar.dataset.ganttEnd ?? '';
    const column = bar.dataset.ganttColumn ?? '';
    const status = bar.dataset.ganttStatus ?? '';
    const assignee = bar.dataset.ganttAssignee ?? '';
    const priority = bar.dataset.ganttPriority ?? '';

    tooltip.innerHTML = `
      <div class="gantt-tooltip-title">${escHtml(title)}</div>
      <div class="gantt-tooltip-row"><span>${escHtml(s.timeLabel)}</span><span>${escHtml(start)} → ${escHtml(end)}</span></div>
      <div class="gantt-tooltip-row"><span>${escHtml(s.columnLabel)}</span><span>${escHtml(column)}</span></div>
      <div class="gantt-tooltip-row"><span>${escHtml(s.statusLabel)}</span><span>${escHtml(status)}</span></div>
      ${assignee ? `<div class="gantt-tooltip-row"><span>${escHtml(s.assigneeLabel)}</span><span>${escHtml(assignee)}</span></div>` : ''}
      <div class="gantt-tooltip-row"><span>${escHtml(s.priorityLabel)}</span><span>${escHtml(priority)}</span></div>
    `;
    tooltip.style.opacity = '1';
  });

  container.addEventListener('mousemove', e => {
    if (!tooltip) return;
    tooltip.style.left = `${e.clientX + 14}px`;
    tooltip.style.top = `${e.clientY - 10}px`;
  });

  container.addEventListener('mouseout', e => {
    const bar = (e.target as HTMLElement).closest<HTMLElement>('[data-gantt-title]');
    if (!bar || !container.contains(e.relatedTarget as Node)) {
      if (tooltip) tooltip.style.opacity = '0';
    }
  });
}

// ---- "No dates" section ----

function buildNoDatesSection(items: UnresolvableCard[], s: GanttStrings): HTMLElement {
  const section = document.createElement('div');
  section.className = 'gantt-no-dates-section';

  const toggle = document.createElement('div');
  toggle.className = 'gantt-no-dates-toggle';

  const icon = document.createElement('i');
  icon.setAttribute('data-lucide', 'chevron-down');
  icon.className = 'gantt-no-dates-toggle-icon';

  const countBadge = document.createElement('span');
  countBadge.className = 'gantt-no-dates-count';
  countBadge.textContent = String(items.length);

  toggle.appendChild(icon);
  toggle.appendChild(document.createTextNode(s.noDatesCards));
  toggle.appendChild(countBadge);

  const list = document.createElement('div');
  list.className = 'gantt-no-dates-list';

  for (const item of items) {
    const el = document.createElement('div');
    el.className = 'gantt-no-dates-item';
    el.addEventListener('click', () => navigateToCard(item.cardId, item.boardId));

    const dot = document.createElement('div');
    dot.className = 'gantt-priority-dot';
    dot.style.background = PRIORITY_COLORS[item.priority];

    const titleEl = document.createElement('div');
    titleEl.className = 'gantt-no-dates-item-title';
    titleEl.textContent = item.title;
    titleEl.title = item.title;

    const badge = document.createElement('span');
    badge.className = 'gantt-column-badge';
    badge.textContent = item.columnName;

    const reason = document.createElement('span');
    reason.className = 'gantt-no-dates-item-reason';
    reason.textContent = item.reason;

    el.appendChild(dot);
    el.appendChild(titleEl);
    el.appendChild(badge);
    if (item.assignee) el.appendChild(buildAssigneeAvatarSmall(item.assignee));
    el.appendChild(reason);
    list.appendChild(el);
  }

  // collapsed by default
  let collapsed = true;
  list.style.display = 'none';
  icon.classList.add('collapsed');

  toggle.addEventListener('click', () => {
    collapsed = !collapsed;
    list.style.display = collapsed ? 'none' : 'block';
    icon.classList.toggle('collapsed', collapsed);
  });

  section.appendChild(toggle);
  section.appendChild(list);
  return section;
}

function buildAssigneeAvatarSmall(assignee: { displayName: string; avatarUrl?: string }): HTMLElement {
  if (assignee.avatarUrl) {
    const wrap = document.createElement('div');
    wrap.className = 'gantt-assignee-avatar';
    wrap.title = assignee.displayName;
    const img = document.createElement('img');
    img.src = assignee.avatarUrl;
    img.alt = assignee.displayName;
    wrap.appendChild(img);
    return wrap;
  }
  const fb = document.createElement('div');
  fb.className = 'gantt-assignee-fallback';
  fb.title = assignee.displayName;
  fb.textContent = assignee.displayName.charAt(0).toUpperCase();
  return fb;
}

// ---- Empty state ----

function buildEmptyState(message: string): HTMLElement {
  const wrapper = document.createElement('div');
  wrapper.className = 'gantt-wrapper';

  const state = document.createElement('div');
  state.className = 'gantt-empty-state';

  const icon = document.createElement('i');
  icon.setAttribute('data-lucide', 'calendar-x-2');

  const p = document.createElement('p');
  p.textContent = message;

  state.appendChild(icon);
  state.appendChild(p);
  wrapper.appendChild(state);
  return wrapper;
}

// ---- Helpers ----

function statusClass(status: string): string {
  switch (status) {
    case 'Completed': return 'completed';
    case 'InProgress': return 'inprogress';
    default: return 'notstarted';
  }
}

function humanStatus(status: string, s: GanttStrings): string {
  switch (status) {
    case 'Completed': return s.statusCompleted;
    case 'InProgress': return s.statusInProgress;
    default: return s.statusNotStarted;
  }
}

/** Use browser locale for date display */
function formatDate(d: Date): string {
  return d.toLocaleDateString(undefined, { year: 'numeric', month: '2-digit', day: '2-digit' });
}

function escHtml(value: string): string {
  const div = document.createElement('div');
  div.textContent = value;
  return div.innerHTML;
}
