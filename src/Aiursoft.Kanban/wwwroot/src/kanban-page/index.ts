import Sortable from 'sortablejs';
import { KanbanBoard, rerenderCardElement } from '../kanban-board';
import type { BoardData, UserSummary } from '../kanban-board';
import { t } from '../kanban-board/i18n';

interface KanbanIndexPageOptions {
  csrfToken: string;
  boardId: number;
  boardData: BoardData | null;
  highlightCardId?: number;
  canEditCurrentBoard: boolean;
}

interface BootstrapModalInstance {
  show(): void;
  hide(): void;
}

interface BootstrapModalStatic {
  new (element: Element): BootstrapModalInstance;
  getInstance?(element: Element): BootstrapModalInstance | null;
}

declare global {
  interface Window {
    bootstrap?: {
      Modal?: BootstrapModalStatic;
    };
    lucide?: {
      createIcons(options?: { nodes?: ParentNode[] }): void;
    };
  }
}

export function initKanbanIndexPage(options: KanbanIndexPageOptions): void {
  initBoardList(options.csrfToken);

  if (options.boardData) {
    initBoardPage(options);
  }

  refreshIcons();
}

function initBoardPage(options: KanbanIndexPageOptions): void {
  const container = document.getElementById('kanban-root');
  if (!container || !options.boardData) return;

  KanbanBoard({
    container,
    data: options.boardData,
    highlightCardId: options.highlightCardId,
    callbacks: {
      onCardClicked: cardId => {
        window.location.href = `/Cards/${cardId}?returnBoardId=${options.boardId}`;
      },
      onAddCardRequested: columnId => {
        window.location.href = `/Cards/New?columnId=${columnId}&returnBoardId=${options.boardId}`;
      },
      onCardMoved: async (cardId, targetColumnId, newOrder) => {
        const response = await postForm('/Kanban/MoveCard', {
          cardId,
          targetColumnId,
          newOrder,
        }, options.csrfToken);
        const result = await readJsonOrThrow<Record<string, unknown>>(response);
        syncMovedCard(container, cardId, targetColumnId, result);
      },
      onColumnReordered: async (columnId, newOrder) => {
        await ensureOk(postForm('/Kanban/MoveColumn', {
          columnId,
          newOrder,
        }, options.csrfToken));
      },
      onColumnRenamed: async (columnId, newName) => {
        await ensureOk(postForm('/Kanban/RenameColumn', {
          columnId,
          name: newName,
        }, options.csrfToken));
      },
      onColumnStatusChanged: async (columnId, newStatus) => {
        await ensureOk(postForm('/Kanban/UpdateColumnStatus', {
          columnId,
          status: newStatus,
        }, options.csrfToken));

        const columnEl = container.querySelector<HTMLElement>(`.kanban-column[data-column-id="${columnId}"]`);
        if (!columnEl) return;

        columnEl.setAttribute('data-column-status', newStatus);
        columnEl.querySelectorAll<HTMLElement>('.kanban-card').forEach(cardEl => {
          rerenderCardElement(cardEl);
        });
      },
      onColumnDeleted: async columnId => {
        await deleteColumnWithConfirmation(columnId, options.csrfToken);
      },
      onCreateColumn: () => {
        showModalById('addColumnModal');
      },
    },
  });

  document.getElementById('btnAddColumn')?.addEventListener('click', () => {
    showModalById('addColumnModal');
  });

  initBoardRename(options);
  initAddColumnModal(options);
  refreshIcons(container);
}

function initBoardRename(options: KanbanIndexPageOptions): void {
  const button = document.querySelector<HTMLElement>('.btn-edit-board-title');
  if (!button || button.dataset.bound === 'true') return;

  button.dataset.bound = 'true';
  button.addEventListener('click', event => {
    event.preventDefault();

    const titleDisplay = document.querySelector<HTMLElement>('.board-title-display');
    if (!titleDisplay || titleDisplay.parentElement?.querySelector('input.board-title-input')) return;

    const currentName = titleDisplay.textContent?.trim() ?? '';
    const input = document.createElement('input');
    input.type = 'text';
    input.className = 'board-title-input';
    input.value = currentName;

    const finish = async (save: boolean) => {
      const nextName = input.value.trim();
      if (save && nextName && nextName !== currentName) {
        try {
          await ensureOk(postForm('/Kanban/RenameBoard', {
            boardId: options.boardId,
            name: nextName,
          }, options.csrfToken));
          titleDisplay.textContent = nextName;
        } catch (error) {
          showFriendlyDialog(getErrorMessage(error, t('failed-rename-board', 'Failed to rename board.')));
        }
      }

      const replacement = document.createElement('span');
      replacement.className = titleDisplay.className;
      replacement.style.fontSize = titleDisplay.style.fontSize;
      replacement.textContent = titleDisplay.textContent ?? currentName;
      input.replaceWith(replacement);
    };

    input.addEventListener('keydown', keyEvent => {
      if (keyEvent.key === 'Enter') {
        keyEvent.preventDefault();
        finish(true).catch(console.error);
      } else if (keyEvent.key === 'Escape') {
        keyEvent.preventDefault();
        finish(false).catch(console.error);
      }
    });

    input.addEventListener('blur', () => {
      finish(true).catch(console.error);
    });

    titleDisplay.replaceWith(input);
    input.focus();
    input.select();
  });
}

