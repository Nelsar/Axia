import type { UserDto } from "../api/dto";
import type { UserPermissions } from "../hooks/usePermissions";
import { useUserAttributes } from "../hooks/useUserAttributes";
import { useUserDetails } from "../hooks/useUserDetails";
import { UserDetailDialog } from "../components/users/UserDetailDialog";
import type { JSX } from "react";

export interface UserDetailDialogContainerProps {
  user: UserDto;
  perms: UserPermissions;
  onEdit: () => void;
  onDelete: () => void;
  onClose: () => void;
}

/** The detail card’s container: it reads the account itself, and wires up the lazy attribute load. */
export function UserDetailDialogContainer({
  user,
  perms,
  onEdit,
  onDelete,
  onClose,
}: UserDetailDialogContainerProps): JSX.Element {
  const details = useUserDetails(user);
  const attributes = useUserAttributes(user.id);

  return (
    <UserDetailDialog
      user={details.user}
      groupsStatus={details.status}
      groupsError={details.error}
      perms={perms}
      attributes={attributes}
      onLoadAttributes={() => void attributes.load()}
      onEdit={onEdit}
      onDelete={onDelete}
      onClose={onClose}
    />
  );
}
