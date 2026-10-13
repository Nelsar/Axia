import { useCallback, type JSX } from "react";
import { t } from "../lib/i18n";
import { api } from "../api";
import type { RateLimitSettingsDto } from "../api/dto";
import { toast } from "../lib/ui";
import { useEntityForm } from "../hooks/useEntityForm";
import { PolicyCard } from "../components/rateLimits/PolicyCard";

export interface PolicyFormValues {
  permitLimit: string;
  windowSeconds: string;
  queueLimit: string;
  enabled: boolean;
}

export interface PolicyCardContainerProps {
  policy: RateLimitSettingsDto;
  onSaved: () => void;
}

export function PolicyCardContainer({ policy, onSaved }: PolicyCardContainerProps): JSX.Element {
  const onSubmit = useCallback(
    async (values: PolicyFormValues): Promise<void> => {
      await api.rateLimits.upsert(policy.policyName, {
        permitLimit: Number.parseInt(values.permitLimit, 10),
        windowSeconds: Number.parseInt(values.windowSeconds, 10),
        queueLimit: Number.parseInt(values.queueLimit, 10),
        enabled: values.enabled,
      });
      toast(t("rateLimits.saved"), { type: "success" });
    },
    [policy.policyName]
  );

  const form = useEntityForm<PolicyFormValues>({
    initialValues: {
      permitLimit: String(policy.permitLimit),
      windowSeconds: String(policy.windowSeconds),
      queueLimit: String(policy.queueLimit),
      enabled: policy.enabled,
    },
    onSubmit,
    onSuccess: onSaved,
  });

  return (
    <PolicyCard
      policyName={policy.policyName}
      values={form.values}
      submitError={form.submitError}
      isSubmitting={form.isSubmitting}
      onFieldChange={form.setField}
      onSubmit={form.submit}
    />
  );
}
