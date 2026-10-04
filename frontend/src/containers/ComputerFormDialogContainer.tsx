import { useCallback } from "react";
import { t } from "../lib/i18n";
import { api } from "../api";
import type { ComputerDto } from "../api/dto";
import { toast } from "../lib/ui";
import { useEntityForm } from "../hooks/useEntityForm";
import type { FormErrors } from "../hooks/useEntityForm";
import { ComputerFormDialog } from "../components/computers/ComputerFormDialog";

export interface ComputerFormValues {
  name: string;
  parentDistinguishedName: string;
  description: string;
  dnsHostName: string;
  enabled: boolean;
}

export interface ComputerFormDialogContainerProps {
  computer: ComputerDto | null;
  parentOptions: readonly string[];
  onSaved: () => void;
  onClose: () => void;
}

function validate(values: ComputerFormValues): FormErrors<ComputerFormValues> {
  const errors: FormErrors<ComputerFormValues> = {};
  if (values.name.trim() === "") errors.name = t("computers.errorNameRequired");
  return errors;
}

export function ComputerFormDialogContainer({
  computer,
  parentOptions,
  onSaved,
  onClose,
}: ComputerFormDialogContainerProps): JSX.Element {
  const isEdit = computer !== null;

  const onSubmit = useCallback(
    async (values: ComputerFormValues): Promise<void> => {
      // For the string fields an empty value means "clear it" — that is the API's contract. The
      // enabled flag is only sent on update: a new entry has none (and ComputerCreateRequest has
      // no such field either, so the two cannot be confused).
      const payload = {
        name: values.name.trim(),
        parentDistinguishedName: values.parentDistinguishedName.trim(),
        description: values.description.trim(),
        dnsHostName: values.dnsHostName.trim(),
      };
      if (computer !== null) {
        await api.computers.update(computer.id, { ...payload, enabled: values.enabled });
        toast(t("computers.updated"), { type: "success" });
      } else {
        await api.computers.create(payload);
        toast(t("computers.created"), { type: "success" });
      }
    },
    [computer]
  );

  const onSuccess = useCallback((): void => {
    onSaved();
    onClose();
  }, [onClose, onSaved]);

  const form = useEntityForm<ComputerFormValues>({
    initialValues: {
      name: computer?.name ?? "",
      parentDistinguishedName: computer?.parentDistinguishedName ?? "",
      description: computer?.description ?? "",
      dnsHostName: computer?.dnsHostName ?? "",
      // "The directory did not say" (null) is edited as "enabled": saving sends an explicit value
      // either way rather than leaving the entry in the unknown.
      enabled: computer?.enabled !== false,
    },
    validate,
    onSubmit,
    onSuccess,
  });

  return (
    <ComputerFormDialog
      isEdit={isEdit}
      computer={computer}
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
