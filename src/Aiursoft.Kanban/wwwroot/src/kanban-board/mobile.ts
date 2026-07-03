// ============================================================
// mobile.ts — Mobile column switcher (prev/next buttons + pill nav)
// ============================================================

export interface MobileSwitcherInstance {
  destroy(): void;
}

/**
 * Initialize the mobile column switcher.
 * Reads existing #mobileColumnSwitcher HTML and wires up prev/next + pill clicks.
 */
export function initMobile(container: HTMLElement): MobileSwitcherInstance {
  const switcher = document.getElementById('mobileColumnSwitcher');
  if (!switcher) return { destroy() {} };

  const prevBtn = document.getElementById('btnMobilePrevColumn');
  const nextBtn = document.getElementById('btnMobileNextColumn');
  const track = document.getElementById('mobileColumnTrack');

  // Populate pills
  if (track) {
    populatePills(track, container);
  }

  // Scroll to column by index
  function scrollToColumn(index: number): void {
    const columns = container.querySelectorAll<HTMLElement>('.kanban-column');
    if (index < 0 || index >= columns.length) return;

    const target = columns[index];
    target.scrollIntoView({ behavior: 'smooth', block: 'nearest', inline: 'center' });

    // Update active pill
    track?.querySelectorAll('.mobile-column-pill').forEach((pill, i) => {
      pill.classList.toggle('active', i === index);
    });

    // Update button disabled states
    if (prevBtn) (prevBtn as HTMLButtonElement).disabled = index === 0;
    if (nextBtn) (nextBtn as HTMLButtonElement).disabled = index === columns.length - 1;
  }

  function getCurrentIndex(): number {
    const columns = Array.from(container.querySelectorAll<HTMLElement>('.kanban-column'));
    if (columns.length === 0) return 0;

    const containerRect = container.getBoundingClientRect();
    const containerCenter = containerRect.left + containerRect.width / 2;

    let closestIdx = 0;
    let closestDist = Infinity;

    columns.forEach((col, i) => {
      const rect = col.getBoundingClientRect();
      const colCenter = rect.left + rect.width / 2;
      const dist = Math.abs(colCenter - containerCenter);
      if (dist < closestDist) {
        closestDist = dist;
        closestIdx = i;
      }
    });

    return closestIdx;
  }

  if (prevBtn) {
    prevBtn.addEventListener('click', () => {
      scrollToColumn(getCurrentIndex() - 1);
    });
  }

  if (nextBtn) {
    nextBtn.addEventListener('click', () => {
      scrollToColumn(getCurrentIndex() + 1);
    });
  }

  if (track) {
    track.addEventListener('click', e => {
      const pill = (e.target as HTMLElement).closest<HTMLElement>('.mobile-column-pill');
      if (!pill) return;

      const index = parseInt(pill.getAttribute('data-column-index') ?? '-1', 10);
      if (index >= 0) scrollToColumn(index);
    });
  }

  // Update pill states on scroll
  let scrollTimer: ReturnType<typeof setTimeout> | null = null;
  container.addEventListener('scroll', () => {
    if (scrollTimer) clearTimeout(scrollTimer);
    scrollTimer = setTimeout(() => {
      const idx = getCurrentIndex();
      track?.querySelectorAll('.mobile-column-pill').forEach((pill, i) => {
        pill.classList.toggle('active', i === idx);
      });
      if (prevBtn) (prevBtn as HTMLButtonElement).disabled = idx === 0;
      const columns = container.querySelectorAll('.kanban-column');
      if (nextBtn) (nextBtn as HTMLButtonElement).disabled = idx >= columns.length - 1;
    }, 100);
  }, { passive: true });

  return {
    destroy() {
      // Event listeners on DOM elements, cleaned up by parent destroy
    },
  };
}

function populatePills(track: HTMLElement, container: HTMLElement): void {
  const columns = container.querySelectorAll<HTMLElement>('.kanban-column');
  track.innerHTML = '';

  columns.forEach((col, i) => {
    const name = col.querySelector<HTMLElement>('.column-title')?.textContent?.trim() ?? `Col ${i + 1}`;
    const pill = document.createElement('button');
    pill.type = 'button';
    pill.className = `mobile-column-pill${i === 0 ? ' active' : ''}`;
    pill.setAttribute('data-column-index', String(i));
    pill.textContent = name;
    track.appendChild(pill);
  });
}
