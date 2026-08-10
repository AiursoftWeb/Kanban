// ============================================================
// index.ts — GanttChart page entry point
// ============================================================

import type { BoardData } from '../kanban-board/types';
import type { GanttMode, GanttStrings } from './types';
import { renderGantt } from './renderer';
import { exportGanttAsPng } from './export';
import './styles/gantt.css';

interface GanttChartPageOptions {
  boardData: BoardData;
  boardName: string;
}

declare global {
  interface Window {
    lucide?: {
      createIcons(options?: { nodes?: ParentNode[] }): void;
    };
  }
}

/**
 * Read a localization string from the hidden #loc-data div.
 * Falls back to `fallback` if the key is not found.
 */
function t(key: string, fallback: string): string {
  const el = document.querySelector<HTMLElement>(`#gantt-loc-data span[data-key="${key}"]`);
  return el?.textContent?.trim() || fallback;
}

/** Build the full GanttStrings bag from loc-data. */
function loadStrings(): GanttStrings {
  return {
    cards:                t('gantt-cards',                  'Cards'),
    noDatesCards:         t('gantt-no-dates-cards',         'Cards without dates'),
    noBoardCards:         t('gantt-no-board-cards',         'This board has no cards.'),
    noDateCardsForMode:   t('gantt-no-date-cards-for-mode', 'No cards have complete dates in this mode.'),
    timeLabel:            t('gantt-time',                   'Time:'),
    columnLabel:          t('gantt-column',                 'Column:'),
    statusLabel:          t('gantt-status',                 'Status:'),
    assigneeLabel:        t('gantt-assignee',               'Assignee:'),
    priorityLabel:        t('gantt-priority',               'Priority:'),
    statusCompleted:      t('gantt-status-completed',       'Completed'),
    statusInProgress:     t('gantt-status-in-progress',     'In Progress'),
    statusNotStarted:     t('gantt-status-not-started',     'Not Started'),
    missingPlannedBoth:   t('gantt-missing-planned-both',   'Missing planned start and due date'),
    missingPlannedStart:  t('gantt-missing-planned-start',  'Missing planned start date'),
    missingDueDate:       t('gantt-missing-due-date',       'Missing due date'),
    missingActualBoth:    t('gantt-missing-actual-both',    'Missing actual start and end date'),
    missingActualStart:   t('gantt-missing-actual-start',   'Missing actual start date'),
    missingActualEnd:     t('gantt-missing-actual-end',     'Missing actual end date'),
    missingDateFallback:  t('gantt-missing-date-fallback',  'Missing date information (planned or actual dates are incomplete)'),
    noExportableChart:    t('gantt-no-exportable-chart',    'No cards have complete dates to export in this mode.'),
  };
}

export function initGanttChartPage(options: GanttChartPageOptions): void {
  const container = document.getElementById('gantt-root');
  if (!container) return;

  const strings = loadStrings();
  let currentMode: GanttMode = 'default';

  const exportBtn = document.getElementById('gantt-export-btn') as HTMLButtonElement | null;

  // The chart only renders a drawable canvas (.gantt-table) when at least one
  // card has complete dates in the current mode. Otherwise the export would
  // produce a blank image, so disable the button and explain why.
  function updateExportButton(): void {
    if (!exportBtn) return;
    const hasChart = !!container.querySelector('.gantt-table');
    exportBtn.disabled = !hasChart;
    exportBtn.title = hasChart ? '' : strings.noExportableChart;
  }

  function render(): void {
    renderGantt(container, options.boardData, currentMode, strings);
    refreshIcons();
    updateExportButton();
  }

  // Wire up mode toggle buttons
  const modeButtons = document.querySelectorAll<HTMLElement>('[data-gantt-mode]');
  modeButtons.forEach(btn => {
    btn.addEventListener('click', () => {
      currentMode = btn.dataset.ganttMode as GanttMode;
      modeButtons.forEach(b => {
        b.classList.toggle('active', b === btn);
      });
      render();
    });
  });

  // Wire up export button
  if (exportBtn) {
    exportBtn.addEventListener('click', async () => {
      const wrapper = container.querySelector<HTMLElement>('.gantt-table');
      if (!wrapper || exportBtn.disabled) return;

      const originalLabel = exportBtn.innerHTML;
      exportBtn.disabled = true;
      exportBtn.innerHTML = '<i data-lucide="loader-circle"></i> ' + t('gantt-exporting', 'Exporting…');
      refreshIcons(exportBtn);

      try {
        await exportGanttAsPng(options.boardName, currentMode, wrapper);
      } catch (err) {
        console.error('Gantt export failed:', err);
        const msg = err instanceof Error && err.message.includes('No drawable')
          ? strings.noExportableChart
          : t('gantt-export-failed', 'Failed to export the Gantt chart. Please try again.');
        alert(msg);
      } finally {
        updateExportButton();
        exportBtn.innerHTML = originalLabel;
        refreshIcons(exportBtn);
      }
    });
  }

  // Initial render
  render();
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
