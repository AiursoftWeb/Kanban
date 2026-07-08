// ============================================================
// scroll-restore.ts — Scroll to and highlight a card on page load
// ============================================================

/**
 * Scroll to a card by ID and apply a highlight animation.
 * Called on initial render when returning from card detail page.
 *
 * @param container  The .kanban-container element
 * @param cardId     The card ID to highlight
 */
export function scrollToCard(container: HTMLElement, cardId: number): void {
  if (!cardId) return;

  // Small delay to let DOM render
  requestAnimationFrame(() => {
    requestAnimationFrame(() => {
      const cardEl = container.querySelector<HTMLElement>(`.kanban-card[data-card-id="${cardId}"]`);
      if (!cardEl) return;

      // Scroll the card into view
      cardEl.scrollIntoView({ behavior: 'smooth', block: 'center' });

      // Highlight animation
      cardEl.classList.add('highlight-flash');

      // Remove highlight after animation completes
      cardEl.addEventListener('animationend', () => {
        cardEl.classList.remove('highlight-flash');
      }, { once: true });

      // Fallback: remove after 3.5s if animationend doesn't fire
      setTimeout(() => {
        cardEl.classList.remove('highlight-flash');
      }, 3500);
    });
  });
}
