import { useCallback } from "react";
import { t } from "../lib/i18n";
import { api } from "../api";
import { toast } from "../lib/ui";
import { useEntityForm } from "../hooks/useEntityForm";
import type { FormErrors } from "../hooks/useEntityForm";
import { NewPolicyForm } from "../components/rateLimits/NewPolicyForm";

export interface NewPolicyFormValues {
  policyName: string;
  permitLimit: string;
  windowSeconds: string;
  queueLimit: string;
}

export interface NewPolicyFormContainerProps {
  onSaved: () => void;
}

const INITIAL_VALUES: NewPolicyFormValues = {
  policyName: "",
  permitLimit: "5",
  windowSeconds: "60",
  queueLimit: "0",
};

function validate(values: NewPolicyFormValues): FormErrors<NewPolicyFormValues> {
  const errors: FormErrors<NewPolicyFormValues> = {};
  // The policy name is part of the path PUT /api/v1/rate-limit-settings/{policyName}: without it
  // the request goes nowhere, and the vanilla version simply did nothing, quietly.
  if (values.policyName.trim() === "") errors.policyName = t("rateLimits.errorNameRequired");
  return errors;
}

export function NewPolicyFormContainer({ onSaved }: NewPolicyFormContainerProps): JSX.Element {
  const onSubmit = useCallback(async (values: NewPolicyFormValues): Promise<void> => {
    await api.rateLimits.upsert(values.policyName.trim(), {
      permitLimit: Number.parseInt(values.permitLimit, 10) || 5,
      windowSeconds: Number.parseInt(values.windowSeconds, 10) || 60,
      queueLimit: Number.parseInt(values.queueLimit, 10) || 0,
      enabled: true,
    });
    toast(t("rateLimits.created"), { type: "success" });
  }, []);

  const form = useEntityForm<NewPolicyFormValues>({
    initialValues: INITIAL_VALUES,
    validate,
    onSubmit,
    onSuccess: onSaved,
    errorFallback: t("rateLimits.createFailed"),
  });

  return (
    <NewPolicyForm
      values={form.values}
      errors={form.errors}
      submitError={form.submitError}
      isSubmitting={form.isSubmitting}
      onFieldChange={form.setField}
      onSubmit={form.submit}
    />
  );
}
