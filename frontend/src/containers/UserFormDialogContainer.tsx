import type { UserDto } from "../api/dto";
import { useUserForm } from "../hooks/useUserForm";
import { UserFormDialog } from "../components/users/UserFormDialog";
import type { JSX } from "react";

export interface UserFormDialogContainerProps {
  /** null means creating a new user. */
  user: UserDto | null;
  onSaved: () => void;
  onClose: () => void;
}

/**
 * The form's container: its only job is to call the hook and hand its values to the dialog. A
 * hook cannot be called conditionally, so it lives here: the component mounts only while the
 * dialog is open, and its state dies with the dialog.
 */
export function UserFormDialogContainer({
  user,
  onSaved,
  onClose,
}: UserFormDialogContainerProps): JSX.Element {
  const form = useUserForm({ user, onSaved, onClose });

  return (
    <UserFormDialog
      isEdit={form.isEdit}
      values={form.values}
      errors={form.errors}
      submitError={form.submitError}
      isSubmitting={form.isSubmitting}
      onFieldChange={form.setField}
      onSubmit={form.submit}
      onClose={onClose}
    />
  );
}
