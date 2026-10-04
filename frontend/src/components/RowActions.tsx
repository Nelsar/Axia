import { t } from "../lib/i18n";
/** The "Edit / Delete" pair at the end of a table row — the same on five screens. */
export interface RowActionsProps<T> {
  item: T;
  onEdit: (item: T) => void;
  onDelete: (item: T) => void;
  editLabel?: string | undefined;
  deleteLabel?: string | undefined;
}

export function RowActions<T>({
  item,
  onEdit,
  onDelete,
  editLabel = t("common.edit"),
  deleteLabel = t("common.delete"),
}: RowActionsProps<T>): JSX.Element {
  return (
    <div>
      <button className="btn btn--sm" onClick={() => onEdit(item)}>
        {editLabel}
      </button>
      <button className="btn btn--sm btn--danger" style={{ marginInlineStart: "6px" }} onClick={() => onDelete(item)}>
        {deleteLabel}
      </button>
    </div>
  );
}
