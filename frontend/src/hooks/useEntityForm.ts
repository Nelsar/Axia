import { useCallback, useEffect, useRef, useState } from "react";
import { t } from "../lib/i18n";
import type { Dispatch, FormEvent, SetStateAction } from "react";
import { messageOf } from "../lib/errors";

/** A field error, only for this form's fields: a typo in a field name does not compile. */
export type FormErrors<TValues> = Partial<Record<keyof TValues, string>>;

export type Validate<TValues> = (values: TValues) => FormErrors<TValues>;

export interface EntityFormState<TValues> {
  values: TValues;
  errors: FormErrors<TValues>;
  submitError: string | null;
  isSubmitting: boolean;
  /**
   * The value is tied to the particular field: setField("enabled", "yes") does not typecheck,
   * because TValues["enabled"] is a boolean.
   */
  setField: <K extends keyof TValues>(field: K, value: TValues[K]) => void;
  setValues: Dispatch<SetStateAction<TValues>>;
  submit: (event?: FormEvent<HTMLFormElement>) => Promise<void>;
}

export interface UseEntityFormOptions<TValues> {
  initialValues: TValues;
  validate?: Validate<TValues> | undefined;
  onSubmit: (values: TValues) => Promise<void>;
  onSuccess?: (() => void) | undefined;
  errorFallback?: string | undefined;
}

/**
 * The mechanics every form shares: field values, validation before submitting, submitting, and
 * the API's error. What is validated and what goes to the API is the caller's to say; what
 * repeats across the console's forms is this behaviour, not the rules.
 */
export function useEntityForm<TValues extends object>({
  initialValues,
  validate,
  onSubmit,
  onSuccess,
  errorFallback = t("common.saveFailed"),
}: UseEntityFormOptions<TValues>): EntityFormState<TValues> {
  const [values, setValues] = useState<TValues>(initialValues);
  const [errors, setErrors] = useState<FormErrors<TValues>>({});
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState<boolean>(false);

  const isMounted = useRef<boolean>(true);
  useEffect(() => {
    isMounted.current = true;
    return () => {
      isMounted.current = false;
    };
  }, []);

  const setField = useCallback(<K extends keyof TValues>(field: K, value: TValues[K]): void => {
    setValues((previous) => ({ ...previous, [field]: value }));
    // A field's error is cleared as soon as it is edited: holding it until the next submit means
    // complaining about something the person is already fixing. delete rather than assigning
    // undefined because of exactOptionalPropertyTypes: "no key" and "a key holding undefined" are
    // not the same thing here.
    setErrors((previous) => {
      if (previous[field] === undefined) return previous;
      const next: FormErrors<TValues> = { ...previous };
      delete next[field];
      return next;
    });
  }, []);

  const submit = useCallback(
    async (event?: FormEvent<HTMLFormElement>): Promise<void> => {
      event?.preventDefault();

      const found: FormErrors<TValues> = validate ? validate(values) : {};
      setErrors(found);
      if (Object.values(found).some((message) => message !== undefined)) return;

      setIsSubmitting(true);
      setSubmitError(null);
      try {
        await onSubmit(values);
        onSuccess?.();
      } catch (error: unknown) {
        if (isMounted.current) setSubmitError(messageOf(error, errorFallback));
      } finally {
        if (isMounted.current) setIsSubmitting(false);
      }
    },
    [errorFallback, onSubmit, onSuccess, validate, values]
  );

  return { values, errors, submitError, isSubmitting, setField, setValues, submit };
}
