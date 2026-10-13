import type { AuditEventDto } from "../../api/dto";
import { t } from "../../lib/i18n";
import { formatDateTime } from "../../lib/ui";
import type { Column } from "../DataTable";
import { Badge } from "../primitives";

function truncate(value: string, limit: number): string {
  return value.length > limit ? `${value.slice(0, limit)}…` : value;
}

export function auditColumns(): Array<Column<AuditEventDto>> {
  return [
    { key: "time", header: t("common.time"), className: "cell-mono", cell: (event) => formatDateTime(event.timestamp) },
    { key: "type", header: t("audit.columnType"), cell: (event) => event.eventType },
    { key: "action", header: t("audit.columnAction"), cell: (event) => event.action },
    { key: "actor", header: t("common.actor"), cell: (event) => event.actor },
    {
      key: "object",
      header: t("audit.columnTarget"),
      className: "cell-mono",
      // A DN does not fit the column, but a truncated DN must not pass for a whole one: the full
      // value stays in the title.
      cell: (event) => (
        <span title={event.objectDn ?? ""}>{event.objectDn !== null ? truncate(event.objectDn, 28) : "—"}</span>
      ),
    },
    {
      key: "result",
      header: t("common.result"),
      cell: (event) =>
        event.success ? <Badge tone="success">{t("common.success")}</Badge> : <Badge tone="danger">{t("common.failure")}</Badge>,
    },
    { key: "details", header: t("audit.columnDetails"), className: "text-muted cell-wrap", cell: (event) => event.details ?? "" },
  ];
}
