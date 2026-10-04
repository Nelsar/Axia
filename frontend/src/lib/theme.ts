const THEME_KEY = "axia_theme";

export type Theme = "light" | "dark";

/** localStorage holds arbitrary text: anything that is not "dark" counts as the light theme. */
export function readTheme(): Theme {
  return localStorage.getItem(THEME_KEY) === "dark" ? "dark" : "light";
}

export function writeTheme(theme: Theme): void {
  localStorage.setItem(THEME_KEY, theme);
}

/**
 * The theme lives as an attribute on <html> rather than in the React tree: CSS reads it
 * (`:root[data-theme="dark"]`), and it is needed before mounting — including on the sign-in
 * screen, which is drawn outside the shell.
 */
export function applyTheme(theme: Theme): void {
  document.documentElement.setAttribute("data-theme", theme);
}

/** Called once at startup, before the first render. */
export function applyStoredTheme(): void {
  applyTheme(readTheme());
}
