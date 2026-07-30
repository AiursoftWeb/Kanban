import type { CardLabel } from '../kanban-board';
import { t } from '../kanban-board/i18n';

interface CardDetailPageOptions {
  csrfToken: string;
  cardId: number;
  boardId: number;
  returnBoardId: number;
  canEdit: boolean;
  imageUploadUrl: string;
  returnBoardUrl: string;
  initialTitle: string;
  initialPriority: number;
  initialAssigneeId?: string;
  initialAssigneeName?: string;
  initialAssigneeInitial?: string;
  initialAssigneeAvatarUrl?: string;
  initialLabels: CardLabel[];
}

interface CommentDto {
  Id: number;
  Content: string;
  CreationTime: string;
  Images?: string;
  AuthorName?: string;
  AuthorInitial?: string;
  Avatar?: string;
  CanDelete?: boolean;
}

interface TransferTargetDto {
  Id: number;
  Name: string;
  Columns: Array<{
    Id: number;
    Name: string;
  }>;
}

interface LabelSearchResult {
  Id: number;
  Name: string;
  Color: string;
}

interface BoardMemberDto {
  Id: string;
  DisplayName?: string;
  UserName?: string;
  Initial?: string;
}

interface BootstrapModalInstance {
  show(): void;
  hide(): void;
}

interface BootstrapModalStatic {
  new (element: Element): BootstrapModalInstance;
  getInstance?(element: Element): BootstrapModalInstance | null;
}

interface LucideLike {
  createIcons(options?: { nodes?: ParentNode[] }): void;
}

interface MathJaxLike {
  typesetPromise(elements?: HTMLElement[]): Promise<void>;
}

interface ImageDropzoneApi {
  getFiles(): File[];
  clearFiles(): void;
}

interface MonacoEditorLike {
  getValue(): string;
  setValue(value: string): void;
  onDidChangeModelContent(cb: () => void): { dispose(): void };
  layout(): void;
  focus(): void;
  getDomNode(): HTMLElement | null;
  getPosition(): { lineNumber: number; column: number } | null;
  executeEdits(source: string, edits: Array<{
    range: { startLineNumber: number; startColumn: number; endLineNumber: number; endColumn: number };
    text: string;
    forceMoveMarkers?: boolean;
  }>): void;
}

interface AiursoftMarkdownUiLike {
  renderMarkdown(markdown: string, options?: { breaks?: boolean }): string;
  initializeMarkdownReader(options: { container: string | HTMLElement | Iterable<HTMLElement> }): Promise<void>;
  loadMonacoFromAmd(): Promise<unknown>;
  createMarkdownEditor(options: {
    editorContainer: HTMLElement;
    textarea: HTMLTextAreaElement;
    previewContainer: HTMLElement | null;
    loadMonaco: () => Promise<unknown>;
    uploadUrl: string;
    editorPane?: HTMLElement;
    previewPane?: HTMLElement;
    initialViewMode?: 'editor' | 'split' | 'preview';
    viewModeStorageKey?: string;
    viewModeControls?: Iterable<{
      element: HTMLElement;
      mode: 'editor' | 'split' | 'preview';
    }>;
    editorOptions?: Record<string, unknown>;
    onPreviewRendered?: (markdown: string) => void;
    onInitializationError?: (error: unknown) => void;
    onPreviewError?: (error: unknown) => void;
    /** @deprecated Use onInitializationError and onPreviewError instead. */
    onError?: (error: unknown) => void;
  }): Promise<{
    editor: MonacoEditorLike | null;
    getValue(): string;
    setValue(value: string): void;
    refreshPreview(): Promise<void>;
  }>;
  attachImageUpload(options: {
    editor: MonacoEditorLike;
    uploadUrl: string;
    onError?: (error: unknown, file: File) => void;
  }): {
    upload(files: Iterable<File>): Promise<void>;
    dispose(): void;
  };
}

declare global {
  interface Window {
    bootstrap?: {
      Modal?: BootstrapModalStatic;
    };
    lucide?: LucideLike;
    MathJax?: MathJaxLike;
    AiursoftMarkdownUi?: AiursoftMarkdownUiLike;
    monaco?: {
      editor: {
        create(element: HTMLElement, opts: Record<string, unknown>): MonacoEditorLike;
      };
    };
  }
}

