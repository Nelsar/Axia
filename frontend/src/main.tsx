import { createRoot } from "react-dom/client";
import type { ReactElement } from "react";
import type { Root } from "react-dom/client";
import { hasRole, isAuthenticated } from "./lib/auth";
import { navigate, route, startRouter } from "./lib/router";
import { applyStoredLanguage, t as translate } from "./lib/i18n";
import { applyStoredTheme } from "./lib/theme";
import { AppShellContainer } from "./containers/AppShellContainer";
import { AuditPage } from "./containers/AuditPage";
import { ComputersPage } from "./containers/ComputersPage";
import { DashboardPage } from "./containers/DashboardPage";
import { GroupsPage } from "./containers/GroupsPage";
import { LoginPage } from "./containers/LoginPage";
import { OrgUnitsPage } from "./containers/OrgUnitsPage";
import { ProvidersPage } from "./containers/ProvidersPage";
import { RateLimitsPage } from "./containers/RateLimitsPage";
import { RoleMappingsPage } from "./containers/RoleMappingsPage";
import { UsersPage } from "./containers/UsersPage";

const app = document.getElementById("app");
if (app === null) throw new Error(translate("shell.mountPointMissing"));
const appRoot: HTMLElement = app;

/**
 * The entry point is responsible for exactly two things: the access rules for a route, and
 * mounting the React root. The shell's markup is in <AppShellContainer>, its state in useAppShell.
 *
 * Every navigation mounts a fresh root, so the previous one has to be unmounted: otherwise its
 * effects (timers, the bulk-job poll) outlive leaving the page.
 */
let activeRoot: Root | null = null;

function mount(element: ReactElement): void {
  if (activeRoot !== null) activeRoot.unmount();
  const root = createRoot(appRoot);
  root.render(element);
  activeRoot = root;
}

function guardOrRedirect(): boolean {
  if (!isAuthenticated()) {
    navigate("/login");
    return false;
  }
  return true;
}

function requireAdminOr(fallback = "/"): boolean {
  if (!hasRole("Admin")) {
    navigate(fallback);
    return false;
  }
  return true;
}

/** A page route: the guard, the shell, and the page inside it. */
function page(path: string, element: () => ReactElement, adminOnly = false): void {
  route(path, () => {
    if (!guardOrRedirect()) return;
    if (adminOnly && !requireAdminOr()) return;
    mount(<AppShellContainer activePath={path}>{element()}</AppShellContainer>);
  });
}

// Sign-in is the only screen without the shell: there is nothing to fill the sidebar with until there is a session.
route("/login", () => {
  if (isAuthenticated()) {
    navigate("/");
    return;
  }
  mount(<LoginPage />);
});

page("/", () => <DashboardPage />);
page("/users", () => <UsersPage />);
page("/groups", () => <GroupsPage />);
page("/org-units", () => <OrgUnitsPage />);
page("/computers", () => <ComputersPage />);
page("/providers", () => <ProvidersPage />, true);
page("/role-mappings", () => <RoleMappingsPage />, true);
page("/rate-limits", () => <RateLimitsPage />, true);
page("/audit", () => <AuditPage />, true);

applyStoredTheme();
applyStoredLanguage();
startRouter();
