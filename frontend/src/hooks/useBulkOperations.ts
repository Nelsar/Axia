import { useCallback, useEffect, useRef, useState } from "react";
import { t } from "../lib/i18n";
import { api } from "../api";
import type { BulkOperationResponse, BulkUserRequest } from "../api/dto";
import { messageOf } from "../lib/errors";

const POLL_INTERVAL_MS = 1500;
const POLL_ATTEMPTS = 30;

const sleep = (ms: number): Promise<void> => new Promise((resolve) => window.setTimeout(resolve, ms));

export type BulkStatus = "idle" | "running" | "polling" | "done" | "error";

export interface BulkOperationsState {
  input: string;
  setInput: (value: string) => void;
  status: BulkStatus;
  isBusy: boolean;
  notice: string | null;
  error: string | null;
  result: BulkOperationResponse | null;
  runSync: () => Promise<void>;
  runAsync: () => Promise<void>;
}

/**
 * JSON typed by an administrator is outside data, so parsing returns unknown and is narrowed by
 * a check. From there the items go to the API as they are: their fields are the backend's to
 * validate, not the console's, and refusing here what the server would accept would be lying
 * about the rules.
 */
function parseOperations(raw: string): BulkUserRequest[] | { error: string } {
  let parsed: unknown;
  try {
    parsed = JSON.parse(raw.trim() === "" ? "[]" : raw) as unknown;
  } catch (err: unknown) {
    return { error: t("bulk.invalidJson", { reason: err instanceof Error ? err.message : t("bulk.parseFailed") }) };
  }

  if (!Array.isArray(parsed) || parsed.length === 0) {
    return { error: t("bulk.expectedArray") };
  }
  if (!parsed.every((item): item is Record<string, unknown> => typeof item === "object" && item !== null)) {
    return { error: t("bulk.expectedObjects") };
  }
  return parsed as unknown as BulkUserRequest[];
}

/** Bulk operations: parsing the JSON, running it synchronously, and enqueuing it with status polling. */
export function useBulkOperations(): BulkOperationsState {
  const [input, setInput] = useState<string>("");
  const [status, setStatus] = useState<BulkStatus>("idle");
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [result, setResult] = useState<BulkOperationResponse | null>(null);

  // Polling a job runs for up to 45 seconds. Leaving the page has to stop it, or it keeps calling
  // the API and writing into the state of a screen that is already unmounted.
  const isMounted = useRef<boolean>(true);
  useEffect(() => {
    isMounted.current = true;
    return () => {
      isMounted.current = false;
    };
  }, []);

  const reset = (): void => {
    setError(null);
    setNotice(null);
    setResult(null);
  };

  const readOperations = useCallback((): BulkUserRequest[] | null => {
    const operations = parseOperations(input);
    if ("error" in operations) {
      reset();
      setError(operations.error);
      setStatus("error");
      return null;
    }
    return operations;
  }, [input]);

  const runSync = useCallback(async (): Promise<void> => {
    const operations = readOperations();
    if (operations === null) return;
    reset();
    setStatus("running");
    try {
      const response = await api.users.bulkSync(operations);
      if (!isMounted.current) return;
      setResult(response);
      setStatus("done");
    } catch (err: unknown) {
      if (!isMounted.current) return;
      setError(messageOf(err, t("bulk.runFailed")));
      setStatus("error");
    }
  }, [readOperations]);

  const runAsync = useCallback(async (): Promise<void> => {
    const operations = readOperations();
    if (operations === null) return;
    reset();
    setStatus("running");

    let jobId: string;
    try {
      ({ jobId } = await api.users.bulkEnqueue(operations));
    } catch (err: unknown) {
      if (!isMounted.current) return;
      setError(messageOf(err, t("bulk.enqueueFailed")));
      setStatus("error");
      return;
    }
    if (!isMounted.current) return;

    setNotice(t("bulk.enqueued", { jobId }));
    setStatus("polling");

    for (let attempt = 0; attempt < POLL_ATTEMPTS; attempt++) {
      await sleep(POLL_INTERVAL_MS);
      if (!isMounted.current) return;
      try {
        const jobStatus = await api.users.bulkStatus(jobId);
        if (!isMounted.current) return;
        if (jobStatus === null) continue;
        if (jobStatus.state === "completed" || jobStatus.state === "failed") {
          setResult(jobStatus);
          setNotice(null);
          setStatus("done");
          return;
        }
        setNotice(t("bulk.jobState", { state: jobStatus.state }));
      } catch {
        // A failed status poll is not worth overwriting an already-shown jobId with an error: the
        // job is still in the queue and can be looked at later.
        return;
      }
    }

    if (isMounted.current) {
      setNotice(t("bulk.stillRunning", { jobId }));
    }
  }, [readOperations]);

  return {
    input,
    setInput,
    status,
    isBusy: status === "running" || status === "polling",
    notice,
    error,
    result,
    runSync,
    runAsync,
  };
}
