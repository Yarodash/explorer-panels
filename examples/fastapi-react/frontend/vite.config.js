import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  server: {
    // Все запросы /api/* уходят на FastAPI
    proxy: {
      '/api': 'http://localhost:8000',
    },
  },
})
