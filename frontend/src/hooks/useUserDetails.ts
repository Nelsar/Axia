import { useEffect, useState } from "react";
import { api } from "../api";
import type { UserDto } from "../api/dto";
import { t } from "../lib/i18n";
import { messageOf } from "../lib/errors";

export type UserDetailsStatus = "loading" | "ready" | "error";

export interface UserDetailsState {
  /** The account as read just now, or — until that read answers — the row the card opened from. */
  user: UserDto;
  status: UserDetailsStatus;
  /** Why the read failed, in the API's words; null unless status is "error". */
  error: string | null;
}

/**
 * The card opens at once with the row it was opened from, and reads the account itself for what
 * a row does not have: its groups. A listing reads memberOf alone, which in a directory without
 * memberOf is no groups at all; GET /users/{id} reads them the way sign-in does, from the groups
 * that list the account as well.
 */
export function useUserDetails(row: UserDto): UserDetailsState {
  const [state, setState] = useState<UserDetailsState>({ user: row, status: "loading", error: null });

  useEffect(() => {
    let current = true;
    setState({ user: row, status: "loading", error: null });

    const read = async (): Promise<void> => {
      try {
        const user = await api.users.getById(row.id);
        if (current) setState({ user, status: "ready", error: null });
      } catch (err: unknown) {
        if (current) setState({ user: row, status: "error", error: messageOf(err, t("common.notAvailable")) });
      }
    };
    void read();

    return () => {
      current = false;
    };
  }, [row]);

  return state;
}
