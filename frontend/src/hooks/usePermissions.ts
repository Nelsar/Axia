import { useSyncExternalStore } from "react";
import type { Permission } from "../api/dto";
import { getPermissions, subscribeToPermissions } from "../lib/permissions";

/**
 * What the session may do, as the API last said — re-rendering whenever the shell refreshes it.
 * See lib/permissions for why this, and not the role, is what the console asks.
 */
export function usePermissionSet(): ReadonlySet<Permission> {
  return useSyncExternalStore(subscribeToPermissions, getPermissions);
}

export function useCan(permission: Permission): boolean {
  return usePermissionSet().has(permission);
}

export interface UserPermissions {
  readonly canCreate: boolean;
  readonly canEdit: boolean;
  readonly canDelete: boolean;
  readonly canBulk: boolean;
  readonly canGenerate: boolean;
}

/** One flag per kind of thing the users screen offers, each the permission its endpoint checks. */
export function useUserPermissions(): UserPermissions {
  const held = usePermissionSet();
  return {
    canCreate: held.has("users.create"),
    canEdit: held.has("users.update"),
    canDelete: held.has("users.delete"),
    canBulk: held.has("users.bulk"),
    canGenerate: held.has("users.generate"),
  };
}
