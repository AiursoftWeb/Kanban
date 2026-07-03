// ============================================================
// i18n.ts — Read localization strings from #loc-data DOM element
// ============================================================

let cache: Record<string, string> | null = null;

/**
 * Read all localization data from the hidden #loc-data div.
 * Cached after first call.
 */
function loadLocData(): Record<string, string> {
  if (cache) return cache;

  cache = {};
  const container = document.getElementById('loc-data');
  if (!container) return cache;

  const spans = container.querySelectorAll('span[data-key]');
  spans.forEach(span => {
    const key = span.getAttribute('data-key');
    if (key) {
      cache![key] = span.textContent?.trim() ?? '';
    }
  });

  return cache;
}

/**
 * Get a localized string by key.
 *
 * @param key    The data-key attribute value in the #loc-data div
 * @param fallback  Fallback text if the key is not found (defaults to key)
 * @returns The localized string
 */
export function t(key: string, fallback?: string): string {
  const data = loadLocData();
  return data[key] ?? fallback ?? key;
}

/**
 * Clear the localization cache (useful if the page DOM changes).
 */
export function clearLocCache(): void {
  cache = null;
}
