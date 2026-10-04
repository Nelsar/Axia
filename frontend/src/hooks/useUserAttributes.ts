import { useCallback, useEffect, useRef, useState } from "react";
import { t } from "../lib/i18n";
import { api } from "../api";
import type { DirectoryAttributeDto, Uuid } from "../api/dto";
import { messageOf } from "../lib/errors";

export type AttributesStatus = "idle" | "loading" | "ready" | "error";

export interface UserAttributesState {
  status: AttributesStatus;
  attributes: readonly DirectoryAttributeDto[];
  error: string | null;
  load: () => Promise<void>;
}

/**
 * An entry's raw attributes are a separate trip to the directory, made only on demand (the
 * "Show" button) rather than every time a card is opened.
 */
export function useUserAttributes(userId: Uuid): UserAttributesState {
  const [status, setStatus] = useState<AttributesStatus>("idle");
  const [attributes, setAttributes] = useState<DirectoryAttributeDto[]>([]);
  const [error, setError] = useState<string | null>(null);
  const isMounted = useRef<boolean>(true);

  useEffect(() => {
    isMounted.current = true;
    return () => {
      isMounted.current = false;
    };
  }, []);

  const load = useCallback(async (): Promise<void> => {
    setStatus("loading");
    setError(null);
    try {
      const result = await api.users.getAttributes(userId);
      if (!isMounted.current) return;
      setAttributes(result.attributes);
      setStatus("ready");
    } catch (err: unknown) {
      if (!isMounted.current) return;
      // A 501 comes from the ActiveDirectory provider, which does not read raw attributes. The
      // API's message explains what to do and is more use than an empty table.
      setError(messageOf(err, t("users.attributesFailed")));
      setStatus("error");
    }
  }, [userId]);

  return { status, attributes, error, load };
}
