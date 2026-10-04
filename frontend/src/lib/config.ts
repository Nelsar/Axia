const STORAGE_KEY = "axia_api_base_url";
const DEFAULT_URL = "http://localhost:5063";

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
