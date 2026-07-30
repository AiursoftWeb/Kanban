import { t } from '../kanban-board/i18n';

interface BoardMemberDto {
  Id: string;
  DisplayName?: string;
  UserName?: string;
  Initial?: string;
}

/**
 * Attaches @mention autocomplete to a textarea.
 *
 * Usage:
 *   const mention = new MentionAutocomplete(textarea, boardId);
 *   // When done:
 *   mention.dispose();
 */
export class MentionAutocomplete {
  private readonly textarea: HTMLTextAreaElement;
  private readonly boardId: number;
  private readonly dropdown: HTMLDivElement;

  private active = false;
  private startPos = 0;
  private query = '';
  private members: BoardMemberDto[] = [];
  private selectedIndex = 0;
  private abortController: AbortController | null = null;

  constructor(textarea: HTMLTextAreaElement, boardId: number) {
    this.textarea = textarea;
    this.boardId = boardId;

    this.dropdown = document.createElement('div');
    this.dropdown.className = 'mention-dropdown list-group';
    this.dropdown.style.cssText =
      'display:none;position:absolute;z-index:1050;max-height:200px;' +
      'overflow-y:auto;box-shadow:0 6px 24px rgba(15,23,42,0.12);border-radius:12px;';
    this.dropdown.setAttribute('role', 'listbox');

    const wrapper = textarea.closest('.image-dropzone-wrapper') ?? textarea.parentNode;
    (wrapper as HTMLElement).style.position = 'relative';
    wrapper!.insertBefore(this.dropdown, textarea.nextSibling);

    this.bindEvents();
  }

  /** Remove all event listeners and the dropdown element. */
  dispose(): void {
    this.dropdown.remove();
    this.textarea.removeEventListener('keydown', this.onKeydown);
    this.textarea.removeEventListener('input', this.onInput);
    this.dropdown.removeEventListener('click', this.onDropdownClick);
    document.removeEventListener('click', this.onDocumentClick);
  }

  // ── event binding ──

  private readonly onKeydown = (event: KeyboardEvent): void => {
    if (!this.active || this.dropdown.style.display === 'none') return;

    if (event.key === 'ArrowDown') {
      event.preventDefault();
      this.selectedIndex = Math.min(
        this.selectedIndex + 1,
        this.filteredMembers().length - 1);
      this.renderDropdown();
      return;
    }
    if (event.key === 'ArrowUp') {
      event.preventDefault();
      this.selectedIndex = Math.max(this.selectedIndex - 1, 0);
      this.renderDropdown();
      return;
    }
    if (event.key === 'Enter' || event.key === 'Tab') {
      event.preventDefault();
      const member = this.filteredMembers()[this.selectedIndex];
      if (member) this.select(member);
      return;
    }
    if (event.key === 'Escape') {
      event.preventDefault();
      this.close();
    }
  };

  private readonly onInput = (): void => {
    void this.handleInput();
  };

  private readonly onDropdownClick = (event: Event): void => {
    const button = (event.target as HTMLElement).closest<HTMLElement>('.mention-item');
    if (!button?.dataset.userId) return;
    const member = this.members.find(m => m.Id === button.dataset.userId);
    if (member) this.select(member);
  };

  private readonly onDocumentClick = (event: Event): void => {
    if (!this.active) return;
    const target = event.target as Node;
    if (!this.dropdown.contains(target) && !this.textarea.contains(target)) {
      this.close();
    }
  };

  private bindEvents(): void {
    this.textarea.addEventListener('keydown', this.onKeydown);
    this.textarea.addEventListener('input', this.onInput);
    this.dropdown.addEventListener('click', this.onDropdownClick);
    document.addEventListener('click', this.onDocumentClick);
  }

  // ── data ──

  private async loadMembers(): Promise<BoardMemberDto[]> {
    if (this.members.length > 0) return this.members;

    this.abortController?.abort();
    this.abortController = new AbortController();

    try {
      const response = await fetch(`/Kanban/GetBoardMembers?boardId=${this.boardId}`, {
        signal: this.abortController.signal,
      });
      this.members = await response.json() as BoardMemberDto[];
    } catch {
      // aborted or failed — keep cached members if any
    }
    return this.members;
  }

  private filteredMembers(): BoardMemberDto[] {
    const q = this.query.toLowerCase();
    if (!q) return this.members;
    return this.members.filter(m =>
      (m.DisplayName ?? '').toLowerCase().includes(q) ||
      (m.UserName ?? '').toLowerCase().includes(q));
  }

  // ── input handling ──

  private static wordBeforeCursor(textarea: HTMLTextAreaElement): { word: string; start: number } {
    const pos = textarea.selectionStart;
    const text = textarea.value;
    let start = pos;
    while (start > 0 && !/\s/.test(text[start - 1])) {
      start--;
    }
    return { word: text.substring(start, pos), start };
  }

  private async handleInput(): Promise<void> {
    const { word, start } = MentionAutocomplete.wordBeforeCursor(this.textarea);

    if (word.startsWith('@') && word.length >= 1) {
      this.active = true;
      this.startPos = start;
      this.query = word.substring(1);
      this.selectedIndex = 0;

      await this.loadMembers();
      if (this.filteredMembers().length > 0) {
        this.renderDropdown();
        this.positionDropdown();
      } else {
        this.close();
      }
    } else {
      this.close();
    }
  }

  // ── rendering ──

  private renderDropdown(): void {
    const filtered = this.filteredMembers();
    this.dropdown.innerHTML = filtered.map((member, index) => {
      const displayName = member.DisplayName || member.UserName || '';
      const initial = member.Initial || displayName.slice(0, 1).toUpperCase();
      const activeClass = index === this.selectedIndex ? ' active' : '';
      return `<button type="button" class="list-group-item list-group-item-action py-2 px-3 mention-item${activeClass}" data-user-id="${this.esc(member.Id)}" role="option" aria-selected="${index === this.selectedIndex}">
        <span class="mention-avatar">${this.esc(initial)}</span>
        <span class="mention-name">${this.esc(displayName)}</span>
      </button>`;
    }).join('');
    this.dropdown.style.display = 'block';
  }

  private positionDropdown(): void {
    this.dropdown.style.left = '0';
    this.dropdown.style.right = '0';
    this.dropdown.style.bottom = '100%';
    this.dropdown.style.marginBottom = '4px';
  }

  // ── selection ──

  private select(member: BoardMemberDto): void {
    const textarea = this.textarea;
    const displayName = member.DisplayName || member.UserName || '';
    const before = textarea.value.substring(0, this.startPos);
    const after = textarea.value.substring(textarea.selectionStart);
    textarea.value = before + '@' + displayName + ' ' + after;

    const newPos = before.length + displayName.length + 2;
    textarea.selectionStart = newPos;
    textarea.selectionEnd = newPos;
    textarea.focus();

    this.close();
  }

  private close(): void {
    this.active = false;
    this.query = '';
    this.selectedIndex = 0;
    this.dropdown.style.display = 'none';
    this.dropdown.innerHTML = '';
  }

  private esc(value: string): string {
    const div = document.createElement('div');
    div.textContent = value;
    return div.innerHTML;
  }
}
