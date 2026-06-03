import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react],
  base: './',
  server: {
    host: '0.0.0.0',
    allowedHosts: [
      'localhost',
      '127.0.0.1',
      '.loca.lt',
    ],
    proxy: {
      '/api': 'http://localhost:5000',
    },
  },
  preview: {
    host: '0.0.0.0',
    port: 4173,
    allowedHosts: [
      'localhost',
      '127.0.0.1',
      '.loca.lt',
    ],
    proxy: {
      '/api': 'http://localhost:5000',
    },
  },
});
