import { useCallback, useState } from "react";
import { t } from "../lib/i18n";
import { api } from "../api";
import { setSession } from "../lib/auth";
import { navigate } from "../lib/router";
import { toast } from "../lib/ui";
import { useApiBaseUrl } from "../hooks/useApiBaseUrl";
import { useSelectableDirectories } from "../hooks/useSelectableDirectories";
import { useEntityForm } from "../hooks/useEntityForm";
import type { FormErrors } from "../hooks/useEntityForm";
import { LoginView } from "../components/login/LoginView";

export type LoginMode = "login" | "register";

export interface LoginFormValues {
  email: string;
  password: string;
}

export interface RegisterFormValues {
  name: string;
  email: string;
  password: string;
}

const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

function validateLogin(values: LoginFormValues): FormErrors<LoginFormValues> {
  const errors: FormErrors<LoginFormValues> = {};
  if (values.email.trim() === "") errors.email = t("login.errorLoginRequired");
  if (values.password === "") errors.password = t("login.errorPasswordRequired");
  return errors;
}

function validateRegister(values: RegisterFormValues): FormErrors<RegisterFormValues> {
  const errors: FormErrors<RegisterFormValues> = {};
  if (values.name.trim() === "") errors.name = t("login.errorNameRequired");
  if (values.email.trim() === "") errors.email = t("login.errorEmailRequired");
  else if (!EMAIL_RE.test(values.email.trim())) errors.email = t("login.errorEmailInvalid");
  if (values.password === "") errors.password = t("login.errorPasswordRequired");
  return errors;
}

export function LoginPage(): JSX.Element {
  const [mode, setMode] = useState<LoginMode>("login");
  const apiUrl = useApiBaseUrl();
  const directories = useSelectableDirectories();

  // The chosen directory is needed by both the request and the session, but differently: the
  // request wants the id as a header, the session wants the id with its name so there is something
  // to show later. Both forms come from the hook rather than the name being looked up by id in the
  // handler.
  const { selectedId: directoryId, selected: directory } = directories;

  const onLoginSubmit = useCallback(
    async (values: LoginFormValues): Promise<void> => {
      const result = await api.auth.login(values.email.trim(), values.password, directoryId);
      setSession(result.token, result.user, directory, result.permissions);
      toast(t("login.welcome", { name: result.user.name }), { type: "success" });
      navigate("/");
    },
    [directoryId, directory]
  );

  const loginForm = useEntityForm<LoginFormValues>({
    initialValues: { email: "", password: "" },
    validate: validateLogin,
    onSubmit: onLoginSubmit,
    errorFallback: t("login.failed"),
  });
  // setValues is a useState setter, so its reference is stable and safe to use as a dependency.
  const { setValues: setLoginValues } = loginForm;

  const onRegisterSubmit = useCallback(
    async (values: RegisterFormValues): Promise<void> => {
      // Registration creates an account in a directory, so it too has to say which one.
      await api.auth.register(
        values.email.trim(),
        values.name.trim(),
        values.password,
        directoryId
      );
      toast(t("login.registered"), { type: "success" });
      // The login is carried over into the sign-in form: the address just typed is the one thing
      // a person here definitely does not want to type twice.
      setLoginValues((previous) => ({ ...previous, email: values.email.trim() }));
      setMode("login");
    },
    [setLoginValues, directoryId]
  );

  const registerForm = useEntityForm<RegisterFormValues>({
    initialValues: { name: "", email: "", password: "" },
    validate: validateRegister,
    onSubmit: onRegisterSubmit,
    errorFallback: t("login.registerFailed"),
  });

  return (
    <LoginView
      mode={mode}
      onModeChange={setMode}
      loginForm={loginForm}
      registerForm={registerForm}
      directories={directories}
      apiUrl={apiUrl}
    />
  );
}
