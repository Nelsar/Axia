import type { ProviderDto, RoleMappingDto, Uuid } from "../../api/dto";
import { t } from "../../lib/i18n";
import type { Column } from "../DataTable";
import { Badge } from "../primitives";
import { RowActions } from "../RowActions";

// A mapping with no provider applies to all of them — that is not "empty" but a meaningful value,
// and it deserves to be named in words.
export function providerLabel(providers: readonly ProviderDto[], providerId: Uuid | null): string {
  if (providerId === null) return t("roleMappings.global");
  const provider = providers.find((item) => item.id === providerId);
  return provider?.name ?? providerId;
}

export interface RoleMappingColumnsConfig {
  providers: readonly ProviderDto[];
  onEdit: (mapping: RoleMappingDto) => void;
  onDelete: (mapping: RoleMappingDto) => void;
}

export function roleMappingColumns({
  providers,
  onEdit,
  onDelete,
}: RoleMappingColumnsConfig): Array<Column<RoleMappingDto>> {
  return [
    {
      key: "group",
      header: t("roleMappings.columnGroup"),
      className: "cell-mono",
      // Flagged, because a mapping by name grants its role to every group that shares the name —
      // whoever can create a group can create one. The page hint says what to do about it.
      cell: (mapping) =>
        mapping.identifiesGroupByNameOnly ? (
          <>
            {mapping.groupName} <Badge tone="warning">{t("roleMappings.byNameBadge")}</Badge>
          </>
        ) : (
          mapping.groupName
        ),
    },
    { key: "role", header: t("common.role"), cell: (mapping) => <Badge tone="accent">{mapping.roleName}</Badge> },
    {
      key: "provider",
      header: t("roleMappings.columnProvider"),
      className: "text-muted",
      cell: (mapping) => providerLabel(providers, mapping.providerId),
    },
    { key: "priority", header: "Priority", cell: (mapping) => mapping.priority },
    {
      key: "actions",
      className: "row-actions",
      cell: (mapping) => <RowActions item={mapping} onEdit={onEdit} onDelete={onDelete} />,
    },
  ];
}
