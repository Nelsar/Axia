import { isUserRole } from "../api/dto";
import type { UserDto } from "../api/dto";
import { clearPermissions, setPermissions } from "./permissions";

const TOKEN_KEY = "axia_token";
const USER_KEY = "axia_user";
const DIRECTORY_KEY = "axia_directory";

/** What the console knows about whoever signed in: the UserDto from the login response, kept in localStorage. */
export type SessionUser = UserDto;

interface JwtPayload {
  exp?: number | undefined;
}

/** The directory the sign-in went through. null is the default directory, which has no id. */
export interface SessionDirectory {
  readonly id: string;
  readonly name: string;
}

export function setSession(
  token: string,
  user: SessionUser,
  directory: SessionDirectory | null,
  permissions: readonly string[]
): void {
  localStorage.setItem(TOKEN_KEY, token);
  localStorage.setItem(USER_KEY, JSON.stringify(user));
  setPermissions(permissions);

  if (directory === null) localStorage.removeItem(DIRECTORY_KEY);
  else localStorage.setItem(DIRECTORY_KEY, JSON.stringify(directory));
}

/**
 * The current session's directory, kept only to show a person where they are. The token is what
 * decides regardless: the backend reads the directory from it, not from what sits in localStorage.
 */
export function getSessionDirectory(): SessionDirectory | null {
  const raw = localStorage.getItem(DIRECTORY_KEY);
  if (raw === null) return null;
  try {
    const parsed: unknown = JSON.parse(raw);
    if (typeof parsed !== "object" || parsed === null) return null;
    const record: Record<string, unknown> = parsed as Record<string, unknown>;
    const id: unknown = record["id"];
    const name: unknown = record["name"];
    return typeof id === "string" && typeof name === "string" ? { id, name } : null;
  } catch {
    return null;
  }
}

export function getToken(): string | null {
  return localStorage.getItem(TOKEN_KEY);
}

/**
 * localStorage is editable by anyone from the browser console, so what comes back from it is
 * `unknown` rather than a SessionUser on trust. What is checked is what the interface actually
 * leans on: the name and the role.
 */
function isSessionUser(value: unknown): value is SessionUser {
  if (typeof value !== "object" || value === null) return false;
  const record: Record<string, unknown> = value as Record<string, unknown>;
  return typeof record["name"] === "string" && isUserRole(record["role"]);
}

export function getCurrentUser(): SessionUser | null {
  const raw = localStorage.getItem(USER_KEY);
  if (raw === null) return null;
  try {
    const parsed: unknown = JSON.parse(raw);
    return isSessionUser(parsed) ? parsed : null;
  } catch {
    return null;
  }
}

function decodeJwt(token: string): JwtPayload | null {
  // split always yields an array, but under noUncheckedIndexedAccess an element is
  // string | undefined: a string with no dots is also a "token" as far as the types go, so
  // reaching for [1] blindly is not allowed.
  const payload = token.split(".")[1];
  if (payload === undefined) return null;

  try {
    const base64 = payload.replace(/-/g, "+").replace(/_/g, "/");
    const json = decodeURIComponent(
      atob(base64)
        .split("")
        .map((char) => `%${char.charCodeAt(0).toString(16).padStart(2, "0")}`)
        .join("")
    );
    const parsed: unknown = JSON.parse(json);
    if (typeof parsed !== "object" || parsed === null) return null;
    const exp: unknown = (parsed as Record<string, unknown>)["exp"];
    return { exp: typeof exp === "number" ? exp : undefined };
  } catch {
    return null;
  }
}

export function isAuthenticated(): boolean {
  const token = getToken();
  if (token === null) return false;
  const payload = decodeJwt(token);
  // A token that could not be parsed must not be rejected here: the backend is what checks
  // expiry, and the frontend only spares the user a session that has plainly already expired.
  if (payload?.exp === undefined) return true;
  return payload.exp * 1000 > Date.now();
}

export function logout(): void {
  localStorage.removeItem(TOKEN_KEY);
  localStorage.removeItem(USER_KEY);
  clearPermissions();

  // The directory deliberately outlives the sign-out: the sign-in form uses it to preselect the
  // previous choice, and retyping the same thing on every sign-in is exactly the friction that
  // kept the directory picker unbuilt. There is no secret in it: the name was just shown in a
  // list served with no authorisation at all.
}