function initAddColumnModal(options: KanbanIndexPageOptions): void {
  const saveButton = document.getElementById('btnSaveColumn') as HTMLButtonElement | null;
  const input = document.getElementById('columnNameInput') as HTMLInputElement | null;
  const error = document.getElementById('columnError');
  const modalElement = document.getElementById('addColumnModal');
  if (!saveButton || !input || !modalElement) return;

  saveButton.addEventListener('click', async () => {
    const name = input.value.trim();
    if (!name) {
      input.classList.add('is-invalid');
      if (error) error.textContent = t('column-name-required', 'Column name is required.');
      return;
    }

    input.classList.remove('is-invalid');
    if (error) error.textContent = '';

    saveButton.disabled = true;
    try {
      await ensureOk(postForm('/Kanban/CreateColumn', {
        boardId: options.boardId,
        name,
      }, options.csrfToken));
      hideModal(modalElement);
      window.location.reload();
    } catch (problem) {
      input.classList.add('is-invalid');
      if (error) error.textContent = getErrorMessage(problem, t('failed-create-column', 'Failed to create column.'));
    } finally {
      saveButton.disabled = false;
    }
  });

  modalElement.addEventListener('shown.bs.modal', () => {
    input.focus();
    input.select();
  });
}

function initBoardList(csrfToken: string): void {
  const boardList = document.getElementById('boardList');
  if (boardList) {
    boardList.addEventListener('click', event => {
      const target = event.target as HTMLElement;
      if (target.closest('a, button, .drag-handle')) return;

      const row = target.closest<HTMLElement>('.clickable-row');
      const href = row?.getAttribute('data-href');
      if (href) {
        window.location.href = href;
      }
    });
  }

  const boardListBody = document.getElementById('boardListBody');
  if (boardListBody) {
    new Sortable(boardListBody, {
      animation: 200,
      easing: 'cubic-bezier(0.25, 0.46, 0.45, 0.94)',
      handle: '.drag-handle',
      ghostClass: 'sortable-ghost',
      chosenClass: 'sortable-chosen',
      onEnd: event => {
        const boardId = parseInt((event.item as HTMLElement).dataset.boardId ?? '0', 10);
        const newOrder = event.newIndex ?? 0;
        postForm('/Kanban/MoveBoard', {
          boardId,
          newOrder,
        }, csrfToken).catch(console.error);
      },
    });
  }

  const deleteButtons = document.querySelectorAll<HTMLElement>('.btn-delete-board-list');
  const deleteBoardNameDisplay = document.getElementById('deleteBoardNameDisplay');
  const confirmButton = document.getElementById('btnConfirmDeleteBoard') as HTMLButtonElement | null;
  const deleteError = document.getElementById('deleteBoardError');
  const modalElement = document.getElementById('deleteBoardConfirmModal');

  if (!confirmButton || !modalElement) return;

  let pendingBoardId = '';
  let pendingRow: HTMLElement | null = null;

  deleteButtons.forEach(button => {
    button.addEventListener('click', () => {
      pendingBoardId = button.dataset.boardId ?? '';
      pendingRow = button.closest<HTMLElement>('tr');
      if (deleteBoardNameDisplay) {
        deleteBoardNameDisplay.textContent = button.dataset.boardName ?? '';
      }
      clearInlineAlert(deleteError);
      showModal(modalElement);
    });
  });

  confirmButton.addEventListener('click', async () => {
    if (!pendingBoardId) return;

    confirmButton.disabled = true;
    try {
      await ensureOk(postForm('/Kanban/DeleteBoard', {
        boardId: pendingBoardId,
      }, csrfToken));

      pendingRow?.remove();
      hideModal(modalElement);

      if (!document.querySelector('#boardListBody tr')) {
        window.location.reload();
      }
    } catch (problem) {
      showInlineAlert(deleteError, getErrorMessage(problem, t('failed-delete-board', 'Failed to delete board.')));
    } finally {
      confirmButton.disabled = false;
    }
  });
}

