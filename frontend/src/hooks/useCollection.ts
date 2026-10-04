import { useCallback, useEffect, useRef, useState } from "react";
import { t } from "../lib/i18n";
import { messageOf } from "../lib/errors";
import type { ListStatus } from "./usePagedList";

export interface CollectionState<T> {
  items: readonly T[];
  status: ListStatus;
  error: string | null;
  reload: () => void;
}

export interface UseCollectionOptions<T> {
  load: () => Promise<T[]>;
  errorFallback?: string | undefined;
}

/** A list with no pagination: providers, role mappings, rate limiting policies. */
export function useCollection<T>({
  load,
  errorFallback = t("common.loadFailed"),
}: UseCollectionOptions<T>): CollectionState<T> {
  const [items, setItems] = useState<T[]>([]);
  const [status, setStatus] = useState<ListStatus>("loading");
  const [error, setError] = useState<string | null>(null);
  const [reloadKey, setReloadKey] = useState<number>(0);
  const requestId = useRef<number>(0);

  useEffect(() => {
    const ticket = ++requestId.current;
    setStatus("loading");
    setError(null);

    load()
      .then((data: T[]) => {
        if (ticket !== requestId.current) return;
        setItems(data);
        setStatus("ready");
      })
      .catch((err: unknown) => {
        if (ticket !== requestId.current) return;
        setError(messageOf(err, errorFallback));
        setStatus("error");
      });

    return () => {
      requestId.current++;
    };
  }, [load, errorFallback, reloadKey]);

  const reload = useCallback((): void => setReloadKey((value) => value + 1), []);

  return { items, status, error, reload };
}
