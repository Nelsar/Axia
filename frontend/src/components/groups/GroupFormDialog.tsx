import type { ChangeEvent, FormEvent, JSX } from "react";
import { t } from "../../lib/i18n";
import type { GroupDto } from "../../api/dto";
import type { FormErrors } from "../../hooks/useEntityForm";
import type { GroupFormValues } from "../../containers/GroupFormDialogContainer";
import { Modal } from "../Modal";
import { ErrorBanner, FormHint, FormRow } from "../primitives";
import { ParentDnField } from "../directory/ParentDnField";

export interface GroupFormDialogProps {
  isEdit: boolean;
  /** null when creating: there is no DN yet, so there is no "current DN" to show. */
  group: GroupDto | null;
  values: GroupFormValues;
  errors: FormErrors<GroupFormValues>;
  submitError: string | null;
  isSubmitting: boolean;
  parentOptions: readonly string[];
  onFieldChange: <K extends keyof GroupFormValues>(field: K, value: GroupFormValues[K]) => void;
  onSubmit: (event?: FormEvent<HTMLFormElement>) => void;
  onClose: () => void;
}

export function GroupFormDialog({
  isEdit,
  group,
  values,
  errors,
  submitError,
  isSubmitting,
  parentOptions,
  onFieldChange,
  onSubmit,
  onClose,
}: GroupFormDialogProps): JSX.Element {
  return (
    <Modal title={isEdit ? t("groups.dialogEdit") : t("groups.dialogCreate")} onClose={onClose}>
      <form className="form-grid" onSubmit={onSubmit} noValidate>
        <FormRow label={t("groups.nameLabel")} error={errors.name}>
          <input
            type="text"
            value={values.name}
            placeholder={t("groups.namePlaceholder")}
            autoFocus
            onChange={(event: ChangeEvent<HTMLInputElement>) => onFieldChange("name", event.target.value)}
          />
        </FormRow>

        <ParentDnField
          label={isEdit ? t("common.parentDn") : t("common.parentDnWithDefault")}
          value={values.parentDistinguishedName}
          onChange={(value: string) => onFieldChange("parentDistinguishedName", value)}
          options={parentOptions}
          placeholder="OU=Groups,DC=example,DC=com"
          hint={isEdit ? t("groups.moveHint") : undefined}
        />

        <FormRow label={t("common.description")}>
          <input
            type="text"
            value={values.description}
            onChange={(event: ChangeEvent<HTMLInputElement>) => onFieldChange("description", event.target.value)}
          />
        </FormRow>

        <FormRow
          label={t("groups.membersLabel")}
          error={errors.members}
          hint={t("groups.errorMembersRequired") + (isEdit ? t("groups.membersReplaceHint") : "")}
        >
          <textarea
            rows={5}
            value={values.members}
            placeholder="uid=jsmith,DC=example,DC=com"
            onChange={(event: ChangeEvent<HTMLTextAreaElement>) => onFieldChange("members", event.target.value)}
          />
        </FormRow>

        {isEdit && group !== null && (
          <FormHint>
            {t("groups.currentDnHint", { dn: group.distinguishedName })}
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
