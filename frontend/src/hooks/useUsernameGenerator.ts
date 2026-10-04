import { useCallback, useEffect, useRef, useState } from "react";
import { t } from "../lib/i18n";
import { api } from "../api";
import type { PatternGeneratorResponse } from "../api/dto";
import { messageOf } from "../lib/errors";

export type GeneratorStatus = "idle" | "loading" | "ready" | "error";

/**
 * Form values rather than a request body: maxItems is a string here because <input type="number">
 * returns a string, and pretending otherwise means sending a NaN one day.
 */
export interface GeneratorFormValues {
  pattern: string;
  rawText: string;
  regexPattern: string;
  usernameTemplate: string;
  transliterate: boolean;
  checkConflicts: boolean;
  maxItems: string;
}

export interface UsernameGeneratorState {
  values: GeneratorFormValues;
  setField: <K extends keyof GeneratorFormValues>(field: K, value: GeneratorFormValues[K]) => void;
  status: GeneratorStatus;
  result: PatternGeneratorResponse | null;
  error: string | null;
  run: () => Promise<void>;
}

const INITIAL_VALUES: GeneratorFormValues = {
  pattern: "",
  rawText: "",
  regexPattern: "",
  usernameTemplate: "",
  transliterate: false,
  checkConflicts: false,
  maxItems: "50",
};

export function useUsernameGenerator(): UsernameGeneratorState {
  const [values, setValues] = useState<GeneratorFormValues>(INITIAL_VALUES);
  const [status, setStatus] = useState<GeneratorStatus>("idle");
  const [result, setResult] = useState<PatternGeneratorResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const isMounted = useRef<boolean>(true);

  useEffect(() => {
    isMounted.current = true;
    return () => {
      isMounted.current = false;
    };
  }, []);

  const setField = useCallback(
    <K extends keyof GeneratorFormValues>(field: K, value: GeneratorFormValues[K]): void => {
      setValues((previous) => ({ ...previous, [field]: value }));
    },
    []
  );

  const run = useCallback(async (): Promise<void> => {
    setStatus("loading");
    setError(null);
    setResult(null);
    try {
      const response = await api.users.generate({
        pattern: values.pattern,
        rawText: values.rawText || undefined,
        regexPattern: values.regexPattern || undefined,
        usernameTemplate: values.usernameTemplate || undefined,
        transliterate: values.transliterate,
        checkConflicts: values.checkConflicts,
        maxItems: Number.parseInt(values.maxItems, 10) || 50,
      });
      if (!isMounted.current) return;
      setResult(response);
      setStatus("ready");
    } catch (err: unknown) {
      if (!isMounted.current) return;
      setError(messageOf(err, t("generator.failed")));
      setStatus("error");
    }
  }, [values]);

  return { values, setField, status, result, error, run };
}
