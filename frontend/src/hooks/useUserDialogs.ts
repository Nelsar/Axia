import { useCallback } from "react";
import type { UserDto } from "../api/dto";
import { useDialog } from "./useDialog";
import type { DialogState } from "./useDialog";

/** The users screen dialogs: the create/edit form and the detail card. */
export type UserDialogKind = "form" | "detail";

export interface UserDialogsState {
  dialog: DialogState<UserDialogKind, UserDto> | null;
  openCreate: () => void;
  openEdit: (user: UserDto) => void;
  openDetail: (user: UserDto) => void;
  close: () => void;
}

// The screen's own operation names on top of the shared useDialog: the container says "open the
// detail card" rather than "put an object with kind: detail into state".
export function useUserDialogs(): UserDialogsState {
  const { dialog, open, close } = useDialog<UserDialogKind, UserDto>();

  return {
    dialog,
    openCreate: useCallback((): void => open("form"), [open]),
    openEdit: useCallback((user: UserDto): void => open("form", user), [open]),
    openDetail: useCallback((user: UserDto): void => open("detail", user), [open]),
    close,
  };
}
