import type { ChangeEvent, CSSProperties, FormEvent } from "react";
import { t } from "../../lib/i18n";
import type { PolicyFormValues } from "../../containers/PolicyCardContainer";
import { Badge, Card, ErrorBanner } from "../primitives";

const GRID: CSSProperties = {
  display: "grid",
  gridTemplateColumns: "repeat(4, 1fr) auto",
  gap: "12px",
  alignItems: "end",
};

export interface PolicyCardProps {
  policyName: string;
  values: PolicyFormValues;
  submitError: string | null;
  isSubmitting: boolean;
  onFieldChange: <K extends keyof PolicyFormValues>(field: K, value: PolicyFormValues[K]) => void;
  onSubmit: (event?: FormEvent<HTMLFormElement>) => void;
}

interface NumberFieldProps {
  label: string;
  value: string;
  onChange: (value: string) => void;
}

function NumberField({ label, value, onChange }: NumberFieldProps): JSX.Element {
  return (
    <div className="form-row">
      <label className="form-label">{label}</label>
      <input
        type="number"
        value={value}
        onChange={(event: ChangeEvent<HTMLInputElement>) => onChange(event.target.value)}
      />
    </div>
  );
}

export function PolicyCard({
  policyName,
  values,
  submitError,
  isSubmitting,
  onFieldChange,
  onSubmit,
}: PolicyCardProps): JSX.Element {
  return (
    <Card>
      <Card.Header>
        <span className="cell-mono">{policyName}</span>
        {values.enabled ? <Badge tone="success">{t("common.enabled")}</Badge> : <Badge tone="neutral">{t("common.disabled")}</Badge>}
      </Card.Header>
      <form className="card__pad form-grid" style={GRID} onSubmit={onSubmit}>
        <NumberField
          label={t("rateLimits.limit")}
          value={values.permitLimit}
          onChange={(value) => onFieldChange("permitLimit", value)}
        />
        <NumberField
          label={t("rateLimits.window")}
          value={values.windowSeconds}
          onChange={(value) => onFieldChange("windowSeconds", value)}
        />
        <NumberField
          label={t("rateLimits.queue")}
          value={values.queueLimit}
          onChange={(value) => onFieldChange("queueLimit", value)}
        />
        <label className="checkbox" style={{ height: "34px" }}>
          <input
            type="checkbox"
            checked={values.enabled}
            onChange={(event: ChangeEvent<HTMLInputElement>) => onFieldChange("enabled", event.target.checked)}
          />{" "}
          {t("rateLimits.enabledLabel")}
        </label>
        <button className="btn btn--primary" type="submit" disabled={isSubmitting}>
          {isSubmitting ? t("common.saving") : t("common.save")}
        </button>
      </form>
      {submitError !== null && (
        <Card.Body>
          <ErrorBanner>{submitError}</ErrorBanner>
        </Card.Body>
      )}
    </Card>
  );
}
