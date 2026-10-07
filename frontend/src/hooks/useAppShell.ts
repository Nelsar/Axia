import { useCallback, useEffect, useMemo } from "react";
import { getCurrentUser, getSessionDirectory, logout } from "../lib/auth";
import { api } from "../api";
import { setPermissions } from "../lib/permissions";
import { usePermissionSet } from "./usePermissions";
import type { SessionUser } from "../lib/auth";
import { getApiBaseUrl } from "../lib/config";
import { NAV_GROUPS } from "../lib/navigation";
import type { NavGroup } from "../lib/navigation";
import { navigate } from "../lib/router";
import type { Theme } from "../lib/theme";
import { useTheme } from "./useTheme";

export interface AppShellState {
  readonly user: SessionUser | null;
  /** Initials for the avatar, computed here so the view never has to take a name apart. */
  readonly initials: string;
  readonly apiBaseUrl: string;
  /** This session's directory name; null is the default directory, which has no name to give. */
  readonly directoryName: string | null;
  /** The groups are already filtered by role; empty sections are dropped. */
  readonly nav: readonly NavGroup[];
  readonly theme: Theme;
  readonly onToggleTheme: () => void;
  readonly onLogout: () => void;
}

/** The first letter of each of the first two words; "?" when the session carries no name at all. */
function initialsOf(name: string): string {
  return name
    .split(/\s+/)
    .map((part) => part.charAt(0))
    .join("")
    .slice(0, 2)
    .toUpperCase();
}

/**
 * All of the shell's logic: who signed in, what they can see in the menu, where the API points
 * and how the theme is toggled.
 *
 * The session and the API address are read from localStorage once per mount: they do not change
 * within a page, and after a sign-in or sign-out the router mounts a fresh root anyway.
 */
export function useAppShell(): AppShellState {
  const { theme, toggle } = useTheme();

  const user = useMemo<SessionUser | null>(() => getCurrentUser(), []);
  const initials = useMemo<string>(() => initialsOf(user?.name ?? "?"), [user]);
  const apiBaseUrl = useMemo<string>(() => getApiBaseUrl(), []);
  const directoryName = useMemo<string | null>(() => getSessionDirectory()?.name ?? null, []);

  // Asked afresh whenever the shell mounts — every navigation — so a change made on the roles
  // screen, or to someone's groups, reaches the menu and the buttons without signing in again.
  // A failure keeps what the session already had: the API still checks every request, and a
  // 401 here signs the user out through the client like any other.
  useEffect(() => {
    void api.me
      .permissions()
      .then(setPermissions)
      .catch((): void => undefined);
  }, []);

  // A hidden menu item is a convenience rather than a protection: an unreachable route is closed
  // by the guard in main.tsx, and the endpoint itself by [Authorize(Policy=…)] on the backend.
  const held = usePermissionSet();
  const nav = useMemo<readonly NavGroup[]>(
    () =>
      NAV_GROUPS.map((group) => ({
        id: group.id,
        title: group.title,
        items: group.items.filter((item) => item.permission === undefined || held.has(item.permission)),
      })).filter((group) => group.items.length > 0),
    [held]
  );


  const onLogout = useCallback((): void => {
    logout();
    navigate("/login");
  }, []);

  return {
    user,
    initials,
    apiBaseUrl,
    directoryName,
    nav,
    theme,
    onToggleTheme: toggle,
    onLogout,
  };
}
