import { api } from "../api";
import { t } from "../lib/i18n";
import type { RateLimitSettingsDto } from "../api/dto";
import { useCollection } from "../hooks/useCollection";
import { RateLimitsView } from "../components/rateLimits/RateLimitsView";
import { NewPolicyFormContainer } from "./NewPolicyFormContainer";
import { PolicyCardContainer } from "./PolicyCardContainer";
import type { JSX } from "react";

const loadPolicies = (): Promise<RateLimitSettingsDto[]> => api.rateLimits.getAll();

export function RateLimitsPage(): JSX.Element {
  const policies = useCollection<RateLimitSettingsDto>({
    load: loadPolicies,
    errorFallback: t("rateLimits.loadFailed"),
  });

  return (
    <RateLimitsView
      status={policies.status}
      error={policies.error}
      isEmpty={policies.items.length === 0}
      policies={policies.items.map((policy) => (
        <PolicyCardContainer key={policy.policyName} policy={policy} onSaved={policies.reload} />
      ))}
      newPolicyForm={<NewPolicyFormContainer onSaved={policies.reload} />}
    />
  );
}
