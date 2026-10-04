import type { ReactNode } from "react";
import { t } from "../../lib/i18n";
import type { ListStatus } from "../../hooks/usePagedList";
import { PageHeader } from "../PageHeader";
import { Card, ErrorBanner, Spinner } from "../primitives";

/**
 * The policies screen is a set of card-forms. Each card is a form with state of its own, so the
 * view receives them as finished elements (the `policies` slot) rather than as data: which
 * component holds a particular card's state is none of its business.
 */
export interface RateLimitsViewProps {
  status: ListStatus;
  error: string | null;
  isEmpty: boolean;
  policies: ReactNode;
  newPolicyForm: ReactNode;
}

export function RateLimitsView({
  status,
  error,
  isEmpty,
  policies,
  newPolicyForm,
}: RateLimitsViewProps): JSX.Element {
  return (
    <div className="page">
      <PageHeader title="Rate limiting" hint={t("rateLimits.hint")} />

      {status === "loading" && <Spinner />}
      {status === "error" && <ErrorBanner>{error}</ErrorBanner>}
      {status === "ready" && (
        <div style={{ display: "flex", flexDirection: "column", gap: "16px" }}>
          {isEmpty ? (
            <Card>
              <Card.Body className="text-muted">{t("rateLimits.empty")}</Card.Body>
            </Card>
          ) : (
            policies
          )}
          {newPolicyForm}
        </div>
      )}
    </div>
  );
}
