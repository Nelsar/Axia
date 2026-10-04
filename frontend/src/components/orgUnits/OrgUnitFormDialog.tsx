import type { ChangeEvent, FormEvent } from "react";
import { t } from "../../lib/i18n";
import type { OrganizationalUnitDto } from "../../api/dto";
import type { FormErrors } from "../../hooks/useEntityForm";
import type { OrgUnitFormValues } from "../../containers/OrgUnitFormDialogContainer";
import { Modal } from "../Modal";
import { ErrorBanner, FormHint, FormRow } from "../primitives";
import { ParentDnField } from "../directory/ParentDnField";

export interface OrgUnitFormDialogProps {
  isEdit: boolean;
  unit: OrganizationalUnitDto | null;
  values: OrgUnitFormValues;
  errors: FormErrors<OrgUnitFormValues>;
  submitError: string | null;
  isSubmitting: boolean;
  parentOptions: readonly string[];
  onFieldChange: <K extends keyof OrgUnitFormValues>(field: K, value: OrgUnitFormValues[K]) => void;
  onSubmit: (event?: FormEvent<HTMLFormElement>) => void;
  onClose: () => void;
}

export function OrgUnitFormDialog({
  isEdit,
  unit,
  values,
  errors,
  submitError,
  isSubmitting,
  parentOptions,
  onFieldChange,
  onSubmit,
  onClose,
}: OrgUnitFormDialogProps): JSX.Element {
  return (
    <Modal title={isEdit ? t("orgUnits.dialogEdit") : t("orgUnits.dialogCreate")} onClose={onClose}>
      <form className="form-grid" onSubmit={onSubmit} noValidate>
        <FormRow label={t("orgUnits.nameLabel")} error={errors.name}>
          <input
            type="text"
            value={values.name}
            placeholder={t("orgUnits.namePlaceholder")}
            autoFocus
            onChange={(event: ChangeEvent<HTMLInputElement>) => onFieldChange("name", event.target.value)}
          />
        </FormRow>

        <ParentDnField
          label={isEdit ? t("common.parentDn") : t("common.parentDnWithDefault")}
          value={values.parentDistinguishedName}
          onChange={(value: string) => onFieldChange("parentDistinguishedName", value)}
          options={parentOptions}
          placeholder="OU=Sales,DC=example,DC=com"
          hint={
            isEdit ? t("orgUnits.moveHint") : undefined
          }
        />

        <FormRow label={t("common.description")}>
          <input
            type="text"
            value={values.description}
            onChange={(event: ChangeEvent<HTMLInputElement>) => onFieldChange("description", event.target.value)}
          />
        </FormRow>

        {isEdit && unit !== null && (
          <FormHint>
            {t("orgUnits.currentDnHint", { dn: unit.distinguishedName })}
          </FormHint>
        )}

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
