import type { ChangeEvent, CSSProperties, FormEvent } from "react";
import { t } from "../../lib/i18n";
import type { LdapSecurity, ProviderType } from "../../api/dto";
import type { FormErrors } from "../../hooks/useEntityForm";
import type { ProviderFormValues } from "../../containers/ProviderFormDialogContainer";
import { Modal } from "../Modal";
import { ErrorBanner, FormRow } from "../primitives";

const PROVIDER_TYPES: ReadonlyArray<{ value: ProviderType; label: string }> = [
  { value: "StandardLdap", label: "StandardLdap (AD/OpenLDAP/eDirectory)" },
  { value: "ActiveDirectory", label: "ActiveDirectory (Windows-only)" },
];

/** Labels are looked up when rendered rather than here, so they follow a change of language. */
const SECURITY_MODES: ReadonlyArray<{ value: LdapSecurity; label: () => string }> = [
  { value: "Ldaps", label: () => t("providers.securityLdaps") },
  { value: "StartTls", label: () => t("providers.securityStartTls") },
  { value: "None", label: () => t("providers.securityNone") },
];

const twoColumns: CSSProperties = { display: "grid", gridTemplateColumns: "1fr 1fr", gap: "10px" };

export interface ProviderFormDialogProps {
  isEdit: boolean;
  title: string;
  values: ProviderFormValues;
  errors: FormErrors<ProviderFormValues>;
  submitError: string | null;
  isSubmitting: boolean;
  onFieldChange: <K extends keyof ProviderFormValues>(field: K, value: ProviderFormValues[K]) => void;
  onSubmit: (event?: FormEvent<HTMLFormElement>) => void;
  onClose: () => void;
}

/** The form fields whose value is free text (unlike security, providerType and isActive). */
type TextField = Exclude<keyof ProviderFormValues, "security" | "providerType" | "isActive">;

export function ProviderFormDialog({
  isEdit,
  title,
  values,
  errors,
  submitError,
  isSubmitting,
  onFieldChange,
  onSubmit,
  onClose,
}: ProviderFormDialogProps): JSX.Element {
  const onText =
    (field: TextField) =>
    (event: ChangeEvent<HTMLInputElement>): void =>
      onFieldChange(field, event.target.value);

  return (
    <Modal title={title} width="560px" onClose={onClose}>
      <form className="form-grid" onSubmit={onSubmit} noValidate>
        <FormRow label={t("common.name")} error={errors.name}>
          <input type="text" value={values.name} autoFocus onChange={onText("name")} />
        </FormRow>

        <div className="form-row" style={{ display: "grid", gridTemplateColumns: "2fr 1fr", gap: "10px" }}>
          <FormRow label={t("providers.host")} error={errors.host}>
            <input type="text" value={values.host} onChange={onText("host")} />
          </FormRow>
          <FormRow label={t("providers.port")} error={errors.port}>
            <input type="number" value={values.port} onChange={onText("port")} />
          </FormRow>
        </div>

        <FormRow
          label={t("providers.security")}
          hint={
            values.security === "None" ? (
              <span className="text-danger">{t("providers.securityNoneWarning")}</span>
            ) : undefined
          }
        >
          <select
            value={values.security}
            onChange={(event: ChangeEvent<HTMLSelectElement>) => {
              const next = SECURITY_MODES.find((mode) => mode.value === event.target.value);
              if (next !== undefined) onFieldChange("security", next.value);
            }}
          >
            {SECURITY_MODES.map((mode) => (
              <option key={mode.value} value={mode.value}>
                {mode.label()}
              </option>
            ))}
          </select>
        </FormRow>

        <FormRow label={t("audit.columnType")}>
          {/* A <select> value is a string; narrowing it to ProviderType here is safer than
              casting in the container, because the list of options is right next to it. */}
          <select
            value={values.providerType}
            onChange={(event: ChangeEvent<HTMLSelectElement>) => {
              const next = PROVIDER_TYPES.find((type) => type.value === event.target.value);
              if (next !== undefined) onFieldChange("providerType", next.value);
            }}
          >
            {PROVIDER_TYPES.map((type) => (
              <option key={type.value} value={type.value}>
                {type.label}
              </option>
            ))}
          </select>
        </FormRow>

        <FormRow label={t("providers.failoverHosts")}>
          <input type="text" value={values.failoverHosts} onChange={onText("failoverHosts")} />
        </FormRow>

        <FormRow label="Base DN">
          <input type="text" value={values.baseDn} onChange={onText("baseDn")} />
        </FormRow>

        <div className="form-row" style={twoColumns}>
          <FormRow label={t("providers.bindUser")}>
            <input type="text" value={values.adminUsername} onChange={onText("adminUsername")} />
          </FormRow>
          <FormRow label={isEdit ? t("providers.bindPasswordKeep") : t("providers.bindPassword")}>
            <input
              type="password"
              value={values.adminPassword}
              autoComplete="new-password"
              onChange={onText("adminPassword")}
            />
          </FormRow>
        </div>

        <FormRow label={t("providers.searchFilter")}>
          <input type="text" value={values.userSearchFilter} onChange={onText("userSearchFilter")} />
        </FormRow>

        <div className="form-row" style={{ ...twoColumns, alignItems: "center" }}>
          <FormRow label={t("providers.pageSize")}>
            <input type="number" value={values.userSearchPageSize} onChange={onText("userSearchPageSize")} />
          </FormRow>
          <label className="checkbox" style={{ marginTop: "18px" }}>
            <input
              type="checkbox"
              checked={values.isActive}
              onChange={(event: ChangeEvent<HTMLInputElement>) => onFieldChange("isActive", event.target.checked)}
            />{" "}
            {t("providers.active")}
          </label>
        </div>

        {submitError !== null && <ErrorBanner>{submitError}</ErrorBanner>}

        <Modal.Actions>
          <button type="button" className="btn" onClick={onClose} disabled={isSubmitting}>{t("common.cancel")}</button>
          <button type="submit" className="btn btn--primary" disabled={isSubmitting}>
            {isSubmitting ? t("common.saving") : isEdit ? t("common.save") : t("common.create")}
          </button>
        </Modal.Actions>
      </form>
    </Modal>
  );
}
