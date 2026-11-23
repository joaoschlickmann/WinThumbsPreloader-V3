import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  build: {
    outDir: '../WinThumbsPreloader/WinThumbsPreloader/wwwroot',
    emptyOutDir: true,
    sourcemap: false,
  },
});
