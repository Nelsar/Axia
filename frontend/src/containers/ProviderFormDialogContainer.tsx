import { useCallback } from "react";
import { t } from "../lib/i18n";
import { api } from "../api";
import type { LdapSecurity, ProviderDto, ProviderType } from "../api/dto";
import { toast } from "../lib/ui";
import { useEntityForm } from "../hooks/useEntityForm";
import type { FormErrors } from "../hooks/useEntityForm";
import { ProviderFormDialog } from "../components/providers/ProviderFormDialog";

const DEFAULT_USER_SEARCH_FILTER =
  "(&(objectClass=person)(|(uid={0})(sAMAccountName={0})(userPrincipalName={0})(mail={0})))";

/**
 * The port each mode is normally served on. The API refuses LDAPS on 389 and plain LDAP on 636,
 * so when the mode changes the port follows it — but only while it is still the standard port of
 * the old mode. A port someone typed is theirs to keep.
 */
const STANDARD_PORT: Readonly<Record<LdapSecurity, string>> = { Ldaps: "636", StartTls: "389", None: "389" };

/**
 * The form's numeric fields are strings: <input type="number"> returns a string, and storing them
 * as numbers means either catching NaN or scrubbing the input on every keystroke. Numbers appear
 * in onSubmit.
 */
export interface ProviderFormValues {
  name: string;
  host: string;
  port: string;
  security: LdapSecurity;
  providerType: ProviderType;
  isActive: boolean;
  failoverHosts: string;
  baseDn: string;
  adminUsername: string;
  adminPassword: string;
  userSearchFilter: string;
  userSearchPageSize: string;
}

export interface ProviderFormDialogContainerProps {
  provider: ProviderDto | null;
  onSaved: () => void;
  onClose: () => void;
}

function validate(values: ProviderFormValues): FormErrors<ProviderFormValues> {
  const errors: FormErrors<ProviderFormValues> = {};
  if (values.name.trim() === "") errors.name = t("providers.errorNameRequired");
  if (values.host.trim() === "") errors.host = t("providers.errorHostRequired");
  const port = Number.parseInt(values.port, 10);
  if (!Number.isInteger(port) || port < 1 || port > 65535) errors.port = t("providers.errorPortRange");
  return errors;
}

export function ProviderFormDialogContainer({
  provider,
  onSaved,
  onClose,
}: ProviderFormDialogContainerProps): JSX.Element {
  const isEdit = provider !== null;

  const onSubmit = useCallback(
    async (values: ProviderFormValues): Promise<void> => {
      const payload = {
        name: values.name.trim(),
        host: values.host.trim(),
        port: Number.parseInt(values.port, 10),
        security: values.security,
        providerType: values.providerType,
        isActive: values.isActive,
        failoverHosts: values.failoverHosts.trim() || null,
        baseDn: values.baseDn.trim(),
        adminUsername: values.adminUsername.trim(),
        // An empty password on update means "keep the current one", so the field is not sent at all.
        adminPassword: values.adminPassword || undefined,
        userSearchFilter: values.userSearchFilter,
        userSearchPageSize: Number.parseInt(values.userSearchPageSize, 10) || 500,
      };
      if (provider !== null) {
        await api.providers.update(provider.id, payload);
        toast(t("providers.updated"), { type: "success" });
      } else {
        await api.providers.create(payload);
        toast(t("providers.created"), { type: "success" });
      }
    },
    [provider]
  );

  const onSuccess = useCallback((): void => {
    onSaved();
    onClose();
  }, [onClose, onSaved]);

  const form = useEntityForm<ProviderFormValues>({
    initialValues: {
      name: provider?.name ?? "",
      host: provider?.host ?? "",
      // A new directory starts on LDAPS, the API's own default; an existing one shows what it has.
      port: String(provider?.port ?? STANDARD_PORT.Ldaps),
      security: provider?.security ?? "Ldaps",
      providerType: provider?.providerType ?? "StandardLdap",
      isActive: provider?.isActive ?? true,
      failoverHosts: provider?.failoverHosts ?? "",
      baseDn: provider?.baseDn ?? "",
      adminUsername: provider?.adminUsername ?? "",
      adminPassword: "",
      userSearchFilter: provider?.userSearchFilter ?? DEFAULT_USER_SEARCH_FILTER,
      userSearchPageSize: String(provider?.userSearchPageSize ?? 500),
    },
    validate,
    onSubmit,
    onSuccess,
  });

  const { setField, setValues } = form;
  const onFieldChange = useCallback(
    <K extends keyof ProviderFormValues>(field: K, value: ProviderFormValues[K]): void => {
      if (field !== "security") {
        setField(field, value);
        return;
      }
      // One update for both fields, so the port never shows a value from the other mode.
      const next = value as LdapSecurity;
      setValues((previous) => ({
        ...previous,
        security: next,
        port: previous.port === STANDARD_PORT[previous.security] ? STANDARD_PORT[next] : previous.port,
      }));
    },
    [setField, setValues]
  );

  return (
    <ProviderFormDialog
      isEdit={isEdit}
      title={provider !== null ? t("providers.dialogEdit", { name: provider.name }) : t("providers.dialogCreate")}
      values={form.values}
      errors={form.errors}
      submitError={form.submitError}
      isSubmitting={form.isSubmitting}
      onFieldChange={onFieldChange}
      onSubmit={form.submit}
      onClose={onClose}
    />
  );
}