export function initCardDetailPage(options: CardDetailPageOptions): void {
  const refs = {
    titleDisplay: document.getElementById('cardTitleDisplay'),
    titleInput: document.getElementById('cardTitleInput') as HTMLInputElement | null,
    titleMeta: document.getElementById('cardTitleMeta'),
    descriptionView: document.getElementById('descriptionView'),
    descriptionPreview: document.getElementById('descriptionPreview'),
    descriptionEdit: document.getElementById('descriptionEdit'),
    descriptionEditorContainer: document.getElementById('descEditorContainer'),
    descriptionEditorPane: document.getElementById('descEditTab'),
    descriptionInitialValue: document.getElementById('descInitialValue') as HTMLTextAreaElement | null,
    descriptionPreviewPane: document.getElementById('descPreviewTab'),
    descriptionLivePreview: document.getElementById('descLivePreview'),
    descriptionEmpty: document.getElementById('descriptionEmpty'),
    editDescriptionButton: document.getElementById('btnEditDesc'),
    cancelDescriptionButton: document.getElementById('btnCancelDesc'),
    saveDescriptionButton: document.getElementById('btnSaveDesc') as HTMLButtonElement | null,
    editorTabButton: document.getElementById('btnEditorTab'),
    previewTabButton: document.getElementById('btnPreviewTab'),
    priorityGroup: document.getElementById('priorityGroup'),
    dueDateInput: document.getElementById('inputDueDate') as HTMLInputElement | null,
    plannedStartInput: document.getElementById('inputPlannedStart') as HTMLInputElement | null,
    recurringSwitch: document.getElementById('inputRecurring') as HTMLInputElement | null,
    recurrenceFields: document.getElementById('recurrenceFields'),
    recurrenceIntervalInput: document.getElementById('inputRecurrenceInterval') as HTMLInputElement | null,
    recurrenceUnitInput: document.getElementById('inputRecurrenceUnit') as HTMLSelectElement | null,
    overviewAssigneeView: document.getElementById('overviewAssigneeView'),
    overviewAssignee: document.getElementById('overviewAssignee'),
    overviewAssigneeEdit: document.getElementById('overviewAssigneeEdit'),
    overviewAssigneeSearch: document.getElementById('overviewAssigneeSearch') as HTMLInputElement | null,
    overviewAssigneeDropdown: document.getElementById('overviewAssigneeDropdown'),
    btnEditAssignee: document.getElementById('btnEditAssignee'),
    btnOverviewClearAssignee: document.getElementById('btnOverviewClearAssignee'),
    heroAssigneeChip: document.querySelector<HTMLElement>('[data-assignee-name]'),
    labelsDisplay: document.getElementById('labelsDisplay'),
    labelInput: document.getElementById('labelSearchInput') as HTMLInputElement | null,
    addLabelButton: document.getElementById('btnAddLabel') as HTMLButtonElement | null,
    labelSuggestions: document.getElementById('labelSuggestions'),
    moveTargetColumn: document.getElementById('moveTargetColumn') as HTMLSelectElement | null,
    moveCardButton: document.getElementById('btnMoveCard') as HTMLButtonElement | null,
    transferTargetBoard: document.getElementById('transferTargetBoard') as HTMLSelectElement | null,
    transferTargetColumn: document.getElementById('transferTargetColumn') as HTMLSelectElement | null,
    transferCardButton: document.getElementById('btnTransferCard') as HTMLButtonElement | null,
    deleteCardButton: document.getElementById('btnDeleteCard'),
    confirmDeleteCardButton: document.getElementById('btnConfirmDeleteCard') as HTMLButtonElement | null,
    deleteCardModal: document.getElementById('deleteCardModal'),
    commentsList: document.getElementById('commentsList'),
    commentCount: document.getElementById('commentCount'),
    commentInput: document.getElementById('commentInput') as HTMLTextAreaElement | null,
    commentSectionHint: document.getElementById('commentSectionHint'),
    commentDragHint: document.getElementById('commentDragHint'),
    addCommentButton: document.getElementById('btnAddComment') as HTMLButtonElement | null,
    deleteCommentModal: document.getElementById('deleteCommentConfirmModal'),
    confirmDeleteCommentButton: document.getElementById('btnConfirmDeleteComment') as HTMLButtonElement | null,
    deleteCommentError: document.getElementById('deleteCommentError'),
    imageOverlay: document.getElementById('imageFullscreenOverlay'),
    imageOverlayImage: document.getElementById('imageFullscreenImg') as HTMLImageElement | null,
  };

  const state = {
    currentTitle: options.initialTitle,
    currentPriority: options.initialPriority,
    currentAssigneeId: options.initialAssigneeId ?? '',
    currentAssigneeName: options.initialAssigneeName ?? '',
    currentAssigneeInitial: options.initialAssigneeInitial ?? '',
    currentAssigneeAvatarUrl: options.initialAssigneeAvatarUrl ?? '',
    currentLabels: [...options.initialLabels],
    transferTargets: [] as TransferTargetDto[],
    labelSearchRequestId: 0,
    assigneeSearchTimer: 0 as number | undefined,
    commentIdToDelete: 0,
  };

  let monacoEditor: MonacoEditorLike | null = null;
  let markdownEditorController: Awaited<ReturnType<AiursoftMarkdownUiLike['createMarkdownEditor']>> | null = null;

  async function initDescriptionEditor(): Promise<void> {
    if (markdownEditorController || !refs.descriptionEditorContainer || !refs.descriptionInitialValue) return;

    if (!window.AiursoftMarkdownUi) return;

    markdownEditorController = await window.AiursoftMarkdownUi.createMarkdownEditor({
      editorContainer: refs.descriptionEditorContainer,
      textarea: refs.descriptionInitialValue,
      previewContainer: refs.descriptionLivePreview,
      loadMonaco: () => window.AiursoftMarkdownUi!.loadMonacoFromAmd(),
      uploadUrl: options.imageUploadUrl,
      editorPane: refs.descriptionEditorPane ?? undefined,
      previewPane: refs.descriptionPreviewPane ?? undefined,
      initialViewMode: 'editor',
      viewModeControls: [
        { element: refs.editorTabButton!, mode: 'editor' },
        { element: refs.previewTabButton!, mode: 'preview' },
      ].filter(control => control.element),
      editorOptions: {
        minimap: { enabled: false },
        scrollBeyondLastLine: false,
      },
      onPreviewRendered: () => {
        if (refs.descriptionLivePreview) {
          configureRenderedMarkdown(refs.descriptionLivePreview);
        }
      },
      onInitializationError: error => {
        console.error('Markdown editor initialization error:', error);
      },
      onPreviewError: error => {
        console.error('Markdown preview error:', error);
      },
    });
    monacoEditor = markdownEditorController.editor;
  }

  function getDescriptionValue(): string {
    return markdownEditorController?.getValue()?.trim() ?? refs.descriptionInitialValue?.value?.trim() ?? '';
  }

  const commentDropzone = options.canEdit && refs.commentInput
    ? setupImageDropzone(refs.commentInput)
    : null;

  renderLabels();
  renderAssigneeSummary();
  syncRecurrenceVisibility();
  renderMainDescription();
  loadComments().catch(console.error);
  if (refs.transferTargetBoard) {
    loadTransferTargets().catch(console.error);
  }
  refreshIcons();

  bindEvents();

  function bindEvents(): void {
    refs.editDescriptionButton?.addEventListener('click', () => {
      void initDescriptionEditor();
      toggleDescriptionEdit(true);
    });

    refs.cancelDescriptionButton?.addEventListener('click', () => {
      if (monacoEditor && refs.descriptionInitialValue) {
        markdownEditorController?.setValue(refs.descriptionInitialValue.value);
      }
      renderMainDescription();
      renderLiveDescriptionPreview();
      toggleDescriptionEdit(false);
    });

    refs.saveDescriptionButton?.addEventListener('click', async () => {
      try {
        await saveCardDetails();
        if (monacoEditor && refs.descriptionInitialValue) {
          refs.descriptionInitialValue.value = markdownEditorController?.getValue() ?? '';
        }
        toggleDescriptionEdit(false);
      } catch (error) {
        showFriendlyDialog(getErrorMessage(error, t('failed-save', 'Failed to save.')));
      }
    });

    if (refs.titleDisplay && refs.titleInput && options.canEdit) {
      refs.titleDisplay.addEventListener('click', () => {
        refs.titleDisplay?.classList.add('d-none');
        refs.titleMeta?.classList.add('d-none');
        refs.titleInput?.classList.remove('d-none');
        refs.titleInput?.focus();
        refs.titleInput?.select();
      });

      refs.titleInput.addEventListener('keydown', event => {
        if (event.key === 'Enter') {
          event.preventDefault();
          saveInlineTitle().catch(problem => {
            showFriendlyDialog(getErrorMessage(problem, t('failed-save', 'Failed to save.')));
          });
        } else if (event.key === 'Escape') {
          resetTitleEditor();
        }
      });

      refs.titleInput.addEventListener('blur', () => {
        saveInlineTitle().catch(problem => {
          showFriendlyDialog(getErrorMessage(problem, t('failed-save', 'Failed to save.')));
        });
      });
    }

    refs.priorityGroup?.addEventListener('click', event => {
      if (!options.canEdit) return;
      const badge = (event.target as HTMLElement).closest<HTMLElement>('.priority-badge.priority-selectable');
      if (!badge?.dataset.priority) return;

      updatePriority(parseInt(badge.dataset.priority, 10)).catch(problem => {
        showFriendlyDialog(getErrorMessage(problem, t('failed-save', 'Failed to save.')));
      });
    });

    refs.dueDateInput?.addEventListener('change', () => {
      if (!options.canEdit) return;
      saveCardDetails().then(() => showSavedToast()).catch(problem => {
        showFriendlyDialog(getErrorMessage(problem, t('failed-save', 'Failed to save.')));
      });
    });

    refs.plannedStartInput?.addEventListener('change', () => {
      if (!options.canEdit) return;
      saveCardDetails().catch(problem => {
        showFriendlyDialog(getErrorMessage(problem, t('failed-save', 'Failed to save.')));
      });
    });

    refs.recurringSwitch?.addEventListener('change', () => {
      syncRecurrenceVisibility();
      if (!options.canEdit) return;
      const wasOn = refs.recurringSwitch?.checked;
      if (wasOn) {
        if (!refs.recurrenceIntervalInput?.value) refs.recurrenceIntervalInput.value = '1';
        if (!refs.recurrenceUnitInput?.value || refs.recurrenceUnitInput.value === '0') refs.recurrenceUnitInput.value = '1';
      }
      saveCardDetails().then(() => showSavedToast()).catch(problem => {
        showFriendlyDialog(getErrorMessage(problem, t('failed-save', 'Failed to save.')));
        // Server rejected — roll back the switch and hide fields
        if (refs.recurringSwitch) refs.recurringSwitch.checked = !wasOn;
        syncRecurrenceVisibility();
      });
    });

    refs.recurrenceIntervalInput?.addEventListener('change', () => {
      if (!options.canEdit) return;
      saveCardDetails().then(() => showSavedToast()).catch(problem => {
        showFriendlyDialog(getErrorMessage(problem, t('failed-save', 'Failed to save.')));
      });
    });

    refs.recurrenceUnitInput?.addEventListener('change', () => {
      if (!options.canEdit) return;
      saveCardDetails().then(() => showSavedToast()).catch(problem => {
        showFriendlyDialog(getErrorMessage(problem, t('failed-save', 'Failed to save.')));
      });
    });

    refs.btnEditAssignee?.addEventListener('click', () => {
      openAssigneeEditor();
    });

    refs.overviewAssigneeSearch?.addEventListener('input', () => {
      if (!options.canEdit) return;
      window.clearTimeout(state.assigneeSearchTimer);
      state.assigneeSearchTimer = window.setTimeout(() => {
        searchAssignees(refs.overviewAssigneeSearch?.value ?? '').catch(console.error);
      }, 250);
    });

    refs.overviewAssigneeDropdown?.addEventListener('click', event => {
      const button = (event.target as HTMLElement).closest<HTMLElement>('button[data-user-id]');
      if (!button?.dataset.userId) return;

      assignCard(button.dataset.userId, button.dataset.userName ?? '', button.dataset.userInitial ?? '').catch(problem => {
        showFriendlyDialog(getErrorMessage(problem, t('failed-save', 'Failed to save.')));
      });
    });

    document.addEventListener('click', event => {
      if (!refs.overviewAssigneeDropdown || !refs.overviewAssigneeSearch || !refs.overviewAssigneeEdit) return;

      const target = event.target as Node;
      // Don't close if clicking the edit button (it toggles the editor)
      if (refs.btnEditAssignee?.contains(target)) return;
      if (!refs.overviewAssigneeDropdown.contains(target)
        && !refs.overviewAssigneeSearch.contains(target)
        && !refs.overviewAssigneeEdit.contains(target)) {
        closeAssigneeEditor();
      }
    });

    refs.overviewAssigneeSearch?.addEventListener('keydown', event => {
      if (event.key === 'Escape') {
        closeAssigneeEditor();
      }
    });

    refs.btnOverviewClearAssignee?.addEventListener('click', () => {
      assignCard('', '', '').catch(problem => {
        showFriendlyDialog(getErrorMessage(problem, t('failed-save', 'Failed to save.')));
      });
    });

    refs.labelInput?.addEventListener('focus', () => {
      refreshLabelSuggestions(refs.labelInput?.value ?? '').catch(console.error);
    });

    refs.labelInput?.addEventListener('input', () => {
      refreshLabelSuggestions(refs.labelInput?.value ?? '').catch(console.error);
    });

    refs.labelInput?.addEventListener('keydown', event => {
      if (event.key !== 'Enter') return;
      event.preventDefault();
      addLabel().catch(problem => {
        showFriendlyDialog(getErrorMessage(problem, t('failed-add-label', 'Failed to add label.')));
      });
    });

    refs.addLabelButton?.addEventListener('click', () => {
      addLabel().catch(problem => {
        showFriendlyDialog(getErrorMessage(problem, t('failed-add-label', 'Failed to add label.')));
      });
    });

    refs.labelsDisplay?.addEventListener('click', event => {
      const button = (event.target as HTMLElement).closest<HTMLButtonElement>('.btn-remove-label, .btn-remove-label-chip');
      if (!button?.dataset.labelId) return;

      removeLabel(parseInt(button.dataset.labelId, 10)).catch(problem => {
        showFriendlyDialog(getErrorMessage(problem, t('failed-remove-label', 'Failed to remove label.')));
      });
    });

    refs.moveCardButton?.addEventListener('click', () => {
      moveCard().catch(problem => {
        showFriendlyDialog(getErrorMessage(problem, t('failed-move', 'Failed to move card.')));
      });
    });

    refs.transferTargetBoard?.addEventListener('focus', () => {
      if (state.transferTargets.length > 0) return;
      loadTransferTargets().catch(console.error);
    });

    refs.transferTargetBoard?.addEventListener('change', () => {
      syncTransferColumns();
    });

    refs.transferCardButton?.addEventListener('click', () => {
      transferCard().catch(problem => {
        showFriendlyDialog(getErrorMessage(problem, t('failed-transfer', 'Failed to transfer card.')));
      });
    });

    refs.deleteCardButton?.addEventListener('click', () => {
      showModal(refs.deleteCardModal);
    });

    refs.confirmDeleteCardButton?.addEventListener('click', () => {
      deleteCard().catch(problem => {
        showFriendlyDialog(getErrorMessage(problem, t('failed-delete-card', 'Failed to delete card.')));
      });
    });

    refs.addCommentButton?.addEventListener('click', () => {
      addComment().catch(problem => {
        showFriendlyDialog(getErrorMessage(problem, t('failed-add-comment', 'Failed to add comment.')));
      });
    });

    refs.commentInput?.addEventListener('focus', () => {
      refs.commentSectionHint?.classList.remove('d-none');
      refs.commentDragHint?.classList.remove('d-none');
    });

    refs.commentInput?.addEventListener('blur', () => {
      if (!refs.commentInput?.value.trim()) {
        refs.commentSectionHint?.classList.add('d-none');
        refs.commentDragHint?.classList.add('d-none');
      }
    });

    refs.commentInput?.addEventListener('keydown', event => {
      if ((event.ctrlKey || event.metaKey) && event.key === 'Enter') {
        event.preventDefault();
        addComment().catch(problem => {
          showFriendlyDialog(getErrorMessage(problem, t('failed-add-comment', 'Failed to add comment.')));
        });
      }
    });

    refs.commentsList?.addEventListener('click', event => {
      const deleteButton = (event.target as HTMLElement).closest<HTMLElement>('.comment-delete-btn');
      if (deleteButton?.dataset.commentId) {
        state.commentIdToDelete = parseInt(deleteButton.dataset.commentId, 10);
        clearInlineAlert(refs.deleteCommentError);
        showModal(refs.deleteCommentModal);
        return;
      }

      const image = (event.target as HTMLElement).closest<HTMLImageElement>('img[data-fullscreen-src]');
      if (image) {
        openImageOverlay(image.dataset.fullscreenSrc ?? image.src);
      }
    });

    refs.confirmDeleteCommentButton?.addEventListener('click', () => {
      deleteComment().catch(problem => {
        showInlineAlert(refs.deleteCommentError, getErrorMessage(problem, t('failed-delete-comment', 'Failed to delete comment.')));
      });
    });

    refs.imageOverlay?.addEventListener('click', () => {
      closeImageOverlay();
    });

    document.addEventListener('keydown', event => {
      if (event.key === 'Escape' && refs.imageOverlay?.classList.contains('active')) {
        closeImageOverlay();
      }
    });
  }

  function renderMainDescription(): void {
    const text = getDescriptionValue();
    if (!refs.descriptionPreview || !refs.descriptionEmpty) return;

    if (!text.trim()) {
      refs.descriptionPreview.innerHTML = '';
      refs.descriptionPreview.classList.add('d-none');
      refs.descriptionEmpty.classList.remove('d-none');
      return;
    }

    refs.descriptionEmpty.classList.add('d-none');
    refs.descriptionPreview.classList.remove('d-none');
    renderDescriptionPreview(text, refs.descriptionPreview);
  }

  function renderLiveDescriptionPreview(): void {
    if (!refs.descriptionLivePreview) return;
    if (markdownEditorController) {
      void markdownEditorController.refreshPreview();
      return;
    }
    renderDescriptionPreview(getDescriptionValue(), refs.descriptionLivePreview);
  }

  function toggleDescriptionEdit(editing: boolean): void {
    refs.descriptionView?.classList.toggle('d-none', editing);
    refs.descriptionEdit?.classList.toggle('d-none', !editing);
    refs.editDescriptionButton?.classList.toggle('d-none', editing);

    if (editing && monacoEditor) {
      monacoEditor.layout();
      monacoEditor.focus();
    }
  }

  async function saveInlineTitle(): Promise<void> {
    if (!refs.titleInput || !refs.titleDisplay) return;

    const nextTitle = refs.titleInput.value.trim();
    if (!nextTitle) {
      refs.titleInput.value = state.currentTitle;
      resetTitleEditor();
      return;
    }

    refs.titleInput.value = nextTitle;
    await saveCardDetails();
    resetTitleEditor();
  }

  function resetTitleEditor(): void {
    if (!refs.titleInput || !refs.titleDisplay) return;

    refs.titleInput.value = state.currentTitle;
    refs.titleInput.classList.add('d-none');
    refs.titleDisplay.classList.remove('d-none');
    refs.titleMeta?.classList.remove('d-none');
  }

  async function saveCardDetails(): Promise<void> {
    const title = refs.titleInput && !refs.titleInput.classList.contains('d-none')
      ? refs.titleInput.value.trim()
      : state.currentTitle;

    if (!title) {
      throw new Error(t('title', 'Title'));
    }

    const recurrenceEnabled = refs.recurringSwitch?.checked ?? false;
    const recurrenceInterval = recurrenceEnabled ? (refs.recurrenceIntervalInput?.value ?? '') : '';
    const recurrenceUnit = recurrenceEnabled ? (refs.recurrenceUnitInput?.value ?? '0') : '0';

    const response = await postForm('/Kanban/UpdateCardDetails', {
      cardId: options.cardId,
      title,
      description: getDescriptionValue(),
      plannedStartTime: refs.plannedStartInput?.value ?? '',
      dueDate: refs.dueDateInput?.value ?? '',
      priority: state.currentPriority,
      assignedUserId: state.currentAssigneeId,
      recurrenceInterval,
      recurrenceUnit,
    }, options.csrfToken);

    const result = await readJsonOrThrow<Record<string, unknown>>(response);
    state.currentTitle = readString(result.Title);
    if (refs.titleDisplay) refs.titleDisplay.textContent = state.currentTitle;
    if (refs.titleInput) refs.titleInput.value = state.currentTitle;
    document.title = state.currentTitle;
    renderMainDescription();
  }

  async function updatePriority(priority: number): Promise<void> {
    const response = await postForm('/Kanban/UpdateCardPriority', {
      cardId: options.cardId,
      priority,
    }, options.csrfToken);
    await ensureOk(response);

    state.currentPriority = priority;
    refs.priorityGroup?.querySelectorAll<HTMLElement>('.priority-badge').forEach((badge, index) => {
      badge.className = `priority-badge${options.canEdit ? ' priority-selectable' : ''}`;
      if (index === priority) {
        const activeClass = ['priority-urgent', 'priority-high', 'priority-medium', 'priority-low', ''][index];
        if (activeClass) badge.classList.add(activeClass);
      } else {
        badge.classList.add('text-muted');
      }
    });
  }

  async function searchAssignees(query: string): Promise<void> {
    if (!refs.overviewAssigneeDropdown || !refs.overviewAssigneeSearch) return;

    const normalized = query.trim().toLowerCase();
    if (!normalized) {
      refs.overviewAssigneeDropdown.classList.add('d-none');
      refs.overviewAssigneeDropdown.innerHTML = '';
      return;
    }

    const response = await fetch(`/Kanban/GetBoardMembers?boardId=${options.boardId}`);
    const members = await readJsonOrThrow<BoardMemberDto[]>(response);
    const filtered = members.filter(member =>
      (member.DisplayName ?? '').toLowerCase().includes(normalized)
      || (member.UserName ?? '').toLowerCase().includes(normalized));

    refs.overviewAssigneeDropdown.innerHTML = filtered.map(member => {
      const displayName = member.DisplayName || member.UserName || '';
      return `<button type="button" class="list-group-item list-group-item-action py-2 px-3" data-user-id="${escapeHtml(member.Id)}" data-user-name="${escapeHtml(displayName)}" data-user-initial="${escapeHtml(member.Initial ?? displayName.slice(0, 1).toUpperCase())}">${escapeHtml(displayName)}</button>`;
    }).join('');
    refs.overviewAssigneeDropdown.classList.toggle('d-none', filtered.length === 0);
  }

  async function assignCard(userId: string, displayName: string, initial: string): Promise<void> {
    const response = await postForm('/Kanban/AssignCard', {
      cardId: options.cardId,
      assignedUserId: userId,
    }, options.csrfToken);
    const result = await readJsonOrThrow<Record<string, unknown>>(response);

    state.currentAssigneeId = readOptionalString(result.AssignedUserId) ?? '';
    state.currentAssigneeName = readOptionalString(result.AssignedUserName) ?? displayName;
    state.currentAssigneeInitial = readOptionalString(result.AssignedUserInitial) ?? initial;
    state.currentAssigneeAvatarUrl = readOptionalString(result.AssignedUserAvatarUrl) ?? '';
    renderAssigneeSummary();
    closeAssigneeEditor();
  }

  function renderAssigneeSummary(): void {
    const unassignedText = escapeHtml(t('unassigned', 'Unassigned'));
    const hasAssignee = !!state.currentAssigneeId;

    // Update hero meta chip
    if (refs.heroAssigneeChip) {
      const textContent = hasAssignee ? escapeHtml(state.currentAssigneeName) : unassignedText;
      // Preserve the icon element, replace everything after it
      const iconEl = refs.heroAssigneeChip.querySelector('i');
      refs.heroAssigneeChip.textContent = '';
      if (iconEl) refs.heroAssigneeChip.appendChild(iconEl);
      refs.heroAssigneeChip.appendChild(document.createTextNode(' ' + textContent));
    }

    // Update overview assignee row
    if (refs.overviewAssignee) {
      const editBtnHtml = options.canEdit
        ? `<button type="button" class="btn btn-sm btn-outline-secondary ms-1" id="btnEditAssignee" title="${escapeHtml(t('edit', 'Edit'))}" style="border-radius:12px;flex-shrink:0"><i class="align-middle" data-lucide="pencil" style="width:14px;height:14px"></i></button>`
        : '';

      if (hasAssignee) {
        const avatar = state.currentAssigneeAvatarUrl
          ? `<img src="${escapeHtml(state.currentAssigneeAvatarUrl)}" alt="${escapeHtml(state.currentAssigneeName)}" class="card-assignee-avatar-image" />`
          : escapeHtml(state.currentAssigneeInitial || state.currentAssigneeName.slice(0, 1).toUpperCase());
        refs.overviewAssignee.innerHTML = `
          <span class="card-assignee-avatar">${avatar}</span>
          <div class="detail-avatar-copy">
            <div class="title">${escapeHtml(state.currentAssigneeName)}</div>
          </div>
          ${editBtnHtml}`;
      } else {
        refs.overviewAssignee.innerHTML = `
          <span class="text-muted">${unassignedText}</span>
          ${editBtnHtml}`;
      }

      // Re-bind the edit button after re-render and update the ref so the
      // document click guard below knows which element is the current button.
      const newEditBtn = refs.overviewAssignee.querySelector<HTMLButtonElement>('#btnEditAssignee');
      refs.btnEditAssignee = newEditBtn;
      if (newEditBtn && options.canEdit) {
        newEditBtn.addEventListener('click', () => {
          openAssigneeEditor();
        });
      }

      refreshIcons(refs.overviewAssignee);
    }
  }

  function openAssigneeEditor(): void {
    if (!options.canEdit) return;
    refs.overviewAssigneeView?.classList.add('d-none');
    refs.overviewAssigneeEdit?.classList.remove('d-none');
    refs.overviewAssigneeSearch?.focus();
  }

  function closeAssigneeEditor(): void {
    refs.overviewAssigneeView?.classList.remove('d-none');
    refs.overviewAssigneeEdit?.classList.add('d-none');
    if (refs.overviewAssigneeSearch) refs.overviewAssigneeSearch.value = '';
    refs.overviewAssigneeDropdown?.classList.add('d-none');
    refs.overviewAssigneeDropdown && (refs.overviewAssigneeDropdown.innerHTML = '');
  }

  function renderLabels(): void {
    if (!refs.labelsDisplay) return;

    if (state.currentLabels.length === 0) {
      refs.labelsDisplay.innerHTML = `<span class="text-muted">${escapeHtml(t('none', 'None'))}</span>`;
      return;
    }

    refs.labelsDisplay.innerHTML = state.currentLabels.map(label => `
      <span class="edit-label-chip" data-label-id="${label.id}">
        <span class="card-label-chip" style="background-color:${escapeHtml(label.color)}22;border-color:${escapeHtml(label.color)};color:${escapeHtml(label.color)};">
          ${escapeHtml(label.name)}
        </span>
        ${options.canEdit ? `
          <button type="button" class="btn-remove-label" data-label-id="${label.id}" aria-label="${escapeHtml(t('delete', 'Delete'))}">&times;</button>` : ''}
      </span>`).join('');
  }

  async function refreshLabelSuggestions(query: string): Promise<void> {
    if (!refs.labelSuggestions) return;

    const requestId = ++state.labelSearchRequestId;
    const response = await fetch(`/Kanban/SearchLabels?q=${encodeURIComponent(query.trim())}`);
    const results = await readJsonOrThrow<LabelSearchResult[]>(response);
    if (requestId !== state.labelSearchRequestId) return;

    refs.labelSuggestions.innerHTML = results
      .map(label => `<option value="${escapeHtml(label.Name)}"></option>`)
      .join('');
  }

  async function addLabel(): Promise<void> {
    const name = refs.labelInput?.value.trim() ?? '';
    if (!name) return;

    const response = await postForm('/Kanban/AddLabel', {
      cardId: options.cardId,
      name,
    }, options.csrfToken);
    const result = await readJsonOrThrow<LabelSearchResult>(response);

    state.currentLabels = upsertLabel(state.currentLabels, mapLabel(result));
    renderLabels();
    refs.labelInput!.value = '';
  }

  async function removeLabel(labelId: number): Promise<void> {
    await ensureOk(postForm('/Kanban/RemoveLabel', {
      cardId: options.cardId,
      labelId,
    }, options.csrfToken));

    state.currentLabels = state.currentLabels.filter(label => label.id !== labelId);
    renderLabels();
  }

  async function moveCard(): Promise<void> {
    const targetColumnId = parseInt(refs.moveTargetColumn?.value ?? '0', 10);
    if (!targetColumnId) return;

    const response = await postForm('/Kanban/MoveCard', {
      cardId: options.cardId,
      targetColumnId,
      newOrder: 0,
    }, options.csrfToken);
    const result = await readJsonOrThrow<Record<string, unknown>>(response);
    const targetName = refs.moveTargetColumn?.selectedOptions[0]?.textContent?.trim() ?? '';
    document.querySelectorAll<HTMLElement>('[data-current-column-name]').forEach(node => {
      node.textContent = targetName;
    });

    if (readBoolean(result.RecurrenceApplied)) {
      const recurrenceTarget = readOptionalString(result.RecurrenceTargetColumnName) ?? targetName;
      showFriendlyDialog(t('recurring-task-returned', 'Recurring task has been returned to {0}.').replace('{0}', recurrenceTarget));
    }
  }

  async function loadTransferTargets(): Promise<void> {
    const response = await fetch(`/Kanban/GetTransferTargets?cardId=${options.cardId}`);
    state.transferTargets = await readJsonOrThrow<TransferTargetDto[]>(response);
    syncTransferBoardOptions();
    syncTransferColumns();
  }

  function syncTransferBoardOptions(): void {
    if (!refs.transferTargetBoard) return;

    const selectedValue = refs.transferTargetBoard.value;
    refs.transferTargetBoard.innerHTML = `<option value="">${escapeHtml(t('select-board', 'Select board...'))}</option>`;

    state.transferTargets.forEach(board => {
      const option = document.createElement('option');
      option.value = String(board.Id);
      option.textContent = board.Name;
      refs.transferTargetBoard?.appendChild(option);
    });

    // Restore selection if the previously selected board is still available
    if (selectedValue && state.transferTargets.some(b => String(b.Id) === selectedValue)) {
      refs.transferTargetBoard.value = selectedValue;
    }
  }

  function syncTransferColumns(): void {
    if (!refs.transferTargetBoard || !refs.transferTargetColumn) return;

    const boardId = parseInt(refs.transferTargetBoard.value, 10);
    const board = state.transferTargets.find(item => item.Id === boardId);
    refs.transferTargetColumn.innerHTML = `<option value="">${escapeHtml(t('select-column', 'Select column...'))}</option>`;

    if (!board) {
      refs.transferTargetColumn.disabled = true;
      return;
    }

    board.Columns.forEach(column => {
      const option = document.createElement('option');
      option.value = String(column.Id);
      option.textContent = column.Name;
      refs.transferTargetColumn?.appendChild(option);
    });
    refs.transferTargetColumn.disabled = false;
  }

  async function transferCard(): Promise<void> {
    if (state.transferTargets.length === 0) {
      await loadTransferTargets();
    }

    const targetBoardId = parseInt(refs.transferTargetBoard?.value ?? '0', 10);
    const targetColumnId = parseInt(refs.transferTargetColumn?.value ?? '0', 10);
    if (!targetBoardId || !targetColumnId) return;

    await ensureOk(postForm('/Kanban/TransferCard', {
      cardId: options.cardId,
      targetBoardId,
      targetColumnId,
    }, options.csrfToken));

    window.location.href = `/Cards/${options.cardId}?returnBoardId=${targetBoardId}`;
  }

  async function deleteCard(): Promise<void> {
    refs.confirmDeleteCardButton && (refs.confirmDeleteCardButton.disabled = true);
    try {
      await ensureOk(postForm('/Kanban/DeleteCard', { cardId: options.cardId }, options.csrfToken));
      window.location.href = options.returnBoardUrl;
    } finally {
      if (refs.confirmDeleteCardButton) {
        refs.confirmDeleteCardButton.disabled = false;
      }
    }
  }

  async function loadComments(): Promise<void> {
    if (!refs.commentsList) return;

    const response = await fetch(`/Kanban/GetComments?cardId=${options.cardId}`);
    const comments = await readJsonOrThrow<CommentDto[]>(response);
    refs.commentCount && (refs.commentCount.textContent = String(comments.length));

    if (comments.length === 0) {
      refs.commentsList.innerHTML = `<p class="comment-empty-hint">${escapeHtml(t('no-comments-yet', 'No comments yet.'))}</p>`;
      return;
    }

    refs.commentsList.innerHTML = comments.map(renderCommentHtml).join('');
    refs.commentsList.classList.add('markdown-content');
    void window.AiursoftMarkdownUi?.initializeMarkdownReader({ container: refs.commentsList });
    refreshIcons(refs.commentsList);
  }

  async function addComment(): Promise<void> {
    if (!refs.commentInput || !refs.addCommentButton) return;

    const content = refs.commentInput.value.trim();
    if (!content) return;
    if (content.length > 2000) {
      throw new Error(t('comment-too-long', 'Comment is too long (max 2000 characters).'));
    }

    refs.addCommentButton.disabled = true;
    try {
      let images = '';
      const files = commentDropzone?.getFiles() ?? [];
      if (files.length > 0) {
        refs.addCommentButton.textContent = t('uploading-images', 'Uploading images...');
        const uploads = await Promise.all(files.map(file => uploadImageToServer(file, options.imageUploadUrl)));
        images = uploads.map(readUploadedImageUrl).join(';');
      }

      refs.addCommentButton.textContent = t('sending', 'Sending...');
      await ensureOk(postForm('/Kanban/AddComment', {
        cardId: options.cardId,
        content,
        images,
      }, options.csrfToken));

      refs.commentInput.value = '';
      commentDropzone?.clearFiles();
      await loadComments();
    } finally {
      refs.addCommentButton.disabled = false;
      refs.addCommentButton.textContent = t('send', 'Send');
    }
  }

  async function deleteComment(): Promise<void> {
    if (!state.commentIdToDelete || !refs.confirmDeleteCommentButton) return;

    refs.confirmDeleteCommentButton.disabled = true;
    try {
      await ensureOk(postForm('/Kanban/DeleteComment', {
        commentId: state.commentIdToDelete,
      }, options.csrfToken));
      hideModal(refs.deleteCommentModal);
      state.commentIdToDelete = 0;
      await loadComments();
    } finally {
      refs.confirmDeleteCommentButton.disabled = false;
    }
  }

  function syncRecurrenceVisibility(): void {
    const visible = !!refs.recurringSwitch?.checked;
    refs.recurrenceFields?.classList.toggle('d-none', !visible);
  }

  function openImageOverlay(url: string): void {
    if (!refs.imageOverlay || !refs.imageOverlayImage || !url) return;
    refs.imageOverlayImage.src = url;
    refs.imageOverlay.classList.add('active');
  }

  function closeImageOverlay(): void {
    refs.imageOverlay?.classList.remove('active');
    if (refs.imageOverlayImage) {
      refs.imageOverlayImage.src = '';
    }
  }
}

