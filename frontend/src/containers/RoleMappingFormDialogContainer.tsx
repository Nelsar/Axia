import { useCallback } from "react";
import { t } from "../lib/i18n";
import { api } from "../api";
import type { ProviderDto, RoleMappingDto, UserRole, Uuid } from "../api/dto";
import { toast } from "../lib/ui";
import { useEntityForm } from "../hooks/useEntityForm";
import type { FormErrors } from "../hooks/useEntityForm";
import { RoleMappingFormDialog } from "../components/roleMappings/RoleMappingFormDialog";

/** providerId in the form is a string: empty means "global". It goes to the API as null. */
export interface RoleMappingFormValues {
  providerId: Uuid | "";
  groupName: string;
  roleName: UserRole;
  priority: string;
}

export interface RoleMappingFormDialogContainerProps {
  mapping: RoleMappingDto | null;
  providers: readonly ProviderDto[];
  onSaved: () => void;
  onClose: () => void;
}

function validate(values: RoleMappingFormValues): FormErrors<RoleMappingFormValues> {
  const errors: FormErrors<RoleMappingFormValues> = {};
  if (values.groupName.trim() === "") errors.groupName = t("roleMappings.errorGroupRequired");
  if (values.priority !== "" && Number.isNaN(Number(values.priority))) {
    errors.priority = t("roleMappings.errorPriorityInteger");
  }
  return errors;
}

export function RoleMappingFormDialogContainer({
  mapping,
  providers,
  onSaved,
  onClose,
}: RoleMappingFormDialogContainerProps): JSX.Element {
  const isEdit = mapping !== null;

  const onSubmit = useCallback(
    async (values: RoleMappingFormValues): Promise<void> => {
      const payload = {
        providerId: values.providerId === "" ? null : values.providerId,
        groupName: values.groupName.trim(),
        roleName: values.roleName,
        priority: Number.parseInt(values.priority, 10) || 0,
      };
      if (mapping !== null) {
        await api.roleMappings.update(mapping.id, payload);
        toast(t("roleMappings.updated"), { type: "success" });
      } else {
        await api.roleMappings.create(payload);
        toast(t("roleMappings.created"), { type: "success" });
      }
    },
    [mapping]
  );

  const onSuccess = useCallback((): void => {
    onSaved();
    onClose();
  }, [onClose, onSaved]);

  const form = useEntityForm<RoleMappingFormValues>({
    initialValues: {
      providerId: mapping?.providerId ?? "",
      groupName: mapping?.groupName ?? "",
      roleName: mapping?.roleName ?? "User",
      priority: String(mapping?.priority ?? 10),
    },
    validate,
    onSubmit,
    onSuccess,
  });

  return (
    <RoleMappingFormDialog
      isEdit={isEdit}
      values={form.values}
      errors={form.errors}
      submitError={form.submitError}
      isSubmitting={form.isSubmitting}
      providers={providers}
      onFieldChange={form.setField}
      onSubmit={form.submit}
      onClose={onClose}
    />
  );
}
