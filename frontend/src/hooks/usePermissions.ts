import { useMemo } from "react";
import { hasRole } from "../lib/auth";

/** What the users screen is allowed to do. */
export interface UserPermissions {
  readonly canCreate: boolean;
  readonly canEdit: boolean;
  readonly canDelete: boolean;
}

// Roles hide buttons that would not work, nothing more: the real permission check is on the
// backend ([Authorize(Roles=…)]), and the frontend is not the source of truth for authorisation.
export function useUserPermissions(): UserPermissions {
  return useMemo<UserPermissions>(
    () => ({
      canCreate: hasRole("Admin", "UserCreator"),
      canEdit: hasRole("Admin", "UserEditor"),
      canDelete: hasRole("Admin", "UserDeleter"),
    }),
    []
  );
}

export function useIsAdmin(): boolean {
  return useMemo<boolean>(() => hasRole("Admin"), []);
}
