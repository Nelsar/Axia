import type { ChangeEvent, FormEvent, JSX } from "react";
import { t } from "../../lib/i18n";
import type { ComputerDto } from "../../api/dto";
import type { FormErrors } from "../../hooks/useEntityForm";
import type { ComputerFormValues } from "../../containers/ComputerFormDialogContainer";
import { Modal } from "../Modal";
import { ErrorBanner, FormHint, FormRow } from "../primitives";
import { ParentDnField } from "../directory/ParentDnField";

export interface ComputerFormDialogProps {
  isEdit: boolean;
  computer: ComputerDto | null;
  values: ComputerFormValues;
  errors: FormErrors<ComputerFormValues>;
  submitError: string | null;
  isSubmitting: boolean;
  parentOptions: readonly string[];
  onFieldChange: <K extends keyof ComputerFormValues>(field: K, value: ComputerFormValues[K]) => void;
  onSubmit: (event?: FormEvent<HTMLFormElement>) => void;
  onClose: () => void;
}

export function ComputerFormDialog({
  isEdit,
  computer,
  values,
  errors,
  submitError,
  isSubmitting,
  parentOptions,
  onFieldChange,
  onSubmit,
  onClose,
}: ComputerFormDialogProps): JSX.Element {
  const onText =
    (field: "name" | "description" | "dnsHostName") =>
    (event: ChangeEvent<HTMLInputElement>): void =>
      onFieldChange(field, event.target.value);

  return (
    <Modal title={isEdit ? t("computers.dialogEdit") : t("computers.dialogCreate")} onClose={onClose}>
      <form className="form-grid" onSubmit={onSubmit} noValidate>
        <FormRow
          label={t("groups.nameLabel")}
          error={errors.name}
          hint={
            isEdit ? undefined : t("computers.nameHint")
          }
        >
          <input type="text" value={values.name} placeholder="WS-042" autoFocus onChange={onText("name")} />
        </FormRow>

        <ParentDnField
          label={isEdit ? t("common.parentDn") : t("common.parentDnWithDefault")}
          value={values.parentDistinguishedName}
          onChange={(value: string) => onFieldChange("parentDistinguishedName", value)}
          options={parentOptions}
          placeholder="OU=Computers,DC=example,DC=com"
        />

        <FormRow label={t("computers.dnsLabel")}>
          <input
            type="text"
            value={values.dnsHostName}
            placeholder="ws-042.example.com"
            onChange={onText("dnsHostName")}
          />
        </FormRow>

        <FormRow label={t("common.description")}>
          <input type="text" value={values.description} onChange={onText("description")} />
        </FormRow>

        {isEdit && computer !== null ? (
          <div>
            <div className="form-row">
              <label className="checkbox">
                <input
                  type="checkbox"
                  checked={values.enabled}
                  onChange={(event: ChangeEvent<HTMLInputElement>) =>
                    onFieldChange("enabled", event.target.checked)
                  }
                />{" "}
                {t("computers.enabledLabel")}
              </label>
              <FormHint>{t("computers.enabledHint")}</FormHint>
            </div>
            <FormHint>{t("computers.currentDnHint", { dn: computer.distinguishedName })}</FormHint>
          </div>
        ) : (
          <FormHint>{t("computers.createNote")}</FormHint>
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