function renderCommentHtml(comment: CommentDto): string {
  const images = (comment.Images ?? '')
    .split(';')
    .map(image => image.trim())
    .filter(Boolean);

  const avatar = comment.Avatar
    ? `<img src="${escapeHtml(comment.Avatar)}" class="card-assignee-avatar-image" alt="${escapeHtml(comment.AuthorName ?? '')}">`
    : escapeHtml(comment.AuthorInitial ?? '');

  const imagesHtml = images.length > 0
    ? `<div class="comment-images mt-2 d-flex gap-2 flex-wrap">
         ${images.map(image => `<img src="${escapeHtml(image)}" data-fullscreen-src="${escapeHtml(image)}" class="comment-image-thumb" style="height:72px;max-width:120px;object-fit:cover;border-radius:10px;cursor:pointer;border:1px solid var(--bs-border-color, #dee2e6);" alt="comment image">`).join('')}
       </div>`
    : '';

  return `
    <div class="comment-item" data-comment-id="${comment.Id}">
      <div class="comment-avatar">${avatar}</div>
      <div class="comment-body">
        <div class="comment-header">
          <span class="comment-author">${escapeHtml(comment.AuthorName ?? '')}</span>
          <span class="comment-time" title="${escapeHtml(formatCommentFullTime(comment.CreationTime))}">${escapeHtml(formatCommentTime(comment.CreationTime))}</span>
          ${comment.CanDelete ? `<button type="button" class="comment-delete-btn" data-comment-id="${comment.Id}" title="${escapeHtml(t('delete-comment', 'Delete comment'))}"><i class="align-middle" data-lucide="trash-2" style="width:14px;height:14px"></i></button>` : ''}
        </div>
        <div class="comment-text">${renderSafeCommentHtml(comment.Content)}</div>
        ${imagesHtml}
      </div>
    </div>`;
}

