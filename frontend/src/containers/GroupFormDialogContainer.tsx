import { useCallback, type JSX } from "react";
import { t } from "../lib/i18n";
import { api } from "../api";
import type { GroupDto } from "../api/dto";
import { toast } from "../lib/ui";
import { useEntityForm } from "../hooks/useEntityForm";
import type { FormErrors } from "../hooks/useEntityForm";
import { GroupFormDialog } from "../components/groups/GroupFormDialog";

/** The membership is typed as text (a DN per line) and goes to the API as a list — different types. */
export interface GroupFormValues {
  name: string;
  parentDistinguishedName: string;
  description: string;
  members: string;
}

export interface GroupFormDialogContainerProps {
  group: GroupDto | null;
  parentOptions: readonly string[];
  onSaved: () => void;
  onClose: () => void;
}

function parseMembers(text: string): string[] {
  return text
    .split("\n")
    .map((line) => line.trim())
    .filter((line) => line.length > 0);
}

function validate(values: GroupFormValues): FormErrors<GroupFormValues> {
  const errors: FormErrors<GroupFormValues> = {};
  if (values.name.trim() === "") errors.name = t("groups.errorNameRequired");
  if (parseMembers(values.members).length === 0) {
    errors.members = t("groups.errorMembersRequired");
  }
  return errors;
}

export function GroupFormDialogContainer({
  group,
  parentOptions,
  onSaved,
  onClose,
}: GroupFormDialogContainerProps): JSX.Element {
  const isEdit = group !== null;

  const onSubmit = useCallback(
    async (values: GroupFormValues): Promise<void> => {
      const payload = {
        name: values.name.trim(),
        parentDistinguishedName: values.parentDistinguishedName.trim(),
        description: values.description.trim(),
        memberDistinguishedNames: parseMembers(values.members),
      };
      if (group !== null) {
        await api.groups.update(group.id, payload);
        toast(t("groups.updated"), { type: "success" });
      } else {
        await api.groups.create(payload);
        toast(t("groups.created"), { type: "success" });
      }
    },
    [group]
  );

  const onSuccess = useCallback((): void => {
    onSaved();
    onClose();
  }, [onClose, onSaved]);

  const form = useEntityForm<GroupFormValues>({
    initialValues: {
      name: group?.name ?? "",
      parentDistinguishedName: group?.parentDistinguishedName ?? "",
      description: group?.description ?? "",
      members: (group?.members ?? []).join("\n"),
    },
    validate,
    onSubmit,
    onSuccess,
  });

  return (
    <GroupFormDialog
      isEdit={isEdit}
      group={group}
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
