import tailwindcss from '@tailwindcss/vite';
import path from 'path';
import { defineConfig } from 'vite';

export default defineConfig(() => {
  return {
    plugins: [tailwindcss()],
    resolve: {
      alias: {
        '@': path.resolve(__dirname, '.'),
      },
    },
    build: {
      outDir: 'wwwroot',
      emptyOutDir: true,
    },
    server: {
      // Allow hosted or constrained dev environments to disable HMR explicitly.
      hmr: process.env.DISABLE_HMR !== 'true',
      watch: process.env.DISABLE_HMR === 'true' ? null : {},
      proxy: {
        '/api': {
          target: 'http://localhost:5000',
          changeOrigin: true,
        },
        '/data/uploads': {
          target: 'http://localhost:5000',
          changeOrigin: true,
        },
      },
    },
  };
});