function mapLabel(value: LabelSearchResult): CardLabel {
  return {
    id: value.Id,
    name: value.Name,
    color: value.Color,
  };
}

function upsertLabel(labels: CardLabel[], target: CardLabel): CardLabel[] {
  const next = labels.filter(label => label.id !== target.id && label.name.toUpperCase() !== target.name.toUpperCase());
  next.push(target);
  return next.sort((left, right) => left.name.localeCompare(right.name));
}

function renderDescriptionPreview(description: string, container: HTMLElement): boolean {
  if (!description.trim()) {
    container.innerHTML = '';
    return false;
  }

  container.innerHTML = renderSafeMarkdownHtml(description);
  container.classList.add('markdown-content');
  void window.AiursoftMarkdownUi?.initializeMarkdownReader({ container });
  configureRenderedMarkdown(container);
  return true;
}

function configureRenderedMarkdown(container: HTMLElement): void {
  container.querySelectorAll<HTMLImageElement>('img').forEach(image => {
    image.setAttribute('data-fullscreen-src', image.currentSrc || image.src);
  });
}

function renderSafeMarkdownHtml(description: string): string {
  return window.AiursoftMarkdownUi?.renderMarkdown(description, { breaks: true })
    ?? escapeHtml(description).replace(/\n/g, '<br>');
}

