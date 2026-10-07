import { isPermission } from "../api/dto";
import type { Permission } from "../api/dto";

/**
 * What the API will allow the current session, as it last said — the one thing the console
 * decides what to show from.
 *
 * It used to decide from the single role in the sign-in response, which is the lowest-priority
 * role a person's groups map to, while the API grants the union of all of them. Someone in both
 * UserEditors and UserDeleters was allowed to delete and shown no delete button; someone given a
 * permission through the roles screen was shown nothing at all, because no role name changed.
 *
 * Kept in localStorage like the rest of the session, so a reload does not start from nothing, and
 * refreshed from GET /api/v1/me/permissions whenever the shell mounts. Subscribers are told when
 * it changes, so a refresh that arrives after a page rendered still reaches it. None of this is a
 * protection — every request is checked by the API on its own — only a way of not offering what
 * will be refused, and of offering what will not.
 */
const STORAGE_KEY = "axia_permissions";
const listeners = new Set<() => void>();

let cachedRaw: string | null = null;
let cachedSet: ReadonlySet<Permission> = new Set();

/** Stable between changes, as useSyncExternalStore requires of a snapshot. */
export function getPermissions(): ReadonlySet<Permission> {
  const raw = localStorage.getItem(STORAGE_KEY);
  if (raw === cachedRaw) return cachedSet;

  cachedRaw = raw;
  cachedSet = new Set(parse(raw));
  return cachedSet;
}

function parse(raw: string | null): Permission[] {
  if (raw === null) return [];
  try {
    const parsed: unknown = JSON.parse(raw);
    return Array.isArray(parsed) ? parsed.filter(isPermission) : [];
  } catch {
    return [];
  }
}

export function setPermissions(permissions: readonly string[]): void {
  localStorage.setItem(STORAGE_KEY, JSON.stringify(permissions.filter(isPermission)));
  notify();
}

export function clearPermissions(): void {
  localStorage.removeItem(STORAGE_KEY);
  notify();
}

/** Whether the session holds every one of the given permissions. */
export function can(...required: readonly Permission[]): boolean {
  const held = getPermissions();
  return required.every((permission) => held.has(permission));
}

export function subscribeToPermissions(listener: () => void): () => void {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}

function notify(): void {
  for (const listener of listeners) listener();
}
