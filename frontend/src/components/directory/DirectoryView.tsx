import type { ChangeEvent, JSX, ReactNode } from "react";
import { DataTable } from "../DataTable";
import type { Column } from "../DataTable";
import { PageHeader } from "../PageHeader";
import { Card, ErrorBanner, FormHint, IncompleteNotice, LoadMore, Spinner } from "../primitives";
import type { ListStatus } from "../../hooks/usePagedList";

/**
 * The shape every directory screen shares: groups, organizational units and computers differ in
 * their heading, their hint text and their columns — but not in how they work.
 *
 * The generic T is carried from items through to columns: columns for GroupDto cannot be drawn
 * over a list of computers, however alike the two lists look.
 */
export interface DirectoryViewProps<T> {
  title: string;
  hint: ReactNode;
  createLabel: string;
  onCreate: () => void;
  canManage: boolean;

  query: string;
  onQueryChange: (value: string) => void;
  searchPlaceholder: string;
  searchHint?: ReactNode;

  status: ListStatus;
  error: string | null;
  items: readonly T[];
  columns: ReadonlyArray<Column<T>>;
  emptyText: string;

  incompleteNoun: string;
  isComplete: boolean;
  hasMore: boolean;
  isLoadingMore: boolean;
  onLoadMore: () => void;

  /** A slot: which dialog to show is the container’s decision. */
  dialog?: ReactNode;
}

export function DirectoryView<T>({
  title,
  hint,
  createLabel,
  onCreate,
  canManage,
  query,
  onQueryChange,
  searchPlaceholder,
  searchHint,
  status,
  error,
  items,
  columns,
  emptyText,
  incompleteNoun,
  isComplete,
  hasMore,
  isLoadingMore,
  onLoadMore,
  dialog,
}: DirectoryViewProps<T>): JSX.Element {
  return (
    <div className="page">
      <PageHeader title={title} hint={hint}>
        {canManage && (
          <button className="btn btn--primary" onClick={onCreate}>
            {createLabel}
          </button>
        )}
      </PageHeader>

      <Card className="mb-16">
        <Card.Body>
          <div className="form-row">
            <input
              type="text"
              value={query}
              placeholder={searchPlaceholder}
              onChange={(event: ChangeEvent<HTMLInputElement>) => onQueryChange(event.target.value)}
            />
            {searchHint !== undefined && <FormHint>{searchHint}</FormHint>}
          </div>
        </Card.Body>
      </Card>

      {status === "loading" && <Spinner />}
      {status === "error" && <ErrorBanner>{error}</ErrorBanner>}
      {status === "ready" && (
        <div>
          {!isComplete && <IncompleteNotice noun={incompleteNoun} />}
          <Card>
            <DataTable columns={columns} items={items} emptyText={emptyText} />
            {hasMore && <LoadMore onClick={onLoadMore} isLoading={isLoadingMore} />}
          </Card>
        </div>
      )}

      {dialog}
    </div>
  );
}