function buildUserSummary(source: { id: unknown; name: unknown; avatarUrl: unknown }): UserSummary | undefined {
  const userId = readOptionalString(source.id);
  const displayName = readOptionalString(source.name);
  if (!userId || !displayName) return undefined;

  return {
    userId,
    displayName,
    avatarUrl: readOptionalString(source.avatarUrl),
  };
}

function syncMovedCard(
  container: HTMLElement,
  cardId: number,
  requestedColumnId: number,
  result: Record<string, unknown>,
): void {
  const cardEl = container.querySelector<HTMLElement>(`.kanban-card[data-card-id="${cardId}"]`);
  if (!cardEl) return;

  const actualColumnId = readNumber(result.ColumnId) || requestedColumnId;
  const columnCards = container.querySelector<HTMLElement>(`.column-cards[data-column-id="${actualColumnId}"]`);
  if (columnCards && cardEl.parentElement !== columnCards) {
    columnCards.appendChild(cardEl);
  }

  setData(cardEl, 'due-date', readOptionalString(result.DueDate) ?? '');
  setData(cardEl, 'actual-start', readOptionalString(result.ActualStartTime) ?? '');
  setData(cardEl, 'actual-end', readOptionalString(result.ActualEndTime) ?? '');
  if (readOptionalString(result.DueDate)) {
    setData(cardEl, 'is-overdue', String(isCardOverdue(readOptionalString(result.DueDate)!)));
  }

  rerenderCardElement(cardEl);
  refreshColumnVisualState(cardEl.closest<HTMLElement>('.kanban-column'));
  refreshColumnVisualState(container.querySelector<HTMLElement>(`.kanban-column[data-column-id="${actualColumnId}"]`));
  triggerFilterReapply();

  if (readBoolean(result.RecurrenceApplied)) {
    const targetName = readOptionalString(result.RecurrenceTargetColumnName) ?? t('not-started', 'Not Started');
    showFriendlyDialog(t('recurring-task-returned', 'Recurring task has been returned to {0}.').replace('{0}', targetName));
  }
}

async function deleteColumnWithConfirmation(columnId: number, csrfToken: string): Promise<void> {
  const modalElement = document.getElementById('deleteColumnConfirmModal');
  const nameDisplay = document.getElementById('deleteColumnNameDisplay');
  const warning = document.getElementById('deleteColumnWarning');
  const error = document.getElementById('deleteColumnError');
  const confirmButton = document.getElementById('btnConfirmDeleteColumn') as HTMLButtonElement | null;
  const columnEl = document.querySelector<HTMLElement>(`.kanban-column[data-column-id="${columnId}"]`);
  if (!modalElement || !confirmButton || !columnEl) return;

  const columnName = columnEl.querySelector<HTMLElement>('.column-title')?.textContent?.trim() ?? '';
  const cardCount = columnEl.querySelectorAll('.kanban-card').length;

  if (nameDisplay) {
    nameDisplay.textContent = columnName;
  }

  clearInlineAlert(error);
  clearInlineAlert(warning);

  if (cardCount > 0) {
    showInlineAlert(
      warning,
      t('delete-column-warning', 'This column contains cards that will be deleted together.'),
      'alert-triangle',
    );
  }

  showModal(modalElement);

  await new Promise<void>(resolve => {
    const onConfirm = async () => {
      confirmButton.disabled = true;
      try {
        await ensureOk(postForm('/Kanban/DeleteColumn', { columnId }, csrfToken));
        columnEl.remove();
        hideModal(modalElement);
        resolve();
      } catch (problem) {
        showInlineAlert(error, getErrorMessage(problem, t('failed-delete-column', 'Failed to delete column.')));
      } finally {
        confirmButton.disabled = false;
      }
    };

    const onHide = () => {
      confirmButton.removeEventListener('click', onConfirm);
      modalElement.removeEventListener('hidden.bs.modal', onHide as EventListener);
      resolve();
    };

    confirmButton.addEventListener('click', onConfirm, { once: true });
    modalElement.addEventListener('hidden.bs.modal', onHide as EventListener, { once: true });
  });
}

function refreshColumnVisualState(columnEl: HTMLElement | null): void {
  if (!columnEl) return;

  const cardsContainer = columnEl.querySelector<HTMLElement>('.column-cards');
  if (!cardsContainer) return;

  const cards = cardsContainer.querySelectorAll('.kanban-card');
  const count = columnEl.querySelector<HTMLElement>('.column-count');
  if (count) {
    count.textContent = String(cards.length);
  }

  const placeholder = cardsContainer.querySelector('.column-empty-placeholder');
  if (cards.length === 0 && !placeholder) {
    const empty = document.createElement('div');
    empty.className = 'column-empty-placeholder';
    empty.textContent = t('drop-cards-here', 'Drop cards here');
    cardsContainer.appendChild(empty);
  } else if (cards.length > 0 && placeholder) {
    placeholder.remove();
  }
}

