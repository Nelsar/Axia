import type { GroupDto } from "../../api/dto";
import { t } from "../../lib/i18n";
import type { Column } from "../DataTable";
import { RowActions } from "../RowActions";

export interface GroupColumnsConfig {
  canManage: boolean;
  onShowMembers: (group: GroupDto) => void;
  onEdit: (group: GroupDto) => void;
  onDelete: (group: GroupDto) => void;
}

/**
 * The column description is a pure function of permissions and handlers. No state, no requests:
 * the result can be compared in a test without standing up a directory or a DOM.
 */
export function groupColumns({
  canManage,
  onShowMembers,
  onEdit,
  onDelete,
}: GroupColumnsConfig): Array<Column<GroupDto>> {
  const columns: Array<Column<GroupDto>> = [
    { key: "name", header: t("common.name"), cell: (group) => group.name },
    { key: "dn", header: "DN", className: "cell-mono", cell: (group) => group.distinguishedName },
    {
      key: "parent",
      header: t("common.parentDn"),
      className: "cell-mono text-muted",
      cell: (group) => group.parentDistinguishedName || "—",
    },
    { key: "description", header: t("common.description"), className: "text-muted", cell: (group) => group.description || "—" },
    {
      key: "members",
      header: t("groups.columnMembers"),
      cell: (group) =>
        group.members.length > 0 ? (
          <button className="btn btn--sm" onClick={() => onShowMembers(group)}>
            {group.members.length}
          </button>
        ) : (
          <span className="text-muted">0</span>
        ),
    },
  ];

  if (canManage) {
    columns.push({
      key: "actions",
      className: "row-actions",
      cell: (group) => <RowActions item={group} onEdit={onEdit} onDelete={onDelete} />,
    });
  }

  return columns;
}
