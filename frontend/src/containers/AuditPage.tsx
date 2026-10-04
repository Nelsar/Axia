import { useMemo } from "react";
import { t } from "../lib/i18n";
import { api } from "../api";
import type { AuditEventDto, PagedResponse } from "../api/dto";
import { usePagedList } from "../hooks/usePagedList";
import type { PageRequest } from "../hooks/usePagedList";
import { auditColumns } from "../components/audit/auditColumns";
import { AuditView } from "../components/audit/AuditView";

// The audit log has no filter, so the adapter simply ignores one.
const fetchEvents = ({ pageSize, pageToken }: PageRequest): Promise<PagedResponse<AuditEventDto>> =>
  api.audit.getRecent(pageSize, pageToken);

export function AuditPage(): JSX.Element {
  const list = usePagedList<AuditEventDto>({
    fetchPage: fetchEvents,
    errorFallback: t("audit.loadFailed"),
  });
  const columns = useMemo(() => auditColumns(), []);

  return (
    <AuditView
      status={list.status}
      error={list.error}
      items={list.items}
      columns={columns}
      hasMore={list.hasMore}
      isLoadingMore={list.isLoadingMore}
      onLoadMore={list.loadMore}
    />
  );
}
