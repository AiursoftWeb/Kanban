import type { BoardData, ColumnData } from './types';
import { DOT_COLORS } from './types';

export type GroupingMode = 'status' | 'category';

export function dotClass(id: number): string {
  return DOT_COLORS[Math.abs(id) % DOT_COLORS.length];
}

export function groupColumns(data: BoardData, mode: GroupingMode): ColumnData[] {
  if (mode === 'status') return data.columns;
  const cards = data.columns.flatMap(column => column.cards);
  return [...(data.categories ?? [])].sort((a, b) => a.order - b.order || a.id - b.id).map(category => ({
    id: category.id,
    name: category.name,
    order: category.order,
    color: '',
    dotClass: dotClass(category.id),
    status: 'NotStarted',
    cards: cards.filter(card => card.categoryId === category.id)
      .sort((a, b) => (a.categoryOrder ?? 0) - (b.categoryOrder ?? 0) || a.id - b.id),
  }));
}
