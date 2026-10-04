import { ar } from "./messages.ar";
import { de } from "./messages.de";
import { en } from "./messages.en";
import type { MessageKey } from "./messages.en";
import { es } from "./messages.es";
import { fr } from "./messages.fr";
import { he } from "./messages.he";
import { kk } from "./messages.kk";
import { ru } from "./messages.ru";
import { zh } from "./messages.zh";

export type { MessageKey };

/**
 * One entry per language, and adding a language is exactly this: a dictionary file and a line
 * here. Nothing else in the console names a language — the picker, the stored preference and the
 * Accept-Language header all read this list.
 */
export interface LanguageDefinition {
  readonly code: string;
  /**
   * The language's name in itself. Never translated: the person reading it is precisely the one
   * who cannot read the current language.
   */
  readonly nativeName: string;
  /**
   * Writing direction, applied to <html> by applyLanguage.
   *
   * Nothing else in the console reads it: the stylesheet uses logical properties
   * (inline-start/inline-end rather than left/right), so the browser mirrors the layout from this
   * attribute alone. That is why adding Arabic and Hebrew took no [dir="rtl"] block — a mirrored
   * copy of every rule would have been a second stylesheet, silently drifting from the first.
   */
  readonly dir: "ltr" | "rtl";
  readonly messages: Readonly<Record<MessageKey, string>>;
}

export const LANGUAGES: readonly LanguageDefinition[] = [
  { code: "en", nativeName: "English", dir: "ltr", messages: en },
  { code: "ru", nativeName: "Русский", dir: "ltr", messages: ru },
  { code: "kk", nativeName: "Қазақша", dir: "ltr", messages: kk },
  { code: "de", nativeName: "Deutsch", dir: "ltr", messages: de },
  { code: "es", nativeName: "Español", dir: "ltr", messages: es },
  { code: "fr", nativeName: "Français", dir: "ltr", messages: fr },
  { code: "zh", nativeName: "中文", dir: "ltr", messages: zh },
  { code: "ar", nativeName: "العربية", dir: "rtl", messages: ar },
  { code: "he", nativeName: "עברית", dir: "rtl", messages: he },
];

const LANGUAGE_KEY = "axia_language";

/**
 * English, and it is also the source of the key list (see messages.en.ts). The default language
 * and the guaranteed-complete one should be the same language; anything else makes "we fell back"
 * and "we have no translation" two different failures with one symptom.
 */
const DEFAULT_LANGUAGE = "en";

function definitionFor(code: string): LanguageDefinition {
  // The zeroth entry is the default and LANGUAGES is never empty, but the compiler cannot know
  // that under noUncheckedIndexedAccess — hence the explicit find rather than an index.
  const found = LANGUAGES.find((language) => language.code === code);
  if (found !== undefined) return found;

  const fallback = LANGUAGES.find((language) => language.code === DEFAULT_LANGUAGE);
  if (fallback === undefined) throw new Error("The default language is missing from LANGUAGES.");
  return fallback;
}

/**
 * Read from localStorage on every call rather than cached in the module: the router mounts a
 * fresh root on each navigation, and a value captured in a closure would outlive a language
 * change. Whatever is stored is arbitrary text — anything not in LANGUAGES is the default.
 */
export function readLanguage(): string {
  return definitionFor(localStorage.getItem(LANGUAGE_KEY) ?? DEFAULT_LANGUAGE).code;
}

export function writeLanguage(code: string): void {
  localStorage.setItem(LANGUAGE_KEY, definitionFor(code).code);
}

/**
 * Language lives as an attribute on <html> for the same reason the theme does: it is read from
 * outside the React tree. Here that reader is the browser — hyphenation, spell-checking in
 * fields, and how a screen reader pronounces the page.
 */
export function applyLanguage(code: string): void {
  const language = definitionFor(code);

  document.documentElement.setAttribute("lang", language.code);
  document.documentElement.setAttribute("dir", language.dir);

  // The tab title is interface too; it just lives in <head>.
  document.title = language.messages["app.title"];
}

/** Called once at startup, before the first render. */
export function applyStoredLanguage(): void {
  applyLanguage(readLanguage());
}

/**
 * Text by key.
 *
 * The key is symbolic rather than the English text itself. Natural keys (t("Users") as the key)
 * are cheaper to introduce but bring back the very disease this moved away from: rewording a
 * sentence becomes a code change, and comparing against text stays possible.
 *
 * Substitutions are named: t("users.found", { count: 12 }). Positional {0} reads worse in a
 * language whose word order differs, which is the whole reason for doing this.
 */
export function t(key: MessageKey, params?: Readonly<Record<string, string | number>>): string {
  const template = definitionFor(readLanguage()).messages[key];

  if (params === undefined) return template;

  return template.replace(/\{(\w+)\}/g, (whole, name: string) => {
    const value = params[name];
    // An unknown substitution is left as it is: a visible "{count}" is a distinct mistake that
    // gets noticed, where emptiness in its place lies quietly.
    return value === undefined ? whole : String(value);
  });
}
