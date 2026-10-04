import { useEffect, useRef, useState } from "react";
import { api } from "../api";
import type {
  AuditEventDto,
  GroupDto,
  OrganizationalUnitDto,
  PagedResponse,
  ProviderDto,
  RoleMappingDto,
  UserListingResponse,
} from "../api/dto";
import { hasRole } from "../lib/auth";
import type { PageRequest } from "./usePagedList";

// Neither /api/groups nor /api/organizationalunits returns a total — the response carries a page
// and a hasMore flag. So the counter here is the length of one deliberately large page, and
// hasMore means the number shown is a lower bound rather than a total. The backend walks the
// whole directory before paginating anyway, so a large pageSize adds it no work.
export const DIRECTORY_COUNT_PAGE_SIZE = 1000;

/**
 * A counter's result is a discriminated union: either a number with caveats, or a failure. That
 * makes it impossible to show "we could not count" as a zero by accident — the failure branch
 * simply has no count field.
 */
export type DirectoryCount =
  | { readonly ok: true; readonly count: number; readonly hasMore: boolean; readonly isComplete: boolean }
  | { readonly ok: false; readonly error: unknown };

export interface DashboardData {
  readonly users: UserListingResponse | null;
  readonly providers: readonly ProviderDto[] | null;
  readonly roleMappings: readonly RoleMappingDto[] | null;
  readonly audit: PagedResponse<AuditEventDto> | null;
  readonly groups: DirectoryCount;
  readonly orgUnits: DirectoryCount;
  readonly canListUsers: boolean;
  readonly isAdmin: boolean;
}

export interface DashboardState {
  status: "loading" | "ready";
  data: DashboardData | null;
}

type DirectorySearch<T> = (request: PageRequest) => Promise<PagedResponse<T>>;

async function countDirectoryEntries<T>(search: DirectorySearch<T>): Promise<DirectoryCount> {
  try {
    const page = await search({ pageSize: DIRECTORY_COUNT_PAGE_SIZE });
    return { ok: true, count: page.items.length, hasMore: page.hasMore, isComplete: page.isComplete !== false };
  } catch (error: unknown) {
    return { ok: false, error };
  }
}

/**
 * Collecting the dashboard's data: six independent requests, each with its own permission. None
 * of them has to succeed — the tile says "n/a" and why, honestly.
 */
export function useDashboardData(): DashboardState {
  const [status, setStatus] = useState<DashboardState["status"]>("loading");
  const [data, setData] = useState<DashboardData | null>(null);
  const isMounted = useRef<boolean>(true);

  useEffect(() => {
    isMounted.current = true;
    const canListUsers = hasRole("Admin", "UserCreator");
    const isAdmin = hasRole("Admin");

    // groups.read and orgunits.read are held by every authenticated role (see DbInitializer), so
    // these two counters are not behind a role check, unlike the rest.
    void Promise.all([
      canListUsers ? api.users.getAll().catch((): null => null) : Promise.resolve(null),
      isAdmin ? api.providers.getAll().catch((): null => null) : Promise.resolve(null),
      isAdmin ? api.roleMappings.getAll().catch((): null => null) : Promise.resolve(null),
      isAdmin ? api.audit.getRecent(5).catch((): null => null) : Promise.resolve(null),
      countDirectoryEntries<GroupDto>((request) => api.groups.search(request)),
      countDirectoryEntries<OrganizationalUnitDto>((request) => api.orgUnits.search(request)),
    ]).then(([users, providers, roleMappings, audit, groups, orgUnits]) => {
      if (!isMounted.current) return;
      setData({ users, providers, roleMappings, audit, groups, orgUnits, canListUsers, isAdmin });
      setStatus("ready");
    });

    return () => {
      isMounted.current = false;
    };
  }, []);

  return { status, data };
}
