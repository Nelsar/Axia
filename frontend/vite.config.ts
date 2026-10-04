import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// 5500 is historical — it is the port `python -m http.server` served this console on before
// Vite, and the muscle memory was not worth breaking. Nothing on the API side needs to match it:
// with Cors:AllowedOrigins unset the default policy admits any loopback port.
export default defineConfig({
  plugins: [react()],
  server: { port: 5500 },
});
