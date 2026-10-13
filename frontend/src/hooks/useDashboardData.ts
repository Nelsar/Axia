import { useEffect, useRef, useState } from "react";
import { api, MAX_PAGE_SIZE } from "../api";
import type {
  AuditEventDto,
  GroupDto,
  OrganizationalUnitDto,
  PagedResponse,
  ProviderDto,
  RoleMappingDto,
  UserListingResponse,
} from "../api/dto";
import { can } from "../lib/permissions";
import type { PageRequest } from "./usePagedList";

// Neither /api/v1/groups nor /api/v1/organizational-units returns a total — the response carries a page
// and a hasMore flag. So the counter here is the length of one deliberately large page, and
// hasMore means the number shown is a lower bound rather than a total. The backend walks the
// whole directory before paginating anyway, so a large pageSize adds it no work.
//
// The largest page the API serves, and no larger: this was 1000, which the API refuses with 400,
// so both counters read "API unavailable" against a directory that was answering perfectly well.
export const DIRECTORY_COUNT_PAGE_SIZE = MAX_PAGE_SIZE;

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
  readonly canSeeProviders: boolean;
  readonly canSeeRoleMappings: boolean;
  readonly canReadAudit: boolean;
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
    // Each tile asks for exactly the permission its endpoint checks — not for a role, which said
    // nothing about anyone whose roles only add up to it.
    const canListUsers = can("users.list");
    const canSeeProviders = can("providers.manage");
    const canSeeRoleMappings = can("roles.manage");
    const canReadAudit = can("audit.read");

    // groups.read and orgunits.read are held by every role on a fresh database (see DbInitializer),
    // so these two counters are asked regardless; a refusal shows as the tile's note.
    void Promise.all([
      canListUsers ? api.users.getAll().catch((): null => null) : Promise.resolve(null),
      canSeeProviders ? api.providers.getAll().catch((): null => null) : Promise.resolve(null),
      canSeeRoleMappings ? api.roleMappings.getAll().catch((): null => null) : Promise.resolve(null),
      canReadAudit ? api.audit.getRecent(5).catch((): null => null) : Promise.resolve(null),
      countDirectoryEntries<GroupDto>((request) => api.groups.search(request)),
      countDirectoryEntries<OrganizationalUnitDto>((request) => api.orgUnits.search(request)),
    ]).then(([users, providers, roleMappings, audit, groups, orgUnits]) => {
      if (!isMounted.current) return;
      setData({
        users,
        providers,
        roleMappings,
        audit,
        groups,
        orgUnits,
        canListUsers,
        canSeeProviders,
        canSeeRoleMappings,
        canReadAudit,
      });
      setStatus("ready");
    });

    return () => {
      isMounted.current = false;
    };
  }, []);

  return { status, data };
}
