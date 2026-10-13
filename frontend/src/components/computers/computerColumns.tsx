import type { ComputerDto } from "../../api/dto";
import { t } from "../../lib/i18n";
import type { Column } from "../DataTable";
import { Badge, Muted } from "../primitives";
import { RowActions } from "../RowActions";
import type { JSX } from "react";

export interface ComputerColumnsConfig {
  canManage: boolean;
  onEdit: (computer: ComputerDto) => void;
  onDelete: (computer: ComputerDto) => void;
}

// Three states, not two. null means "the directory did not say" — which happens for entries with
// no userAccountControl (an RFC 4519 device, for instance), and showing those as enabled is not
// allowed. The DTO's `boolean | null` forces all three branches to be handled: `if (enabled)` is
// not enough here.
function EnabledCell({ computer }: { computer: ComputerDto }): JSX.Element {
  if (computer.enabled === true) return <Badge tone="success">{t("computers.stateEnabled")}</Badge>;
  if (computer.enabled === false) return <Badge tone="danger">{t("computers.stateDisabled")}</Badge>;
  return <Muted>{t("common.unknown")}</Muted>;
}

function OsCell({ computer }: { computer: ComputerDto }): JSX.Element {
  if (computer.operatingSystem === "") return <Muted>—</Muted>;
  return (
    <div>
      {computer.operatingSystem}
      {computer.operatingSystemVersion !== "" && (
        <div className="text-muted" style={{ fontSize: "11.5px" }}>
          {computer.operatingSystemVersion}
        </div>
      )}
    </div>
  );
}

export function computerColumns({
  canManage,
  onEdit,
  onDelete,
}: ComputerColumnsConfig): Array<Column<ComputerDto>> {
  const columns: Array<Column<ComputerDto>> = [
    {
      key: "name",
      header: t("common.name"),
      cell: (computer) => (
        <div>
          <div style={{ fontWeight: 600 }}>{computer.name}</div>
          {computer.description !== "" && (
            <div className="text-muted" style={{ fontSize: "11.5px" }}>
              {computer.description}
            </div>
          )}
        </div>
      ),
    },
    { key: "dns", header: t("computers.dnsLabel"), className: "cell-mono", cell: (computer) => computer.dnsHostName || "—" },
    { key: "os", header: t("computers.columnOs"), cell: (computer) => <OsCell computer={computer} /> },
    { key: "enabled", header: t("computers.columnState"), cell: (computer) => <EnabledCell computer={computer} /> },
    {
      key: "dn",
      header: "DN",
      className: "cell-mono text-muted",
      style: { fontSize: "11.5px" },
      cell: (computer) => computer.distinguishedName,
    },
  ];

  if (canManage) {
    columns.push({
      key: "actions",
      className: "row-actions",
      cell: (computer) => <RowActions item={computer} onEdit={onEdit} onDelete={onDelete} />,
    });
  }

  return columns;
}
