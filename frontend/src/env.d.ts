/**
 * The build-time settings this console reads. Vite fills import.meta.env from VITE_* variables
 * set when `npm run build` runs; anything not set is undefined.
 */
interface ImportMetaEnv {
  /**
   * "true" when the console is served from the same origin as the API, behind one reverse proxy —
   * as the Docker image is. Unset for the development server, which talks to the API's own port.
   */
  readonly VITE_API_SAME_ORIGIN?: string | undefined;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
