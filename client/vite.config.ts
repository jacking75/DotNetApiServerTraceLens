import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  server: {
    host: 'localhost',
    port: 25012,
    proxy: {
      '/api': 'http://127.0.0.1:25011',
    },
  },
  build: {
    outDir: '../src/TraceLens.Server/wwwroot',
    emptyOutDir: true,
  },
})