function renderSafeCommentHtml(content: string): string {
  return window.AiursoftMarkdownUi?.renderMarkdown(content, { breaks: true })
    ?? escapeHtml(content).replace(/\n/g, '<br>');
}

function setupImageDropzone(element: HTMLTextAreaElement): ImageDropzoneApi {
  const wrapper = document.createElement('div');
  wrapper.className = 'image-dropzone-wrapper';
  wrapper.style.position = 'relative';
  wrapper.style.display = 'flex';
  wrapper.style.flexDirection = 'column';
  wrapper.style.flex = '1';
  wrapper.style.minWidth = '0';

  element.parentNode?.insertBefore(wrapper, element);

  const previewContainer = document.createElement('div');
  previewContainer.className = 'image-dropzone-preview';
  previewContainer.style.display = 'none';
  previewContainer.style.flexWrap = 'wrap';
  previewContainer.style.gap = '0.5rem';
  previewContainer.style.marginBottom = '0.5rem';

  const hintText = document.createElement('div');
  hintText.className = 'image-dropzone-hint';
  hintText.style.fontSize = '0.75rem';
  hintText.style.color = 'var(--bs-secondary-color, #868e96)';
  hintText.style.marginTop = '0.35rem';
  hintText.style.display = 'flex';
  hintText.style.alignItems = 'center';
  hintText.style.gap = '0.3rem';
  hintText.style.userSelect = 'none';
  hintText.innerHTML = `
    <svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
      <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"></path>
      <polyline points="17 8 12 3 7 8"></polyline>
      <line x1="12" y1="3" x2="12" y2="15"></line>
    </svg>
    <span>${escapeHtml(t('drag-or-paste-images', 'You can drag images here or paste from clipboard.'))}</span>`;

  const overlay = document.createElement('div');
  overlay.className = 'image-dropzone-overlay';
  overlay.style.position = 'absolute';
  overlay.style.inset = '0';
  overlay.style.backgroundColor = 'rgba(255, 255, 255, 0.85)';
  overlay.style.border = '2px dashed var(--bs-primary, #4dabf7)';
  overlay.style.borderRadius = '12px';
  overlay.style.display = 'none';
  overlay.style.alignItems = 'center';
  overlay.style.justifyContent = 'center';
  overlay.style.zIndex = '10';
  overlay.style.pointerEvents = 'none';
  overlay.innerHTML = `
    <div style="color:var(--bs-primary, #4dabf7);font-weight:600;display:flex;align-items:center;gap:0.5rem;font-size:0.9rem;">
      <svg xmlns="http://www.w3.org/2000/svg" width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
        <rect x="3" y="3" width="18" height="18" rx="2" ry="2"></rect>
        <circle cx="8.5" cy="8.5" r="1.5"></circle>
        <polyline points="21 15 16 10 5 21"></polyline>
      </svg>
      ${escapeHtml(t('drop-images-here', 'Release to add images'))}
    </div>`;

  wrapper.append(previewContainer, element, hintText, overlay);

  let files: File[] = [];

  const renderPreviews = (): void => {
    previewContainer.innerHTML = '';
    previewContainer.style.display = files.length > 0 ? 'flex' : 'none';

    files.forEach((file, index) => {
      const holder = document.createElement('div');
      holder.style.position = 'relative';
      holder.style.display = 'inline-block';

      const image = document.createElement('img');
      image.src = URL.createObjectURL(file);
      image.style.height = '72px';
      image.style.maxWidth = '120px';
      image.style.objectFit = 'cover';
      image.style.borderRadius = '10px';
      image.style.border = '1px solid var(--bs-border-color, #dee2e6)';

      const removeButton = document.createElement('button');
      removeButton.type = 'button';
      removeButton.innerHTML = '&times;';
      removeButton.style.position = 'absolute';
      removeButton.style.top = '-6px';
      removeButton.style.right = '-6px';
      removeButton.style.background = 'var(--bs-danger, #dc3545)';
      removeButton.style.color = '#fff';
      removeButton.style.border = 'none';
      removeButton.style.borderRadius = '50%';
      removeButton.style.width = '20px';
      removeButton.style.height = '20px';
      removeButton.style.display = 'flex';
      removeButton.style.alignItems = 'center';
      removeButton.style.justifyContent = 'center';
      removeButton.style.padding = '0';
      removeButton.addEventListener('click', event => {
        event.preventDefault();
        URL.revokeObjectURL(image.src);
        files.splice(index, 1);
        renderPreviews();
      });

      holder.append(image, removeButton);
      previewContainer.appendChild(holder);
    });
  };

  const addFiles = (incoming: FileList | File[]): void => {
    Array.from(incoming).forEach(file => {
      if (file.type.startsWith('image/')) {
        files.push(file);
      }
    });
    renderPreviews();
  };

  let dragCounter = 0;
  wrapper.addEventListener('dragenter', event => {
    event.preventDefault();
    dragCounter += 1;
    overlay.style.display = 'flex';
  });
  wrapper.addEventListener('dragover', event => {
    event.preventDefault();
  });
  wrapper.addEventListener('dragleave', event => {
    event.preventDefault();
    dragCounter -= 1;
    if (dragCounter <= 0) {
      dragCounter = 0;
      overlay.style.display = 'none';
    }
  });
  wrapper.addEventListener('drop', event => {
    event.preventDefault();
    dragCounter = 0;
    overlay.style.display = 'none';
    if (event.dataTransfer?.files?.length) {
      addFiles(event.dataTransfer.files);
    }
  });
  element.addEventListener('paste', event => {
    const pastedFiles = Array.from(event.clipboardData?.items ?? [])
      .filter(item => item.type.startsWith('image/'))
      .map(item => item.getAsFile())
      .filter((file): file is File => !!file);
    if (pastedFiles.length > 0) {
      addFiles(pastedFiles);
    }
  });

  return {
    getFiles: () => [...files],
    clearFiles: () => {
      files = [];
      renderPreviews();
    },
  };
}

