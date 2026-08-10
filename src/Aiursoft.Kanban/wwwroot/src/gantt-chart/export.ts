import { toPng, toSvg } from 'html-to-image';
import type { GanttMode } from './types';

const modeLabel: Record<GanttMode, string> = {
    default: 'overview',
    planned: 'planned',
    actual: 'actual',
};

/**
 * Gantt wrapper is an overflow:auto scroll container, so a direct snapshot would
 * only capture the visible viewport. Clone its content into an offscreen,
 * full-size container to export the entire chart regardless of scrolling.
 */
function buildOffscreenClone(source: HTMLElement): {
    container: HTMLElement;
    cleanup: () => void;
} {
    const clone = source.cloneNode(true) as HTMLElement;
    clone.style.overflow = 'visible';
    clone.style.width = `${source.scrollWidth}px`;
    clone.style.height = `${source.scrollHeight}px`;
    clone.style.padding = getComputedStyle(source).padding;

    const container = document.createElement('div');
    // Keep the node at a valid on-screen position (0,0) so html-to-image can
    // compute a correct bounding box; pushing it behind the page (negative
    // z-index) hides it visually during capture without moving it off-screen,
    // which would render a blank/transparent image.
    container.style.position = 'fixed';
    container.style.left = '0';
    container.style.top = '0';
    container.style.zIndex = '-99999';
    container.style.pointerEvents = 'none';
    container.style.width = `${source.scrollWidth}px`;
    container.style.height = `${source.scrollHeight}px`;
    container.style.background = getComputedStyle(source).backgroundColor || '#ffffff';
    container.appendChild(clone);
    document.body.appendChild(container);

    return {
        container,
        cleanup: () => container.remove(),
    };
}

function triggerDownload(dataUrl: string, filename: string): void {
    const link = document.createElement('a');
    link.download = filename;
    link.href = dataUrl;
    link.click();
}

function buildFilename(boardName: string, mode: GanttMode): string {
    const safeName = boardName.replace(/[^\w一-龥-]+/g, '_').replace(/_+/g, '_') || 'kanban';
    const stamp = new Date().toISOString().slice(0, 10);
    return `gantt-${safeName}-${modeLabel[mode]}-${stamp}.png`;
}

export async function exportGanttAsPng(
    boardName: string,
    mode: GanttMode,
    source: HTMLElement,
): Promise<void> {
    const { container, cleanup } = buildOffscreenClone(source);
    try {
        const dataUrl = await toPng(container, {
            pixelRatio: 2,
            cacheBust: true,
            backgroundColor: getComputedStyle(source).backgroundColor || '#ffffff',
        });
        triggerDownload(dataUrl, buildFilename(boardName, mode));
    } finally {
        cleanup();
    }
}

export async function exportGanttAsSvg(
    boardName: string,
    mode: GanttMode,
    source: HTMLElement,
): Promise<void> {
    const { container, cleanup } = buildOffscreenClone(source);
    try {
        const dataUrl = await toSvg(container, {
            cacheBust: true,
            backgroundColor: getComputedStyle(source).backgroundColor || '#ffffff',
        });
        const svgName = buildFilename(boardName, mode).replace(/\.png$/, '.svg');
        triggerDownload(dataUrl, svgName);
    } finally {
        cleanup();
    }
}
