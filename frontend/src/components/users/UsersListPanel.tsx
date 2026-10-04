import type { UserDto } from "../../api/dto";
import { t } from "../../lib/i18n";
import type { UserPermissions } from "../../hooks/usePermissions";
import type { ListStatus } from "../../hooks/usePagedList";
import { Card, ErrorBanner, IncompleteNotice, LoadMore, SearchBox, Spinner } from "../primitives";
import { UserTable } from "./UserTable";

export interface UsersListPanelProps {
  query: string;
  onQueryChange: (value: string) => void;
  onRefresh: () => void;
  status: ListStatus;
  error: string | null;
  items: readonly UserDto[];
  isComplete: boolean;
  hasMore: boolean;
  isLoadingMore: boolean;
  onLoadMore: () => void;
  perms: UserPermissions;
  onSelect: (user: UserDto) => void;
  onEdit: (user: UserDto) => void;
  onDelete: (user: UserDto) => void;
}

/**
 * The "List" tab. A presentational component: the search box, the loading and error states, the
 * table and the load-more button. What "refresh" or "delete" mean is the hook's to decide.
 */
export function UsersListPanel({
  query,
  onQueryChange,
  onRefresh,
  status,
  error,
  items,
  isComplete,
  hasMore,
  isLoadingMore,
  onLoadMore,
  perms,
  onSelect,
  onEdit,
  onDelete,
}: UsersListPanelProps): JSX.Element {
  return (
    <div>
      {status === "ready" && !isComplete && <IncompleteNotice noun={t("users.noun")} />}
      <Card>
        <Card.Body style={{ display: "flex", gap: "10px", alignItems: "center" }}>
          <SearchBox value={query} onChange={onQueryChange} placeholder={t("users.filterPlaceholder")} />
          <button className="btn" onClick={onRefresh}>{t("common.refresh")}</button>
        </Card.Body>

        {status === "loading" && <Spinner label={t("users.loading")} />}
        {status === "error" && <ErrorBanner>{error}</ErrorBanner>}
        {status === "ready" && (
          <UserTable items={items} perms={perms} onSelect={onSelect} onEdit={onEdit} onDelete={onDelete} />
        )}
        {status === "ready" && hasMore && <LoadMore onClick={onLoadMore} isLoading={isLoadingMore} />}
      </Card>
    </div>
  );
}
