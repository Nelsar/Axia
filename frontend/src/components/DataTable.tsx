import type { CSSProperties, JSX, ReactNode } from "react";
import { t } from "../lib/i18n";

/**
 * A table column. The generic T ties `cell` to the row type: a column declared for ComputerDto
 * will not accept `item.email` — a computer has no such field, and that is caught by the compiler
 * rather than by an empty cell in production.
 */
export interface Column<T> {
  /** The React key and the column identifier; it plays no part in the row data. */
  key: string;
  header?: ReactNode;
  cell: (item: T) => ReactNode;
  className?: string | undefined;
  style?: CSSProperties | undefined;
  headerStyle?: CSSProperties | undefined;
}

export interface DataTableProps<T> {
  columns: ReadonlyArray<Column<T>>;
  items: readonly T[];
  emptyText?: string | undefined;
  getRowKey?: ((item: T, index: number) => string) | undefined;
  onRowClick?: ((item: T) => void) | undefined;
}

/** A directory row always has a string id — but not every listing is a directory one, hence the fallback. */
function defaultRowKey<T>(item: T, index: number): string {
  if (typeof item === "object" && item !== null) {
    const id: unknown = (item as Record<string, unknown>)["id"];
    if (typeof id === "string") return id;
  }
  return String(index);
}

/**
 * A table described by data: a column is a heading and a function that draws the cell.
 * `<DataTable<UserDto> …>` is inferred from items, so the explicit argument is rarely needed.
 */
export function DataTable<T>({
  columns,
  items,
  emptyText = t("common.nothingFound"),
  getRowKey = defaultRowKey,
  onRowClick,
}: DataTableProps<T>): JSX.Element {
  if (items.length === 0) {
    return <div className="card__pad text-muted">{emptyText}</div>;
  }

  return (
    <div className="table-wrap">
      <table className="data-table">
        <thead>
          <tr>
            {columns.map((column) => (
              <th key={column.key} style={column.headerStyle}>
                {column.header ?? ""}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {items.map((item, index) => (
            <tr
              key={getRowKey(item, index)}
              className={onRowClick ? "clickable" : undefined}
              onClick={onRowClick ? () => onRowClick(item) : undefined}
            >
              {columns.map((column) => (
                <td key={column.key} className={column.className} style={column.style}>
                  {column.cell(item)}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
