import type { UserDto } from "../api/dto";
import type { UserPermissions } from "../hooks/usePermissions";
import { useUserAttributes } from "../hooks/useUserAttributes";
import { UserDetailDialog } from "../components/users/UserDetailDialog";
import type { JSX } from "react";

export interface UserDetailDialogContainerProps {
  user: UserDto;
  perms: UserPermissions;
  onEdit: () => void;
  onDelete: () => void;
  onClose: () => void;
}

/** The detail card’s container: it wires up the lazy attribute load; everything else passes straight through. */
export function UserDetailDialogContainer({
  user,
  perms,
  onEdit,
  onDelete,
  onClose,
}: UserDetailDialogContainerProps): JSX.Element {
  const attributes = useUserAttributes(user.id);

  return (
    <UserDetailDialog
      user={user}
      perms={perms}
      attributes={attributes}
      onLoadAttributes={() => void attributes.load()}
      onEdit={onEdit}
      onDelete={onDelete}
      onClose={onClose}
    />
  );
}
