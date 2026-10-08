const STORAGE_KEY = "axia_api_base_url";

/**
 * Where the API is when nobody has said otherwise. The Docker image serves the console and proxies
 * /api to the API from one origin, so there it is wherever the console was loaded from; the
 * development server runs the console on its own port and the API on 5063.
 */
const DEFAULT_URL =
  import.meta.env.VITE_API_SAME_ORIGIN === "true" ? window.location.origin : "http://localhost:5063";

export function getApiBaseUrl(): string {
  return localStorage.getItem(STORAGE_KEY) ?? DEFAULT_URL;
}

export function setApiBaseUrl(url: string): void {
  const trimmed = url.trim().replace(/\/+$/, "");
  if (trimmed.length > 0) localStorage.setItem(STORAGE_KEY, trimmed);
}

export function getDefaultApiBaseUrl(): string {
  return DEFAULT_URL;
}
