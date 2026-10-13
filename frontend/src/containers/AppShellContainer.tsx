import type { JSX, ReactNode } from "react";
import { useAppShell } from "../hooks/useAppShell";
import { ShellView } from "../components/shell/ShellView";

export interface AppShellContainerProps {
  /** The current route’s path: the router knows it, the shell does not. */
  activePath: string;
  children: ReactNode;
}

/**
 * Wires the hook to the markup, and nothing else. The name and role fall back to an empty string:
 * this screen is only reachable through the guard, but the SessionUser | null type does not know
 * that.
 */
export function AppShellContainer({ activePath, children }: AppShellContainerProps): JSX.Element {
  const shell = useAppShell();

  return (
    <ShellView
      activePath={activePath}
      apiBaseUrl={shell.apiBaseUrl}
      directoryName={shell.directoryName}
      userName={shell.user?.name ?? ""}
      userRole={shell.user?.role ?? ""}
      initials={shell.initials}
      nav={shell.nav}
      theme={shell.theme}
      onToggleTheme={shell.onToggleTheme}
      onLogout={shell.onLogout}
    >
      {children}
    </ShellView>
  );
}
