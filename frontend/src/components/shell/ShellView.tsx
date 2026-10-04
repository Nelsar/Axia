import type { ReactNode } from "react";
import type { NavGroup } from "../../lib/navigation";
import type { Theme } from "../../lib/theme";
import { t } from "../../lib/i18n";
import { LanguagePicker } from "../LanguagePicker";
import { AppShell } from "./AppShell";

export interface ShellViewProps {
  /** The current path; the menu item is highlighted from it. */
  activePath: string;
  apiBaseUrl: string;
  /** null means the session is in the default directory, which gets no badge of its own. */
  directoryName: string | null;
  userName: string;
  userRole: string;
  initials: string;
  /** The groups arrive already filtered: the view does not decide who sees what. */
  nav: readonly NavGroup[];
  theme: Theme;
  onToggleTheme: () => void;
  onLogout: () => void;
  /** A slot: the router picks the page, and the shell knows nothing about it. */
  children: ReactNode;
}

/**
 * The shell's presentational component: it assembles AppShell's parts from data received as
 * props. It can be rendered in a test with any session — including ones a real directory will not
 * produce (a role with no reachable section, a name without spaces, an empty API address).
 */
export function ShellView({
  activePath,
  apiBaseUrl,
  directoryName,
  userName,
  userRole,
  initials,
  nav,
  theme,
  onToggleTheme,
  onLogout,
  children,
}: ShellViewProps): JSX.Element {
  return (
    <AppShell activePath={activePath}>
      <AppShell.Sidebar>
        <AppShell.Brand logo="D" name="DirCore" />
        <AppShell.ApiBadge url={apiBaseUrl} />
        {directoryName !== null && <AppShell.DirectoryBadge name={directoryName} />}

        <AppShell.Nav>
          {nav.map((group) => (
            <AppShell.Section key={group.id} title={group.title === null ? null : t(group.title)}>
              {group.items.map((item) => (
                <AppShell.Link key={item.path} path={item.path} icon={item.icon}>
                  {t(item.label)}
                </AppShell.Link>
              ))}
            </AppShell.Section>
          ))}
        </AppShell.Nav>

        <AppShell.Footer>
          <AppShell.User initials={initials} name={userName} role={userRole} />
          <AppShell.IconButton
            title={theme === "dark" ? t("shell.themeLight") : t("shell.themeDark")}
            onClick={onToggleTheme}
          >
            ◐
          </AppShell.IconButton>
          <LanguagePicker />
          <AppShell.IconButton title={t("shell.logout")} onClick={onLogout}>
            ⎋
          </AppShell.IconButton>
        </AppShell.Footer>
      </AppShell.Sidebar>

      <AppShell.Content>{children}</AppShell.Content>
    </AppShell>
  );
}
