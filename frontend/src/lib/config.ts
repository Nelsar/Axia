const STORAGE_KEY = "axia_api_base_url";

/**
 * Where the API is when nobody has said otherwise. The Docker image serves the console and proxies
 * /api to the API from one origin, so there it is wherever the console was loaded from; the
 * development server runs the console on its own port and the API on 5063.
 */
const DEFAULT_URL =
  import.meta.env.VITE_API_SAME_ORIGIN === "true" ? window.location.origin : "http://localhost:5063";

/**
 * Whether the API's address is this build's to decide rather than the browser's. A console served
 * from the same origin as the API — the Docker image — has exactly one API, its own. Letting the
 * address be changed there, and remembered, let a link or a moment at someone's desk point their
 * console at another server, which then received their password at the next sign-in. The
 * development server keeps the field: there the API is wherever the developer runs it.
 */
export const API_ADDRESS_IS_FIXED = import.meta.env.VITE_API_SAME_ORIGIN === "true";

export function getApiBaseUrl(): string {
  if (API_ADDRESS_IS_FIXED) return DEFAULT_URL;
  return localStorage.getItem(STORAGE_KEY) ?? DEFAULT_URL;
}

export function setApiBaseUrl(url: string): void {
  if (API_ADDRESS_IS_FIXED) return;
  const trimmed = url.trim().replace(/\/+$/, "");
  if (trimmed.length > 0) localStorage.setItem(STORAGE_KEY, trimmed);
}

export function getDefaultApiBaseUrl(): string {
  return DEFAULT_URL;
}
