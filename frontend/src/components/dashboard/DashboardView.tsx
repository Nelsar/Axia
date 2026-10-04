import type { AuditEventDto, PagedResponse } from "../../api/dto";
import { t } from "../../lib/i18n";
import type { SessionUser } from "../../lib/auth";
import { formatDateTime } from "../../lib/ui";
import type { DashboardData, DashboardState } from "../../hooks/useDashboardData";
import { PageHeader } from "../PageHeader";
import { Badge, Card, Spinner } from "../primitives";
import { directoryCountNote, directoryCountValue, usersCountNote } from "./dashboardNotes";

export interface DashboardViewProps {
  user: SessionUser | null;
  status: DashboardState["status"];
  data: DashboardData | null;
}

interface StatCardProps {
  label: string;
  /** null is "n/a": there is no value, and a zero in its place would be a lie. */
  value: number | null;
  note: string | null;
}

function StatCard({ label, value, note }: StatCardProps): JSX.Element {
  return (
    <div className="stat-card">
      <div className="stat-card__label">{label}</div>
      <div className="stat-card__value">
        {value === null ? (
          <span className="text-muted" style={{ fontSize: "15px", fontWeight: 500 }}>{t("common.notAvailable")}</span>
        ) : (
          value.toLocaleString("ru-RU")
        )}
      </div>
      {note !== null && <div className="stat-card__meta">{note}</div>}
    </div>
  );
}

interface AuditPreviewProps {
  audit: PagedResponse<AuditEventDto> | null;
  isAdmin: boolean;
}

function AuditPreview({ audit, isAdmin }: AuditPreviewProps): JSX.Element {
  if (!isAdmin) {
    return <div className="card__pad text-muted">{t("dashboard.auditAdminOnly")}</div>;
  }
  if (audit === null || audit.items.length === 0) {
    return <div className="card__pad text-muted">{t("dashboard.noEvents")}</div>;
  }
  return (
    <div className="table-wrap">
      <table className="data-table">
        <thead>
          <tr>
            <th>{t("common.time")}</th>
            <th>{t("dashboard.eventColumn")}</th>
            <th>{t("common.actor")}</th>
            <th>{t("common.result")}</th>
          </tr>
        </thead>
        <tbody>
          {audit.items.map((event) => (
            <tr key={event.id}>
              <td className="cell-mono">{formatDateTime(event.timestamp)}</td>
              <td>
                {event.action} <span className="text-muted">({event.eventType})</span>
              </td>
              <td>{event.actor}</td>
              <td>{event.success ? <Badge tone="success">{t("common.success")}</Badge> : <Badge tone="danger">{t("common.failure")}</Badge>}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

export function DashboardView({ user, status, data }: DashboardViewProps): JSX.Element {
  return (
    <div className="page">
      <PageHeader title={t("dashboard.title")} hint={t("dashboard.greeting", { name: user?.name ?? "", role: user?.role ?? "" })} />

      {status === "loading" && <Spinner label={t("dashboard.collecting")} />}
      {status === "ready" && data !== null && (
        <div>
          <div className="stat-grid">
            <StatCard
              label={t("dashboard.usersCard")}
              value={data.users !== null ? data.users.items.length : null}
              note={usersCountNote(data.users, data.canListUsers)}
            />
            <StatCard
              label={t("nav.providers")}
              value={data.providers !== null ? data.providers.length : null}
              note={data.isAdmin ? null : t("common.adminOnly")}
            />
            <StatCard
              label={t("nav.roleMappings")}
              value={data.roleMappings !== null ? data.roleMappings.length : null}
              note={data.isAdmin ? null : t("common.adminOnly")}
            />
            <StatCard
              label={t("nav.groups")}
              value={directoryCountValue(data.groups)}
              note={directoryCountNote(data.groups, t("dashboard.groupsUnsupported"))}
            />
            <StatCard
              label={t("nav.orgUnits")}
              value={directoryCountValue(data.orgUnits)}
              note={directoryCountNote(data.orgUnits, t("dashboard.orgUnitsUnsupported"))}
            />
          </div>

          <Card>
            <Card.Header>
              <span>{t("dashboard.recentAudit")}</span>
              {data.isAdmin && <a href="#/audit">{t("dashboard.auditLink")}</a>}
            </Card.Header>
            <AuditPreview audit={data.audit} isAdmin={data.isAdmin} />
          </Card>

          <div className="stub-notice" style={{ textAlign: "start" }}>
            {t("dashboard.scopeNote")}
          </div>
        </div>
      )}
    </div>
  );
}
