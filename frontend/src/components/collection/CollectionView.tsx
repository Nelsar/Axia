import type { ReactNode } from "react";
import { DataTable } from "../DataTable";
import type { Column } from "../DataTable";
import { PageHeader } from "../PageHeader";
import { Card, ErrorBanner, Spinner } from "../primitives";
import type { ListStatus } from "../../hooks/usePagedList";

/**
 * A reference-list screen: a heading, a create button, a table and a dialog slot. What separates
 * it from DirectoryView is that there is neither search nor pagination — the API returns the
 * whole list.
 */
export interface CollectionViewProps<T> {
  title: string;
  hint: ReactNode;
  createLabel?: string | undefined;
  onCreate?: (() => void) | undefined;
  status: ListStatus;
  error: string | null;
  items: readonly T[];
  columns: ReadonlyArray<Column<T>>;
  emptyText: string;
  dialog?: ReactNode;
}

export function CollectionView<T>({
  title,
  hint,
  createLabel,
  onCreate,
  status,
  error,
  items,
  columns,
  emptyText,
  dialog,
}: CollectionViewProps<T>): JSX.Element {
  return (
    <div className="page">
      <PageHeader title={title} hint={hint}>
        {createLabel !== undefined && onCreate !== undefined && (
          <button className="btn btn--primary" onClick={onCreate}>
            {createLabel}
          </button>
        )}
      </PageHeader>

      {status === "loading" && <Spinner />}
      {status === "error" && <ErrorBanner>{error}</ErrorBanner>}
      {status === "ready" && (
        <Card>
          <DataTable columns={columns} items={items} emptyText={emptyText} />
        </Card>
      )}

      {dialog}
    </div>
  );
}
