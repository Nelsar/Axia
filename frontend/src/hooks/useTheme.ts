import { useCallback, useState } from "react";
import { applyTheme, readTheme, writeTheme } from "../lib/theme";
import type { Theme } from "../lib/theme";

export interface ThemeState {
  readonly theme: Theme;
  readonly toggle: () => void;
}

/**
 * The theme is the only state the shell owns.
 *
 * The attribute on <html> is set synchronously in the handler rather than in a useEffect: it lives
 * outside the React tree, and writing it before the render avoids a frame with the old theme. The
 * current value is read from localStorage rather than from a closure, so the toggle stays correct
 * even when a navigation mounts a fresh root.
 */
export function useTheme(): ThemeState {
  const [theme, setTheme] = useState<Theme>(() => readTheme());

  const toggle = useCallback((): void => {
    const next: Theme = readTheme() === "light" ? "dark" : "light";
    writeTheme(next);
    applyTheme(next);
    setTheme(next);
  }, []);

  return { theme, toggle };
}
