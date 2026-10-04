import type { ProviderDto } from "../../api/dto";
import { t } from "../../lib/i18n";
import type { Column } from "../DataTable";
import { Badge } from "../primitives";
import { RowActions } from "../RowActions";

export interface ProviderColumnsConfig {
  onEdit: (provider: ProviderDto) => void;
  onDelete: (provider: ProviderDto) => void;
}

export function providerColumns({ onEdit, onDelete }: ProviderColumnsConfig): Array<Column<ProviderDto>> {
  return [
    { key: "name", header: t("common.name"), style: { fontWeight: 600 }, cell: (provider) => provider.name },
    {
      key: "host",
      header: t("providers.host"),
      className: "cell-mono",
      cell: (provider) => `${provider.host}:${provider.port}`,
    },
    { key: "type", header: t("audit.columnType"), cell: (provider) => provider.providerType },
    {
      key: "status",
      header: t("common.status"),
      cell: (provider) =>
        provider.isActive ? <Badge tone="success">{t("providers.stateActive")}</Badge> : <Badge tone="neutral">{t("computers.stateDisabled")}</Badge>,
    },
    {
      key: "actions",
      className: "row-actions",
      cell: (provider) => <RowActions item={provider} onEdit={onEdit} onDelete={onDelete} />,
    },
  ];
}
