import { useCallback, useEffect, useRef, useState } from "react";
import { t } from "../lib/i18n";
import type { PagedResponse } from "../api/dto";
import { messageOf } from "../lib/errors";
import { toast } from "../lib/ui";

const DEBOUNCE_MS = 350;
const DEFAULT_PAGE_SIZE = 50;

export type ListStatus = "loading" | "ready" | "error";

export interface PageRequest {
  filter?: string | undefined;
  pageSize: number;
  pageToken?: string | undefined;
}

/**
 * The adapter for fetching a page. It exists because the api.* signatures differ:
 * search(filter, size, token) against getRecent(size, token). The generic T is the element type
 * and it flows into items, so a directory screen cannot accidentally draw groups in the users
 * table.
 */
export type FetchPage<T> = (request: PageRequest) => Promise<PagedResponse<T>>;

export interface PagedListState<T> {
  query: string;
  setQuery: (value: string) => void;
  items: readonly T[];
  status: ListStatus;
  error: string | null;
  isComplete: boolean;
  hasMore: boolean;
  isLoadingMore: boolean;
  loadMore: () => void;
  reload: () => void;
}

export interface UsePagedListOptions<T> {
  fetchPage: FetchPage<T>;
  pageSize?: number | undefined;
  errorFallback?: string | undefined;
  loadMoreFallback?: string | undefined;
}

/**
 * A paged directory listing: a debounced filter, the first page, loading further pages, and
 * reloading. One hook for every list in the console — users, groups, OUs, computers, audit.
 */
export function usePagedList<T>({
  fetchPage,
  pageSize = DEFAULT_PAGE_SIZE,
  errorFallback = t("common.loadFailed"),
  loadMoreFallback = t("list.loadMoreFailed"),
}: UsePagedListOptions<T>): PagedListState<T> {
  const [query, setQuery] = useState<string>("");
  const [appliedQuery, setAppliedQuery] = useState<string>("");
  const [items, setItems] = useState<T[]>([]);
  const [pageToken, setPageToken] = useState<string | null>(null);
  const [hasMore, setHasMore] = useState<boolean>(false);
  // isComplete === false means the directory walk stopped early and the list is incomplete. The
  // default is true: a response that says nothing about it is complete by definition.
  const [isComplete, setIsComplete] = useState<boolean>(true);
  const [status, setStatus] = useState<ListStatus>("loading");
  const [isLoadingMore, setIsLoadingMore] = useState<boolean>(false);
  const [error, setError] = useState<string | null>(null);
  const [reloadKey, setReloadKey] = useState<number>(0);

  // The number of the current request. A search on a short string can answer more slowly than one
  // on a long string, and without this counter the response for "iv" could overwrite the list for
  // "ivanov".
  const requestId = useRef<number>(0);

  useEffect(() => {
    const timer = window.setTimeout(() => setAppliedQuery(query.trim()), DEBOUNCE_MS);
    return () => window.clearTimeout(timer);
  }, [query]);

  useEffect(() => {
    const ticket = ++requestId.current;
    setStatus("loading");
    setError(null);

    fetchPage({ filter: appliedQuery || undefined, pageSize })
      .then((page: PagedResponse<T>) => {
        if (ticket !== requestId.current) return;
        setItems(page.items);
        setPageToken(page.pageToken);
        setHasMore(page.hasMore);
        setIsComplete(page.isComplete !== false);
        setStatus("ready");
      })
      .catch((err: unknown) => {
        if (ticket !== requestId.current) return;
        setError(messageOf(err, errorFallback));
        setStatus("error");
      });

    // Unmounting, or a newer request, makes an answer still in flight worthless.
    return () => {
      requestId.current++;
    };
  }, [appliedQuery, reloadKey, pageSize, fetchPage, errorFallback]);

  const loadMore = useCallback((): void => {
    if (!hasMore || isLoadingMore || pageToken === null) return;
    const ticket = requestId.current;
    setIsLoadingMore(true);

    fetchPage({ filter: appliedQuery || undefined, pageSize, pageToken })
      .then((page: PagedResponse<T>) => {
        if (ticket !== requestId.current) return;
        setItems((previous) => previous.concat(page.items));
        setPageToken(page.pageToken);
        setHasMore(page.hasMore);
        setIsComplete(page.isComplete !== false);
      })
      .catch((err: unknown) => {
        if (ticket === requestId.current) toast(messageOf(err, loadMoreFallback), { type: "error" });
      })
      .finally(() => setIsLoadingMore(false));
  }, [appliedQuery, fetchPage, hasMore, isLoadingMore, loadMoreFallback, pageSize, pageToken]);

  const reload = useCallback((): void => setReloadKey((value) => value + 1), []);

  return {
    query,
    setQuery,
    items,
    status,
    error,
    isComplete,
    hasMore,
    isLoadingMore,
    loadMore,
    reload,
  };
}
