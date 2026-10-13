import type { ChangeEvent, JSX } from "react";
import { LANGUAGES, applyLanguage, readLanguage, t, writeLanguage } from "../lib/i18n";
import { navigate } from "../lib/router";

/**
 * Interface language, as a control rather than a toggle.
 *
 * A toggle worked while there were two languages and stops working at seven: cycling through a
 * list by clicking is not choosing from it. Each option is labelled in its own language, which is
 * the one label that must never be translated — the person reading it is precisely the one who
 * cannot read the current language.
 *
 * Self-contained on purpose: it reads and writes the stored language directly instead of taking
 * props. The alternative is threading a value and a callback through the shell, the login screen
 * and everything between them, for a control that owns its own state anyway.
 */
export function LanguagePicker(): JSX.Element {
  const current = readLanguage();

  /**
   * Changing the language remounts the current route rather than updating React state.
   *
   * t() reads the language at call time, so the whole tree has to be redrawn at once — and the
   * router already mounts a fresh root on every navigation, so navigating to the current path
   * does exactly that. The alternative — a context and a hook in each of sixty components —
   * achieves the same thing at the cost of editing every one of them.
   */
  function change(event: ChangeEvent<HTMLSelectElement>): void {
    writeLanguage(event.target.value);
    applyLanguage(event.target.value);
    navigate(location.hash.slice(1) || "/");
  }

  return (
    <select
      className="language-picker"
      value={current}
      title={t("shell.language")}
      aria-label={t("shell.language")}
      onChange={change}
    >
      {LANGUAGES.map((language) => (
        <option key={language.code} value={language.code}>
          {language.nativeName}
        </option>
      ))}
    </select>
  );
}
