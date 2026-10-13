import type { ChangeEvent, FormEvent, JSX } from "react";
import { t } from "../../lib/i18n";
import { USER_ROLES, isUserRole } from "../../api/dto";
import type { ProviderDto } from "../../api/dto";
import type { FormErrors } from "../../hooks/useEntityForm";
import type { RoleMappingFormValues } from "../../containers/RoleMappingFormDialogContainer";
import { Modal } from "../Modal";
import { ErrorBanner, FormRow } from "../primitives";

export interface RoleMappingFormDialogProps {
  isEdit: boolean;
  values: RoleMappingFormValues;
  errors: FormErrors<RoleMappingFormValues>;
  submitError: string | null;
  isSubmitting: boolean;
  providers: readonly ProviderDto[];
  onFieldChange: <K extends keyof RoleMappingFormValues>(field: K, value: RoleMappingFormValues[K]) => void;
  onSubmit: (event?: FormEvent<HTMLFormElement>) => void;
  onClose: () => void;
}

export function RoleMappingFormDialog({
  isEdit,
  values,
  errors,
  submitError,
  isSubmitting,
  providers,
  onFieldChange,
  onSubmit,
  onClose,
}: RoleMappingFormDialogProps): JSX.Element {
  return (
    <Modal title={isEdit ? t("roleMappings.dialogEdit") : t("roleMappings.dialogCreate")} onClose={onClose}>
      <form className="form-grid" onSubmit={onSubmit} noValidate>
        <FormRow label={t("roleMappings.groupLabel")} error={errors.groupName}>
          <input
            type="text"
            value={values.groupName}
            placeholder="CN=Administrators,OU=Groups,DC=example,DC=com"
            autoFocus
            onChange={(event: ChangeEvent<HTMLInputElement>) => onFieldChange("groupName", event.target.value)}
          />
        </FormRow>

        <FormRow label={t("roleMappings.roleLabel")}>
          {/* The role in the form state is typed UserRole, so the string from the select is
              narrowed by the isUserRole guard — otherwise it would take a cast, and the union
              would stop meaning anything. */}
          <select
            value={values.roleName}
            onChange={(event: ChangeEvent<HTMLSelectElement>) => {
              if (isUserRole(event.target.value)) onFieldChange("roleName", event.target.value);
            }}
          >
            {USER_ROLES.map((role) => (
              <option key={role} value={role}>
                {role}
              </option>
            ))}
          </select>
        </FormRow>

        <FormRow label={t("roleMappings.providerLabel")}>
          <select
            value={values.providerId}
            onChange={(event: ChangeEvent<HTMLSelectElement>) => onFieldChange("providerId", event.target.value)}
          >
            <option value="">{t("roleMappings.global")}</option>
            {providers.map((provider) => (
              <option key={provider.id} value={provider.id}>
                {provider.name}
              </option>
            ))}
          </select>
        </FormRow>

        <FormRow label={t("roleMappings.priorityLabel")} error={errors.priority}>
          <input
            type="number"
            value={values.priority}
            onChange={(event: ChangeEvent<HTMLInputElement>) => onFieldChange("priority", event.target.value)}
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
