import { useCallback, useState } from "react";

/**
 * Which dialog is open is one piece of state rather than a set of booleans: dialogs are mutually
 * exclusive, and there is simply nothing here able to represent "two are open at once".
 *
 * TKind is the union of the screen's dialogs ("form" | "detail"), TItem the entity the dialog
 * works on. `item: null` means creation: there is no entity yet.
 */
export interface DialogState<TKind extends string, TItem> {
  kind: TKind;
  item: TItem | null;
}

export interface UseDialogResult<TKind extends string, TItem> {
  dialog: DialogState<TKind, TItem> | null;
  open: (kind: TKind, item?: TItem | null) => void;
  close: () => void;
}

export function useDialog<TKind extends string, TItem>(): UseDialogResult<TKind, TItem> {
  const [dialog, setDialog] = useState<DialogState<TKind, TItem> | null>(null);

  const open = useCallback((kind: TKind, item: TItem | null = null): void => {
    setDialog({ kind, item });
  }, []);

  const close = useCallback((): void => setDialog(null), []);

  return { dialog, open, close };
}
