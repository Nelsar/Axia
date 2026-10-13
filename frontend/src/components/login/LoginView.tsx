import type { ChangeEvent, JSX } from "react";
import { t } from "../../lib/i18n";
import type { ApiBaseUrlState } from "../../hooks/useApiBaseUrl";
import type { SelectableDirectoriesState } from "../../hooks/useSelectableDirectories";
import type { EntityFormState } from "../../hooks/useEntityForm";
import type { LoginFormValues, LoginMode, RegisterFormValues } from "../../containers/LoginPage";
import { LanguagePicker } from "../LanguagePicker";
import { Tabs } from "../Tabs";
import { ErrorBanner, FormHint, FormRow } from "../primitives";

export interface LoginViewProps {
  mode: LoginMode;
  onModeChange: (mode: LoginMode) => void;
  loginForm: EntityFormState<LoginFormValues>;
  registerForm: EntityFormState<RegisterFormValues>;
  directories: SelectableDirectoriesState;
  apiUrl: ApiBaseUrlState;
}

interface SubmitButtonProps {
  isSubmitting: boolean;
  label: string;
  busyLabel: string;
}

function SubmitButton({ isSubmitting, label, busyLabel }: SubmitButtonProps): JSX.Element {
  return (
    <button className="btn btn--primary" type="submit" style={{ justifyContent: "center" }} disabled={isSubmitting}>
      {isSubmitting ? busyLabel : label}
    </button>
  );
}

/**
 * The directory picker. Shown only when there is genuinely something to choose between: an
 * installation with one directory would get a list of one item, which decides nothing and merely
 * asks for a decision where none exists.
 *
 * A directory is chosen once, here. After that it is recorded in the token and can only be changed
 * by signing in again — which is also how the backend has it, where a header naming a different
 * directory is refused.
 */
function DirectoryPicker({ directories }: { directories: SelectableDirectoriesState }): JSX.Element | null {
  if (directories.isLoading) return null;
  if (directories.error === null && directories.options.length === 0) return null;

  return (
    <FormRow label={t("nav.section.directory")}>
      <select
        value={directories.selectedId}
        disabled={directories.error !== null}
        onChange={(event: ChangeEvent<HTMLSelectElement>) => directories.setSelectedId(event.target.value)}
      >
        <option value="">{t("login.directoryDefault")}</option>
        {directories.options.map((option) => (
          <option key={option.id} value={option.id}>
            {option.name}
          </option>
        ))}
      </select>
      {directories.error !== null && (
        <FormHint>{t("login.directoryFallbackHint", { error: directories.error })}</FormHint>
      )}
    </FormRow>
  );
}

// The login field is not type="email": an AD account may have neither mail nor userPrincipalName
// — only a sAMAccountName — and the browser refused to submit such a login. The directory search
// tries all of those attributes anyway.
function LoginForm({
  form,
  directories,
}: {
  form: EntityFormState<LoginFormValues>;
  directories: SelectableDirectoriesState;
}): JSX.Element {
  return (
    <form className="form-grid" onSubmit={form.submit} noValidate>
      <DirectoryPicker directories={directories} />
      <FormRow label={t("login.login")} error={form.errors.email}>
        <input
          type="text"
          value={form.values.email}
          autoComplete="username"
          placeholder={t("login.loginPlaceholder")}
          onChange={(event: ChangeEvent<HTMLInputElement>) => form.setField("email", event.target.value)}
        />
      </FormRow>
      <FormRow label={t("login.password")} error={form.errors.password}>
        <input
          type="password"
          value={form.values.password}
          autoComplete="current-password"
          onChange={(event: ChangeEvent<HTMLInputElement>) => form.setField("password", event.target.value)}
        />
      </FormRow>
      {form.submitError !== null && <ErrorBanner>{form.submitError}</ErrorBanner>}
      <SubmitButton isSubmitting={form.isSubmitting} label={t("login.submit")} busyLabel={t("login.submitting")} />
    </form>
  );
}

function RegisterForm({
  form,
  directories,
}: {
  form: EntityFormState<RegisterFormValues>;
  directories: SelectableDirectoriesState;
}): JSX.Element {
  return (
    <form className="form-grid" onSubmit={form.submit} noValidate>
      {/* An account is created in a directory, so this form has to say which one too. */}
      <DirectoryPicker directories={directories} />
      <FormRow label={t("common.name")} error={form.errors.name}>
        <input
          type="text"
          value={form.values.name}
          onChange={(event: ChangeEvent<HTMLInputElement>) => form.setField("name", event.target.value)}
        />
      </FormRow>
      <FormRow label="Email" error={form.errors.email}>
        <input
          type="email"
          value={form.values.email}
          autoComplete="username"
          onChange={(event: ChangeEvent<HTMLInputElement>) => form.setField("email", event.target.value)}
        />
      </FormRow>
      <FormRow label={t("login.password")} error={form.errors.password}>
        <input
          type="password"
          value={form.values.password}
          autoComplete="new-password"
          onChange={(event: ChangeEvent<HTMLInputElement>) => form.setField("password", event.target.value)}
        />
      </FormRow>
      {form.submitError !== null && <ErrorBanner>{form.submitError}</ErrorBanner>}
      <SubmitButton isSubmitting={form.isSubmitting} label={t("login.registerSubmit")} busyLabel={t("common.creating")} />
    </form>
  );
}

function isLoginMode(value: string): value is LoginMode {
  return value === "login" || value === "register";
}

export function LoginView({
  mode,
  onModeChange,
  loginForm,
  registerForm,
  directories,
  apiUrl,
}: LoginViewProps): JSX.Element {
  return (
    <div className="login-screen">
      <div className="login-card">
        <div className="login-card__logo">
          <img className="sidebar__logo" src="/favicon.svg" alt="" />
          <div style={{ fontWeight: 700, fontSize: "18px" }}>Axia</div>
          {/* The language control has to be reachable before signing in: someone who cannot read
              the current language cannot get past this screen to change it. */}
          <div style={{ marginInlineStart: "auto" }}>
            <LanguagePicker />
          </div>
        </div>
        <h1 className="login-card__title">{t("login.title")}</h1>
        <p className="login-card__sub">{t("login.subtitle")}</p>

        <Tabs
          value={mode}
          variant="pill"
          onChange={(next: string) => {
            if (isLoginMode(next)) onModeChange(next);
          }}
        >
          <Tabs.List label={t("login.tabs")}>
            <Tabs.Tab value="login">{t("login.tabLogin")}</Tabs.Tab>
            <Tabs.Tab value="register">{t("login.tabRegister")}</Tabs.Tab>
          </Tabs.List>
          <Tabs.Panel value="login">
            <LoginForm form={loginForm} directories={directories} />
          </Tabs.Panel>
          <Tabs.Panel value="register">
            <RegisterForm form={registerForm} directories={directories} />
          </Tabs.Panel>
        </Tabs>

        <details style={{ marginTop: "20px" }}>
          <summary style={{ fontSize: "12.5px", color: "var(--text-faint)", cursor: "pointer" }}>{t("login.apiUrl")}</summary>
          <div className="form-row" style={{ marginTop: "10px" }}>
            <input
              type="text"
              value={apiUrl.value}
              placeholder={apiUrl.placeholder}
              onChange={(event: ChangeEvent<HTMLInputElement>) => apiUrl.setValue(event.target.value)}
              onBlur={apiUrl.saveIfChanged}
            />
            <FormHint>{t("login.apiUrlHint")}</FormHint>
          </div>
        </details>
      </div>
    </div>
  );
}
