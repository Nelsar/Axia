import type { ChangeEvent, CSSProperties, FormEvent, HTMLInputTypeAttribute } from "react";
import { t } from "../../lib/i18n";
import type { FormErrors } from "../../hooks/useEntityForm";
import type { NewPolicyFormValues } from "../../containers/NewPolicyFormContainer";
import { Card, ErrorBanner } from "../primitives";

const GRID: CSSProperties = {
  display: "grid",
  gridTemplateColumns: "1.2fr repeat(3, 1fr) auto",
  gap: "12px",
  alignItems: "end",
};

export interface NewPolicyFormProps {
  values: NewPolicyFormValues;
  errors: FormErrors<NewPolicyFormValues>;
  submitError: string | null;
  isSubmitting: boolean;
  onFieldChange: <K extends keyof NewPolicyFormValues>(field: K, value: NewPolicyFormValues[K]) => void;
  onSubmit: (event?: FormEvent<HTMLFormElement>) => void;
}

interface FieldProps {
  label: string;
  type?: HTMLInputTypeAttribute | undefined;
  value: string;
  error?: string | undefined;
  placeholder?: string | undefined;
  onChange: (value: string) => void;
}

function Field({ label, type = "number", value, error, placeholder, onChange }: FieldProps): JSX.Element {
  return (
    <div className="form-row">
      <label className="form-label">{label}</label>
      <input
        type={type}
        value={value}
        placeholder={placeholder}
        onChange={(event: ChangeEvent<HTMLInputElement>) => onChange(event.target.value)}
      />
      {error !== undefined && (
        <div className="text-danger" style={{ fontSize: "12.5px" }}>
          {error}
        </div>
      )}
    </div>
  );
}

export function NewPolicyForm({
  values,
  errors,
  submitError,
  isSubmitting,
  onFieldChange,
  onSubmit,
}: NewPolicyFormProps): JSX.Element {
  return (
    <Card>
      <Card.Header>{t("rateLimits.newPolicy")}</Card.Header>
      <form className="card__pad form-grid" style={GRID} onSubmit={onSubmit} noValidate>
        <Field
          label={t("rateLimits.policyName")}
          type="text"
          value={values.policyName}
          error={errors.policyName}
          placeholder="auth-login"
          onChange={(value) => onFieldChange("policyName", value)}
        />
        <Field
          label={t("rateLimits.limit")}
          value={values.permitLimit}
          onChange={(value) => onFieldChange("permitLimit", value)}
        />
        <Field
          label={t("rateLimits.window")}
          value={values.windowSeconds}
          onChange={(value) => onFieldChange("windowSeconds", value)}
        />
        <Field
          label={t("rateLimits.queue")}
          value={values.queueLimit}
          onChange={(value) => onFieldChange("queueLimit", value)}
        />
        <button className="btn btn--primary" type="submit" disabled={isSubmitting}>
          {isSubmitting ? t("common.creating") : t("common.create")}
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
