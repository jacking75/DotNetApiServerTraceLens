import { defineConfig } from 'vitest/config'
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
  test: {
    // 대부분의 테스트는 순수 함수라 DOM이 필요 없다.
    // DOM이 필요한 파일은 맨 위에 `@vitest-environment jsdom` 주석을 단다(*.dom.test.tsx).
    environment: 'node',
  },
})
