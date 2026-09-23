import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// https://vite.dev/config/
// En developpement, /api est relaye vers le serveur (meme origine que le dashboard : les
// cookies de session fonctionnent sans CORS). DEV_API_TARGET : autre adresse (ex. https://localhost:4443).
const target = process.env.DEV_API_TARGET || 'http://localhost:4100'

export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/api': { target, changeOrigin: false, secure: false },
    },
  },
})
