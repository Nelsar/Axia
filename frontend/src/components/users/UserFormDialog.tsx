import type { ChangeEvent, FormEvent } from "react";
import { t } from "../../lib/i18n";
import type { FormErrors } from "../../hooks/useEntityForm";
import type { UserFormValues } from "../../hooks/useUserForm";
import { Modal } from "../Modal";
import { ErrorBanner, FormRow } from "../primitives";

export interface UserFormDialogProps {
  isEdit: boolean;
  values: UserFormValues;
  errors: FormErrors<UserFormValues>;
  submitError: string | null;
  isSubmitting: boolean;
  /** The value type is inferred from the field name: onFieldChange("name", 42) does not compile. */
  onFieldChange: <K extends keyof UserFormValues>(field: K, value: UserFormValues[K]) => void;
  onSubmit: (event?: FormEvent<HTMLFormElement>) => void;
  onClose: () => void;
}

/**
 * The create/edit form — markup and the binding of values to handlers, nothing else. The values,
 * the field errors and the submitting flag come from useUserForm.
 */
export function UserFormDialog({
  isEdit,
  values,
  errors,
  submitError,
  isSubmitting,
  onFieldChange,
  onSubmit,
  onClose,
}: UserFormDialogProps): JSX.Element {
  const onInput =
    (field: keyof UserFormValues) =>
    (event: ChangeEvent<HTMLInputElement>): void =>
      onFieldChange(field, event.target.value);

  return (
    <Modal title={isEdit ? t("users.dialogEdit") : t("users.dialogCreate")} onClose={onClose}>
      <form className="form-grid" onSubmit={onSubmit} noValidate>
        <FormRow label={t("common.name")} error={errors.name}>
          <input type="text" value={values.name} autoFocus onChange={onInput("name")} />
        </FormRow>

        <FormRow label="Email" error={errors.email}>
          <input type="email" value={values.email} onChange={onInput("email")} />
        </FormRow>

        <FormRow
          label={isEdit ? t("users.passwordKeep") : t("users.password")}
          error={errors.password}
        >
          <input
            type="password"
            value={values.password}
            autoComplete="new-password"
            onChange={onInput("password")}
          />
        </FormRow>

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
