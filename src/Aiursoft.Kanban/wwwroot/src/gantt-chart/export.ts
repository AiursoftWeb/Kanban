import { toPng, toSvg } from 'html-to-image';
import type { GanttMode } from './types';

const modeLabel: Record<GanttMode, string> = {
    default: 'overview',
    planned: 'planned',
    actual: 'actual',
};

/**
 * Gantt wrapper is an overflow:auto scroll container, so a direct snapshot
 * would only capture the visible viewport. Clone it off-screen and let it
 * shrink to its real content size so small boards do not export with a huge
 * blank area below the chart.
 */
function buildOffscreenClone(source: HTMLElement): {
    node: HTMLElement;
    cleanup: () => void;
} {
    const clone = source.cloneNode(true) as HTMLElement;
    clone.style.overflow = 'visible';
    // "Cards without dates" section is interactive UI, not part of the chart.
    clone.querySelector('.gantt-no-dates-section')?.remove();
    // When cloned, the wrapper is no longer inside the flex page layout, but
    // its flex children (.gantt-wrapper flex:1, .gantt-timeline flex:1,
    // .gantt-body flex:1) still try to grow and get stretched to a large
    // height, leaving a blank area below the chart. Kill flex growth so the
    // clone collapses to its actual content size.
    clone.style.flex = 'none';
    clone.querySelectorAll<HTMLElement>('.gantt-timeline, .gantt-body').forEach(el => {
        el.style.flex = 'none';
    });
    // .gantt-wrapper is flex:1 in the live page and gets stretched to the
    // viewport height. Outside a flex parent that rule is inert, so leaving
    // height auto lets the clone collapse to its actual content height.
    clone.style.height = 'auto';
    clone.style.width = 'max-content';
    clone.style.position = 'fixed';
    clone.style.left = '0';
    clone.style.top = '0';
    clone.style.zIndex = '-99999';
    clone.style.pointerEvents = 'none';
    clone.style.background = 'var(--bs-body-bg, #ffffff)';
    document.body.appendChild(clone);

    return {
        node: clone,
        cleanup: () => clone.remove(),
    };
}

// Upper bound on the exported bitmap's total pixel count. A naive pixelRatio of
// 2 on a large board (e.g. ~280 days × ~180 cards ≈ 16240×16024 px) yields ~260M
// pixels and a ~1GB RGBA buffer, which can hang or OOM browsers on mobile. html-to-image
// only clamps a single side that exceeds 16384, so it never bounds the total area.
const MAX_TOTAL_PIXELS = 64_000_000; // ~8000×8000 at ratio 1
const BASE_PIXEL_RATIO = 2;
// Below this ratio the rasterized chart is too blurry to be useful, so we
// refuse the PNG export and point users to SVG/tiled export instead.
const MIN_ACCEPTABLE_PIXEL_RATIO = 1;

/**
 * Compute the pixelRatio that keeps width·height·ratio² within MAX_TOTAL_PIXELS.
 * Never returns a ratio that would exceed the budget. Throws when the chart is
 * so large that even a 1× ratio would blow the cap, since silently dropping to
 * a sub-1 ratio would both break the cap and produce an unreadable image.
 */
function safePixelRatio(width: number, height: number): number {
    if (width <= 0 || height <= 0) return BASE_PIXEL_RATIO;
    const totalAtBase = width * BASE_PIXEL_RATIO * height * BASE_PIXEL_RATIO;
    if (totalAtBase <= MAX_TOTAL_PIXELS) return BASE_PIXEL_RATIO;
    const ratio = Math.sqrt(MAX_TOTAL_PIXELS / (width * height));
    const clamped = Math.min(BASE_PIXEL_RATIO, Math.floor(ratio * 100) / 100);
    if (clamped < MIN_ACCEPTABLE_PIXEL_RATIO) {
        throw new Error('Chart too large for PNG export.');
    }
    return clamped;
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

// .gantt-wrapper itself has a transparent background; fall back to a solid
// page color so the exported image is never transparent.
function exportBackgroundColor(): string {
    const pageBg = getComputedStyle(document.body).backgroundColor;
    if (pageBg && pageBg !== 'rgba(0, 0, 0, 0)' && pageBg !== 'transparent') {
        return pageBg;
    }
    const wrapperBg = getComputedStyle(document.documentElement).backgroundColor;
    if (wrapperBg && wrapperBg !== 'rgba(0, 0, 0, 0)' && wrapperBg !== 'transparent') {
        return wrapperBg;
    }
    return '#ffffff';
}

export async function exportGanttAsPng(
    boardName: string,
    mode: GanttMode,
    source: HTMLElement,
): Promise<void> {
    // Defense-in-depth: refuse to export if the source contains no actual chart.
    // An empty board or a mode with no dated cards produces only UI chrome
    // (empty-state / no-dates section) and would yield a meaningless image.
    if (!source.querySelector('.gantt-timeline')) {
        throw new Error('No drawable Gantt chart to export.');
    }

    const { node, cleanup } = buildOffscreenClone(source);
    try {
        // Measure the cloned chart's natural size, then clamp pixelRatio so the
        // resulting bitmap never blows past the total-pixel budget.
        const pixelRatio = safePixelRatio(node.offsetWidth, node.offsetHeight);
        const dataUrl = await toPng(node, {
            pixelRatio,
            cacheBust: true,
            backgroundColor: exportBackgroundColor(),
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
    const { node, cleanup } = buildOffscreenClone(source);
    try {
        const dataUrl = await toSvg(node, {
            cacheBust: true,
            backgroundColor: exportBackgroundColor(),
        });
        const svgName = buildFilename(boardName, mode).replace(/\.png$/, '.svg');
        triggerDownload(dataUrl, svgName);
    } finally {
        cleanup();
    }
}
