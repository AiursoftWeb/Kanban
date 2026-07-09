import { defineConfig } from 'vite';
import { resolve } from 'path';

export default defineConfig({
  build: {
    lib: {
      entry: {
        'kanban-board': resolve(__dirname, 'src/kanban-board/index.ts'),
        'kanban-page': resolve(__dirname, 'src/kanban-page/index.ts'),
        'card-detail-page': resolve(__dirname, 'src/card-detail-page/index.ts'),
        'gantt-chart': resolve(__dirname, 'src/gantt-chart/index.ts'),
      },
      formats: ['es'],
      fileName: (_format, entryName) => `${entryName}.js`,
      cssFileName: 'kanban-board',
    },
    outDir: 'dist',
    emptyOutDir: true,
  },
  css: {
    modules: false,
  },
});
