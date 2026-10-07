import { createRoot } from "react-dom/client";
import type { ReactElement } from "react";
import type { Root } from "react-dom/client";
import type { Permission } from "./api/dto";
import { isAuthenticated } from "./lib/auth";
import { can } from "./lib/permissions";
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
import { RolesPage } from "./containers/RolesPage";
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

function requirePermissionOr(permission: Permission, fallback = "/"): boolean {
  if (!can(permission)) {
    navigate(fallback);
    return false;
  }
  return true;
}

/**
 * A page route: the guard, the shell, and the page inside it. `permission` is the one the page's
 * own listing needs — the same one its menu item is shown for (see lib/navigation).
 */
function page(path: string, element: () => ReactElement, permission?: Permission): void {
  route(path, () => {
    if (!guardOrRedirect()) return;
    if (permission !== undefined && !requirePermissionOr(permission)) return;
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
page("/providers", () => <ProvidersPage />, "providers.manage");
page("/role-mappings", () => <RoleMappingsPage />, "roles.manage");
page("/roles", () => <RolesPage />, "roles.manage");
page("/rate-limits", () => <RateLimitsPage />, "ratelimits.manage");
page("/audit", () => <AuditPage />, "audit.read");

applyStoredTheme();
applyStoredLanguage();
startRouter();
