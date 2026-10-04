import { useCallback } from "react";
import { t } from "../lib/i18n";
import { api } from "../api";
import type { PagedResponse, UserDto } from "../api/dto";
import { confirmAndRun } from "../lib/actions";
import { usePagedList } from "./usePagedList";
import type { PageRequest, PagedListState } from "./usePagedList";

export interface UsersListState extends PagedListState<UserDto> {
  /** true only if the user was actually deleted, not if the confirmation was dismissed. */
  removeUser: (user: UserDto) => Promise<boolean>;
}

// The reference is stable between renders — otherwise usePagedList would refetch the list forever.
const fetchUsers = (request: PageRequest): Promise<PagedResponse<UserDto>> => api.users.search(request);

/** The users list = the shared usePagedList plus one domain action. */
export function useUsersList(): UsersListState {
  const list = usePagedList<UserDto>({
    fetchPage: fetchUsers,
    errorFallback: t("users.loadFailed"),
  });
  const { reload } = list;

  // One deletion path for both the list row and the detail card: the confirmation, the toast and
  // the reload must not diverge, least of all for an irreversible action.
  const removeUser = useCallback(
    async (user: UserDto): Promise<boolean> => {
      // The email can be empty: "Delete user ?" names nobody, which is the one thing an
      // irreversible question must not do.
      const label = user.email || user.username || user.name;
      const deleted = await confirmAndRun({
        question: t("users.deleteConfirm", { label }),
        run: () => api.users.remove(user.id),
        success: t("users.deleted"),
        failure: t("users.deleteFailed"),
      });
      if (deleted) reload();
      return deleted;
    },
    [reload]
  );

  return { ...list, removeUser };
}
