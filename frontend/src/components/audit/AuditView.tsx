import type { AuditEventDto } from "../../api/dto";
import { t } from "../../lib/i18n";
import type { ListStatus } from "../../hooks/usePagedList";
import { DataTable } from "../DataTable";
import type { Column } from "../DataTable";
import { PageHeader } from "../PageHeader";
import { Card, ErrorBanner, LoadMore, Spinner } from "../primitives";
import type { JSX } from "react";

export interface AuditViewProps {
  status: ListStatus;
  error: string | null;
  items: readonly AuditEventDto[];
  columns: ReadonlyArray<Column<AuditEventDto>>;
  hasMore: boolean;
  isLoadingMore: boolean;
  onLoadMore: () => void;
}

export function AuditView({
  status,
  error,
  items,
  columns,
  hasMore,
  isLoadingMore,
  onLoadMore,
}: AuditViewProps): JSX.Element {
  return (
    <div className="page">
      <PageHeader
        title={t("nav.audit")}
        hint={t("audit.hint")}
      />

      <Card>
        {status === "loading" && <Spinner />}
        {status === "error" && (
          <Card.Body>
            <ErrorBanner>{error}</ErrorBanner>
          </Card.Body>
        )}
        {status === "ready" && (
          <div>
            <DataTable columns={columns} items={items} emptyText={t("dashboard.noEvents")} />
            {hasMore && <LoadMore onClick={onLoadMore} isLoading={isLoadingMore} />}
          </div>
        )}
      </Card>
    </div>
  );
}