function triggerFilterReapply(): void {
  document.dispatchEvent(new CustomEvent('kanban:filters-apply'));
}

function postForm(url: string, values: Record<string, string | number>, csrfToken: string): Promise<Response> {
  const body = new URLSearchParams();
  body.set('__RequestVerificationToken', csrfToken);
  Object.entries(values).forEach(([key, value]) => {
    body.set(key, String(value));
  });

  return fetch(url, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/x-www-form-urlencoded',
      RequestVerificationToken: csrfToken,
    },
    body,
  });
}

async function ensureOk(responsePromise: Promise<Response> | Response): Promise<Response> {
  const response = await responsePromise;
  if (response.ok) return response;

  const text = await response.text();
  throw new Error(text || `${t('server-error', 'Server error')} ${response.status}`);
}

async function readJsonOrThrow<T>(response: Response): Promise<T> {
  const okResponse = await ensureOk(response);
  return okResponse.json() as Promise<T>;
}

function readNumber(value: unknown, fallback = 0): number {
  if (typeof value === 'number') return value;
  if (typeof value === 'string' && value.trim()) {
    const parsed = parseInt(value, 10);
    return Number.isNaN(parsed) ? fallback : parsed;
  }
  return fallback;
}

function readBoolean(value: unknown): boolean {
  return value === true || value === 'true';
}

function readString(value: unknown): string {
  return typeof value === 'string' ? value : '';
}

function readOptionalString(value: unknown): string | undefined {
  const text = readString(value).trim();
  return text ? text : undefined;
}

function setData(element: HTMLElement, name: string, value: string): void {
  if (!value) {
    element.removeAttribute(`data-${name}`);
    return;
  }
  element.setAttribute(`data-${name}`, value);
}

function isCardOverdue(dueDate: string): boolean {
  const parsed = new Date(dueDate);
  if (Number.isNaN(parsed.getTime())) return false;
  parsed.setHours(0, 0, 0, 0);

  const today = new Date();
  today.setHours(0, 0, 0, 0);
  return parsed < today;
}

function clearInlineAlert(alertElement: HTMLElement | null): void {
  if (!alertElement) return;
  alertElement.innerHTML = '';
  alertElement.classList.add('d-none');
}

function showInlineAlert(alertElement: HTMLElement | null, message: string, iconName = 'alert-circle'): void {
  if (!alertElement) return;

  alertElement.innerHTML = `
    <div class="d-flex align-items-start">
      <div class="alert-icon pe-3">
        <i class="align-middle" data-lucide="${escapeHtml(iconName)}"></i>
      </div>
      <div class="alert-message">${escapeHtml(message)}</div>
    </div>`;
  alertElement.classList.remove('d-none');
  refreshIcons(alertElement);
}

function showFriendlyDialog(message: string): void {
  const modalElement = document.getElementById('friendlyAlertModal');
  const title = document.getElementById('friendlyAlertTitle');
  const icon = document.getElementById('friendlyAlertIcon');
  const body = document.getElementById('friendlyAlertMessage');
  if (!modalElement || !title || !icon || !body) return;

  title.textContent = t('notice', 'Notice');
  body.textContent = message;
  icon.setAttribute('data-lucide', 'alert-circle');
  refreshIcons(modalElement);
  showModal(modalElement);
}

function showModalById(id: string): void {
  showModal(document.getElementById(id));
}

function showModal(element: HTMLElement | null): void {
  getModalInstance(element)?.show();
}

function hideModal(element: HTMLElement | null): void {
  getModalInstance(element)?.hide();
}

function getModalInstance(element: HTMLElement | null): BootstrapModalInstance | null {
  if (!element) return null;
  const Modal = window.bootstrap?.Modal;
  if (!Modal) return null;
  return Modal.getInstance?.(element) ?? new Modal(element);
}

function refreshIcons(node?: ParentNode): void {
  const lucide = window.lucide;
  if (!lucide) return;

  if (node) {
    lucide.createIcons({ nodes: [node] });
  } else {
    lucide.createIcons();
  }
}

function escapeHtml(value: string): string {
  const div = document.createElement('div');
  div.textContent = value;
  return div.innerHTML;
}

function getErrorMessage(error: unknown, fallback: string): string {
  if (error instanceof Error && error.message) {
    return error.message;
  }
  return fallback;
}
