import { useCallback, useMemo } from "react";
import { t } from "../lib/i18n";
import { api } from "../api";
import type { UserDto } from "../api/dto";
import { toast } from "../lib/ui";
import { useEntityForm } from "./useEntityForm";
import type { EntityFormState, Validate } from "./useEntityForm";

export interface UserFormValues {
  name: string;
  email: string;
  password: string;
}

export interface UserFormState extends EntityFormState<UserFormValues> {
  isEdit: boolean;
}

export interface UseUserFormOptions {
  /** null means creating a new user. */
  user: UserDto | null;
  onSaved: () => void;
  onClose: () => void;
}

const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

// Client-side validation catches only what is visible without the directory: required fields, and
// something plainly not an email in the email field. The backend is the source of truth (password
// length, domain policy, uniqueness), so its error is shown as it is rather than replaced by one
// of ours.
function makeValidate(isEdit: boolean): Validate<UserFormValues> {
  return (values) => {
    const errors: ReturnType<Validate<UserFormValues>> = {};
    if (values.name.trim() === "") errors.name = t("login.errorNameRequired");

    if (!isEdit && values.email.trim() === "") {
      errors.email = t("users.errorEmailRequired");
    } else if (values.email.trim() !== "" && !EMAIL_RE.test(values.email.trim())) {
      errors.email = t("login.errorEmailInvalid");
    }

    if (!isEdit && values.password === "") errors.password = t("users.errorPasswordRequired");
    return errors;
  };
}

/** The user form = the shared useEntityForm plus the rules specific to a user. */
export function useUserForm({ user, onSaved, onClose }: UseUserFormOptions): UserFormState {
  const isEdit = user !== null;
  const validate = useMemo<Validate<UserFormValues>>(() => makeValidate(isEdit), [isEdit]);

  const onSubmit = useCallback(
    async (values: UserFormValues): Promise<void> => {
      if (user !== null) {
        await api.users.update(user.id, {
          name: values.name.trim(),
          // An empty field on update means "leave it alone", so it is not sent at all.
          email: values.email.trim() || undefined,
          password: values.password || undefined,
        });
        toast(t("users.saved"), { type: "success" });
      } else {
        await api.users.create({
          name: values.name.trim(),
          email: values.email.trim(),
          password: values.password,
        });
        toast(t("users.created"), { type: "success" });
      }
    },
    [user]
  );

  const onSuccess = useCallback((): void => {
    onSaved();
    onClose();
  }, [onClose, onSaved]);

  const form = useEntityForm<UserFormValues>({
    initialValues: {
      name: user?.name ?? "",
      email: user?.email ?? "",
      // A password never comes back from the directory and is never prefilled into the form.
      password: "",
    },
    validate,
    onSubmit,
    onSuccess,
  });

  return { isEdit, ...form };
}
