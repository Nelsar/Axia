import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// Порт тот же, что был у python -m http.server, чтобы не переучивать руки и не трогать
// настройку CORS на backend.
export default defineConfig({
  plugins: [react()],
  server: { port: 5500 },
});
