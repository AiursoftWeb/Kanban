import { defineConfig } from 'vite';
import { resolve } from 'path';

export default defineConfig({
  build: {
    lib: {
      entry: resolve(__dirname, 'src/kanban-board/index.ts'),
      name: 'KanbanBoard',
      formats: ['es'],
      fileName: () => 'kanban-board.js',
    },
    outDir: 'dist',
    emptyOutDir: true,
    rollupOptions: {
      external: [],
      output: {
        assetFileNames: 'kanban-board.[ext]',
      },
    },
  },
  css: {
    modules: false,
  },
});
