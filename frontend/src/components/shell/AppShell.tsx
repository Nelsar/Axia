import { createContext, useContext, useMemo } from "react";
import { t } from "../../lib/i18n";
import type { JSX, ReactNode } from "react";

/**
 * The console shell as compound components: <AppShell> knows the current path and hands it down
 * through context, and <AppShell.Link> decides for itself whether it is highlighted. Otherwise
 * the active path would have to be threaded into every menu item by hand.
 *
 * Not one useState, not one call to the api or to localStorage: everything arrives as props.
 */
interface AppShellContextValue {
  readonly activePath: string;
}

// A null default is the only way to tell "inside <AppShell>" from "outside it".
const AppShellContext = createContext<AppShellContextValue | null>(null);

function useAppShellContext(componentName: string): AppShellContextValue {
  const context = useContext(AppShellContext);
  if (context === null) {
    throw new Error(t("shell.tabsOutsideProvider", { component: componentName, parent: "AppShell" }));
  }
  return context;
}

export interface AppShellProps {
  activePath: string;
  children: ReactNode;
}

function AppShellRoot({ activePath, children }: AppShellProps): JSX.Element {
  const context = useMemo<AppShellContextValue>(() => ({ activePath }), [activePath]);
  return (
    <AppShellContext.Provider value={context}>
      <div className="app-shell">{children}</div>
    </AppShellContext.Provider>
  );
}

export interface SidebarProps {
  children: ReactNode;
}

function Sidebar({ children }: SidebarProps): JSX.Element {
  return <aside className="sidebar">{children}</aside>;
}

export interface BrandProps {
  /** URL of the logo image. */
  logo: string;
  name: string;
}

function Brand({ logo, name }: BrandProps): JSX.Element {
  return (
    <div className="sidebar__brand">
      <img className="sidebar__logo" src={logo} alt="" />
      <div className="sidebar__brand-name">{name}</div>
    </div>
  );
}

export interface ApiBadgeProps {
  url: string;
}

function ApiBadge({ url }: ApiBadgeProps): JSX.Element {
  return (
    <div className="sidebar__api" title={t("shell.apiAddress")}>
      <div className="sidebar__api-label">API</div>
      <div className="sidebar__api-value">{url}</div>
    </div>
  );
}

export interface DirectoryBadgeProps {
  name: string;
}

/**
 * The current session's directory. The same classes as ApiBadge, deliberately: they play one
 * role — a short fact about where the console is pointed — and should look alike.
 *
 * Showing it matters precisely because the directory is now bound to the session: someone with
 * two domains would otherwise not tell "there is no such user" from "I am looking at the wrong
 * directory".
 */
function DirectoryBadge({ name }: DirectoryBadgeProps): JSX.Element {
  return (
    <div className="sidebar__api" title={t("shell.sessionDirectory")}>
      <div className="sidebar__api-label">{t("nav.section.directory")}</div>
      <div className="sidebar__api-value">{name}</div>
    </div>
  );
}

export interface NavProps {
  label?: string | undefined;
  children: ReactNode;
}

function Nav({ label = t("nav.sections"), children }: NavProps): JSX.Element {
  return (
    <nav className="sidebar__nav" aria-label={label}>
      {children}
    </nav>
  );
}

export interface SectionProps {
  /** null is a group with no heading (the dashboard). An empty string used to serve for that,
      which made "no heading" a special case of text rather than a state of its own. */
  title: string | null;
  children: ReactNode;
}

// A fragment rather than a wrapper: .sidebar__nav is a flex column, and an extra div would break the spacing between items.
function Section({ title, children }: SectionProps): JSX.Element {
  return (
    <>
      {title !== null && <div className="sidebar__section">{title}</div>}
      {children}
    </>
  );
}

export interface LinkProps {
  path: string;
  icon: string;
  children: ReactNode;
}

function Link({ path, icon, children }: LinkProps): JSX.Element {
  const { activePath } = useAppShellContext("AppShell.Link");
  const active = activePath === path;

  return (
    <a
      className={`sidebar__item${active ? " active" : ""}`}
      href={`#${path}`}
      aria-current={active ? "page" : undefined}
    >
      <span aria-hidden="true">{icon}</span>
      {children}
    </a>
  );
}

export interface FooterProps {
  children: ReactNode;
}

function Footer({ children }: FooterProps): JSX.Element {
  return <div className="sidebar__footer">{children}</div>;
}

export interface UserProps {
  initials: string;
  name: string;
  role: string;
}

function User({ initials, name, role }: UserProps): JSX.Element {
  return (
    <>
      <div className="sidebar__avatar" aria-hidden="true">
        {initials}
      </div>
      <div className="sidebar__user">
        <div className="sidebar__user-name">{name}</div>
        <div className="sidebar__user-role">{role}</div>
      </div>
    </>
  );
}

export interface IconButtonProps {
  title: string;
  onClick: () => void;
  children: ReactNode;
}

// A <button> rather than the <div class="icon-btn"> this used to be: a button is reachable from
// the keyboard and announced by a screen reader. Nothing changes visually — .icon-btn sets the
// border and background explicitly.
function IconButton({ title, onClick, children }: IconButtonProps): JSX.Element {
  return (
    <button type="button" className="icon-btn" title={title} aria-label={title} onClick={onClick}>
      <span aria-hidden="true">{children}</span>
    </button>
  );
}

export interface ContentProps {
  /** The page slot: the shell does not know what the router put inside it. */
  children: ReactNode;
}

function Content({ children }: ContentProps): JSX.Element {
  return <div className="main-col">{children}</div>;
}

export const AppShell = Object.assign(AppShellRoot, {
  Sidebar,
  Brand,
  ApiBadge,
  DirectoryBadge,
  Nav,
  Section,
  Link,
  Footer,
  User,
  IconButton,
  Content,
});
