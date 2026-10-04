import type { OrganizationalUnitDto } from "../../api/dto";
import { t } from "../../lib/i18n";
import type { Column } from "../DataTable";
import { RowActions } from "../RowActions";

export interface OrgUnitColumnsConfig {
  canManage: boolean;
  onEdit: (unit: OrganizationalUnitDto) => void;
  onDelete: (unit: OrganizationalUnitDto) => void;
}

export function orgUnitColumns({
  canManage,
  onEdit,
  onDelete,
}: OrgUnitColumnsConfig): Array<Column<OrganizationalUnitDto>> {
  const columns: Array<Column<OrganizationalUnitDto>> = [
    { key: "name", header: t("common.name"), cell: (unit) => unit.name },
    { key: "dn", header: "DN", className: "cell-mono", cell: (unit) => unit.distinguishedName },
    {
      key: "parent",
      header: t("common.parentDn"),
      className: "cell-mono text-muted",
      cell: (unit) => unit.parentDistinguishedName || "—",
    },
    { key: "description", header: t("common.description"), className: "text-muted", cell: (unit) => unit.description || "—" },
  ];

  if (canManage) {
    columns.push({
      key: "actions",
      className: "row-actions",
      cell: (unit) => <RowActions item={unit} onEdit={onEdit} onDelete={onDelete} />,
    });
  }

  return columns;
}
