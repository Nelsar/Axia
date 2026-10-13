import { useCallback, useState } from "react";
import { t } from "../lib/i18n";
import { API_ADDRESS_IS_FIXED, getApiBaseUrl, getDefaultApiBaseUrl, setApiBaseUrl } from "../lib/config";
import { toast } from "../lib/ui";

export interface ApiBaseUrlState {
  value: string;
  setValue: (value: string) => void;
  saveIfChanged: () => void;
  placeholder: string;
  /** False where the build decides the address; see API_ADDRESS_IS_FIXED. */
  editable: boolean;
}

/** The backend address is edited on the sign-in screen and lives in localStorage. */
export function useApiBaseUrl(): ApiBaseUrlState {
  const [value, setValue] = useState<string>(() => getApiBaseUrl());

  // Saved on leaving the field, but only if the address actually changed: an "address saved"
  // toast on every blur would report an event that did not happen.
  const saveIfChanged = useCallback((): void => {
    const next = value.trim() || getDefaultApiBaseUrl();
    if (next.replace(/\/+$/, "") === getApiBaseUrl()) return;
    setApiBaseUrl(next);
    setValue(getApiBaseUrl());
    toast(t("login.apiUrlSaved"), { type: "success" });
  }, [value]);

  return { value, setValue, saveIfChanged, placeholder: getDefaultApiBaseUrl(), editable: !API_ADDRESS_IS_FIXED };
}
