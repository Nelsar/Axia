import type { CSSProperties, JSX, MouseEvent } from "react";
import { t } from "../../lib/i18n";
import type { UserDto } from "../../api/dto";
import type { UserPermissions } from "../../hooks/usePermissions";
import { DataTable } from "../DataTable";
import type { Column } from "../DataTable";
import { Badge } from "../primitives";
import { roleTone, subtitleFor } from "./userFormatting";

export interface UserTableProps {
  items: readonly UserDto[];
  perms: UserPermissions;
  onSelect: (user: UserDto) => void;
  onEdit: (user: UserDto) => void;
  /** Returns true if the user was actually deleted; the row does not need that, hence void. */
  onDelete: (user: UserDto) => void;
}

interface RowActionProps {
  label: string;
  user: UserDto;
  onClick: (user: UserDto) => void;
  danger?: boolean | undefined;
  style?: CSSProperties | undefined;
}

// Clicking a row opens the detail card, so the row's buttons have to stop propagation: otherwise
// every action would open the card behind itself.
function RowAction({ label, user, onClick, danger = false, style }: RowActionProps): JSX.Element {
  return (
    <button
      className={`btn btn--sm${danger ? " btn--danger" : ""}`}
      style={style}
      onClick={(event: MouseEvent<HTMLButtonElement>) => {
        event.stopPropagation();
        onClick(user);
      }}
    >
      {label}
    </button>
  );
}

/** A purely presentational component: it receives finished data and handlers and loads nothing. */
export function UserTable({ items, perms, onSelect, onEdit, onDelete }: UserTableProps): JSX.Element {
  const columns: Array<Column<UserDto>> = [
    {
      key: "user",
      header: t("users.columnUser"),
      cell: (user) => (
        <div>
          <div style={{ fontWeight: 600 }}>{user.name}</div>
          <div className="cell-mono">{subtitleFor(user)}</div>
        </div>
      ),
    },
    {
      key: "role",
      header: t("common.role"),
      cell: (user) => <Badge tone={roleTone(user.role)}>{user.role}</Badge>,
    },
    {
      key: "actions",
      className: "row-actions",
      cell: (user) => (
        <div>
          {perms.canEdit && <RowAction label={t("common.edit")} user={user} onClick={onEdit} />}
          {perms.canDelete && (
            <RowAction label={t("common.delete")} danger style={{ marginInlineStart: "6px" }} user={user} onClick={onDelete} />
          )}
        </div>
      ),
    },
  ];

  return <DataTable columns={columns} items={items} emptyText={t("common.nothingFound")} onRowClick={onSelect} />;
}
