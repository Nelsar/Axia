import { ApiError } from "../../api/client";
import { t } from "../../lib/i18n";
import type { UserListingResponse } from "../../api/dto";
import { DIRECTORY_COUNT_PAGE_SIZE } from "../../hooks/useDashboardData";
import type { DirectoryCount } from "../../hooks/useDashboardData";

/**
 * The caveats under the counters. Pure functions: a number may be shown without a caveat only
 * when it really is a total — an interrupted walk and a page that hit its limit both yield an
 * understated number, and passing that off as a total silently is not allowed. null means "no
 * caveat".
 */

// GET /api/v1/users answers with { items, isComplete }. isComplete === false means the directory
// walk stopped early: the users shown are real, but they are not the whole directory.
export function usersCountNote(users: UserListingResponse | null, canListUsers: boolean): string | null {
  if (!canListUsers) return t("dashboard.noRightsUsers");
  if (users !== null && !users.isComplete) return t("dashboard.incompleteCount");
  return null;
}

/** null means there is no number at all; the failure branch has no count field, so the two cannot be confused. */
export function directoryCountValue(result: DirectoryCount): number | null {
  return result.ok ? result.count : null;
}

export function directoryCountNote(result: DirectoryCount, unsupportedNote: string): string | null {
  if (!result.ok) {
    // A 501 is ActiveDirectory: it does not manage groups or OUs and says so instead of returning an empty list.
    if (result.error instanceof ApiError && result.error.status === 501) return unsupportedNote;
    if (result.error instanceof ApiError && result.error.status === 403) return t("common.noRights");
    return t("dashboard.apiUnavailable");
  }
  if (!result.isComplete) return t("dashboard.incompleteCount");
  if (result.hasMore) return t("dashboard.firstPageOnly", { size: DIRECTORY_COUNT_PAGE_SIZE });
  return null;
}
