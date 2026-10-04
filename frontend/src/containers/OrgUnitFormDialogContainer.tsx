import { useCallback } from "react";
import { t } from "../lib/i18n";
import { api } from "../api";
import type { OrganizationalUnitDto } from "../api/dto";
import { toast } from "../lib/ui";
import { useEntityForm } from "../hooks/useEntityForm";
import type { FormErrors } from "../hooks/useEntityForm";
import { OrgUnitFormDialog } from "../components/orgUnits/OrgUnitFormDialog";

export interface OrgUnitFormValues {
  name: string;
  parentDistinguishedName: string;
  description: string;
}

export interface OrgUnitFormDialogContainerProps {
  unit: OrganizationalUnitDto | null;
  parentOptions: readonly string[];
  onSaved: () => void;
  onClose: () => void;
}

function validate(values: OrgUnitFormValues): FormErrors<OrgUnitFormValues> {
  const errors: FormErrors<OrgUnitFormValues> = {};
  if (values.name.trim() === "") errors.name = t("orgUnits.errorNameRequired");
  return errors;
}

export function OrgUnitFormDialogContainer({
  unit,
  parentOptions,
  onSaved,
  onClose,
}: OrgUnitFormDialogContainerProps): JSX.Element {
  const isEdit = unit !== null;

  const onSubmit = useCallback(
    async (values: OrgUnitFormValues): Promise<void> => {
      const payload = {
        name: values.name.trim(),
        parentDistinguishedName: values.parentDistinguishedName.trim(),
        description: values.description.trim(),
      };
      if (unit !== null) {
        await api.orgUnits.update(unit.id, payload);
        toast(t("orgUnits.updated"), { type: "success" });
      } else {
        await api.orgUnits.create(payload);
        toast(t("orgUnits.created"), { type: "success" });
      }
    },
    [unit]
  );

  const onSuccess = useCallback((): void => {
    onSaved();
    onClose();
  }, [onClose, onSaved]);

  const form = useEntityForm<OrgUnitFormValues>({
    initialValues: {
      name: unit?.name ?? "",
      parentDistinguishedName: unit?.parentDistinguishedName ?? "",
      description: unit?.description ?? "",
    },
    validate,
    onSubmit,
    onSuccess,
  });

  return (
    <OrgUnitFormDialog
      isEdit={isEdit}
      unit={unit}
      values={form.values}
      errors={form.errors}
      submitError={form.submitError}
      isSubmitting={form.isSubmitting}
      parentOptions={parentOptions}
      onFieldChange={form.setField}
      onSubmit={form.submit}
      onClose={onClose}
    />
  );
}
