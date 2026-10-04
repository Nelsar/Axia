import { useCallback, useEffect, useMemo, useState } from "react";
import { t } from "../lib/i18n";
import { api } from "../api";
import type { SelectableProviderDto } from "../api/dto";
import { getSessionDirectory } from "../lib/auth";
import type { SessionDirectory } from "../lib/auth";

export interface SelectableDirectoriesState {
  readonly options: readonly SelectableProviderDto[];
  /** An empty string is the default directory, which has no id and does not appear in the list. */
  readonly selectedId: string;
  readonly setSelectedId: (id: string) => void;
  readonly isLoading: boolean;
  /** The list failed to load. Signing in still works — but only into the default directory. */
  readonly error: string | null;
  /** What to put in the session after signing in: null when the default directory is chosen. */
  readonly selected: SessionDirectory | null;
}

/**
 * The directories a person can choose between on the sign-in form.
 *
 * The list comes from the anonymous /api/v1/providers/selectable, which exists for this screen,
 * where there is no token yet. Until it did, there was nothing to choose from: a directory id was
 * only returned by the administrative /api/v1/providers, so signing in to anything but the
 * default directory took someone dictating that id to you.
 *
 * The previous choice is preselected, but only if the directory is still in the list: an
 * administrator may have deactivated or deleted it, and quietly aiming a sign-in at a directory
 * that has vanished means a refusal with nothing to explain it by.
 */
export function useSelectableDirectories(): SelectableDirectoriesState {
  const [options, setOptions] = useState<readonly SelectableProviderDto[]>([]);
  const [selectedId, setSelectedId] = useState<string>("");
  const [isLoading, setIsLoading] = useState<boolean>(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    void (async (): Promise<void> => {
      try {
        const loaded = await api.providers.getSelectable();
        if (cancelled) return;

        setOptions(loaded);

        const remembered = getSessionDirectory();
        if (remembered !== null && loaded.some((option) => option.id === remembered.id)) {
          setSelectedId(remembered.id);
        }
      } catch {
        // Deliberately without the API's error text: the sign-in screen is no place to work out
        // why the directory list did not arrive, and signing in to the default directory keeps
        // working throughout.
        if (!cancelled) setError(t("login.directoriesFailed"));
      } finally {
        if (!cancelled) setIsLoading(false);
      }
    })();

    return () => {
      cancelled = true;
    };
  }, []);

  const selected = useMemo<SessionDirectory | null>(() => {
    const match = options.find((option) => option.id === selectedId);
    return match === undefined ? null : { id: match.id, name: match.name };
  }, [options, selectedId]);

  const select = useCallback((id: string): void => setSelectedId(id), []);

  return { options, selectedId, setSelectedId: select, isLoading, error, selected };
}