async function uploadImageToServer(blob: Blob, uploadUrl: string): Promise<Record<string, unknown>> {
  const formData = new FormData();
  const ext = blob.type === 'image/png'
    ? 'png'
    : blob.type === 'image/gif'
      ? 'gif'
      : blob.type === 'image/webp'
        ? 'webp'
        : 'jpg';
  formData.append('file', blob, `upload-${Date.now()}.${ext}`);

  const response = await fetch(uploadUrl, {
    method: 'POST',
    body: formData,
  });
  return readJsonOrThrow<Record<string, unknown>>(response);
}

function readUploadedImageUrl(payload: Record<string, unknown>): string {
  const url = readOptionalString(payload.InternetPath ?? payload.internetPath);
  if (!url) {
    throw new Error(t('failed-upload-image', 'Failed to upload image.'));
  }
  return url;
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

function formatCommentFullTime(value: string): string {
  return parseCommentDate(value)?.toLocaleString() ?? value;
}

function formatCommentTime(value: string): string {
  const parsed = parseCommentDate(value);
  if (!parsed) return value;

  const diffMinutes = Math.floor((Date.now() - parsed.getTime()) / 60000);
  if (diffMinutes < 1) return t('just-now', 'just now');
  if (diffMinutes < 60) return `${diffMinutes}${t('m-ago', 'm ago')}`;

  const diffHours = Math.floor(diffMinutes / 60);
  if (diffHours < 24) return `${diffHours}${t('h-ago', 'h ago')}`;

  const diffDays = Math.floor(diffHours / 24);
  if (diffDays < 7) return `${diffDays}${t('d-ago', 'd ago')}`;

  return parsed.toLocaleString();
}

function parseCommentDate(value: string): Date | null {
  const parsed = /Z|[+-]\d{2}:?\d{2}$/.test(value) ? new Date(value) : new Date(`${value}Z`);
  return Number.isNaN(parsed.getTime()) ? null : parsed;
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

function showSavedToast(): void {
    const existing = document.querySelector('.saved-toast');
    if (existing) existing.remove();

    const toast = document.createElement('div');
    toast.className = 'saved-toast';
    toast.innerHTML = `<i data-lucide="check-circle" style="width:18px;height:18px;color:#22c55e"></i>${escapeHtml(t('saved', 'Saved'))}`;
    Object.assign(toast.style, {
      position: 'fixed',
      top: '1rem',
      left: '50%',
      transform: 'translateX(-50%)',
      background: 'var(--bs-body-bg, #fff)',
      color: 'var(--bs-body-color)',
      padding: '0.6rem 1.2rem',
      borderRadius: '14px',
      fontSize: '0.85rem',
      fontWeight: '600',
      zIndex: '9999',
      display: 'flex',
      alignItems: 'center',
      gap: '0.5rem',
      boxShadow: '0 6px 24px rgba(15,23,42,0.12)',
      border: '1px solid rgba(148,163,184,0.18)',
      transition: 'opacity 0.3s ease, transform 0.3s ease',
    });

    document.body.appendChild(toast);
    window.lucide?.createIcons?.({ nodes: [toast] });

    setTimeout(() => {
      toast.style.opacity = '0';
      toast.style.transform = 'translateX(-50%) translateY(-8px)';
      setTimeout(() => toast.remove(), 300);
    }, 1500);
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
