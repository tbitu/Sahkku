/**
 * The locale state and lookup layer: reactive, dependency-free and DOM-free.
 *
 * `strings.ts` holds the text; nothing here knows what any of it says. Three rules shape the lookups:
 *
 * 1. **Every lookup is total.** A key the active locale does not define resolves through English and
 *    finally to the key itself, so a missing translation shows a readable placeholder instead of a
 *    blank screen or a thrown error.
 * 2. **Placeholders are never silently dropped.** `{name}` substitution is textual and leaves unknown
 *    placeholders in place, because a half-filled sentence is a bug report while a vanished one is not.
 * 3. **A locale change is an event.** `subscribe` hands the listener the new *and* the previous locale,
 *    which is all a view needs to repaint every string it has already rendered.
 *
 * `I18n` also exposes `t` as a bound arrow function, so the whole object can be handed to a view that
 * only wants a `Translator` (`board.ts`, `dice.ts`, `MatchController`).
 */

import {
  DefaultLocale,
  LocaleInfos,
  Locales,
  flattenStringTable,
  stringTables,
  type Locale,
  type LocaleInfo,
  type StringKey,
} from "./strings";

/** Values a template may interpolate. Numbers are rendered with `String(...)`. */
export type TranslateParams = Record<string, string | number>;

/**
 * The narrowest contract a view needs: "give me the text for this key in the current language".
 * Everything user-visible in the client takes one of these, defaulting to English (`defaultTranslate`).
 */
export type Translator = (key: StringKey, params?: TranslateParams) => string;

/** Called after the locale changed, with the new locale and the one it replaced. */
export type LocaleListener = (locale: Locale, previous: Locale) => void;

/** The flattened table of each locale, built once. Rebuilt nowhere: `strings.ts` is static data. */
const flattenedTables: Record<Locale, Map<string, string>> = {
  se: flattenStringTable(stringTables.se),
  no: flattenStringTable(stringTables.no),
  en: flattenStringTable(stringTables.en),
};

/** The `dotted.key -> text` map of one locale, for tests and for tooling. */
export function flattenLocale(locale: Locale): Map<string, string> {
  return flattenedTables[locale];
}

/**
 * Reads a locale code out of untrusted input (a stored setting, a `navigator.language`): `nb`/`nn`
 * and a regional suffix such as `no-NO` all settle on the Norwegian table, everything unknown on
 * English. Never throws, so a corrupt `localStorage` value cannot stop the client from starting.
 */
export function normalizeLocale(value: string | null | undefined): Locale {
  if (value == null) return DefaultLocale;

  const trimmed = value.trim().toLowerCase().replace(/_/g, "-");
  if (trimmed.length === 0) return DefaultLocale;

  const base = trimmed.split("-")[0] ?? "";
  if (base === "se" || base === "smi" || base === "sma" || base === "smj" || base === "sme") return "se";
  if (base === "no" || base === "nb" || base === "nn" || base === "nor") return "no";
  if (base === "en") return "en";
  return DefaultLocale;
}

/** Fills `{name}` placeholders. Unknown placeholders are left as they are (see the module note). */
export function interpolate(template: string, params?: TranslateParams): string {
  if (params == null) return template;

  return template.replace(/\{(\w+)\}/g, (match, name: string) => {
    const value = params[name];
    return value == null ? match : String(value);
  });
}

/**
 * The text of `key` in `locale`, falling back to English and then to the key itself. `params` are
 * interpolated after the fallback, so an English fallback still receives the caller's values.
 */
export function translate(locale: Locale, key: StringKey, params?: TranslateParams): string {
  const text = flattenedTables[locale].get(key) ?? flattenedTables[DefaultLocale].get(key);
  if (text == null) return key;
  return interpolate(text, params);
}

/** The English translator, and the default for every view that was handed no other. */
export const defaultTranslate: Translator = (key, params) => translate(DefaultLocale, key, params);

/**
 * The live locale of one client: the current code, the `t` lookup and the change subscription.
 * Deliberately tiny — it holds no DOM and renders nothing, so a locale switch is one `setLocale` call
 * followed by whatever the subscribed views choose to repaint.
 */
export class I18n {
  /** The lookup bound to this instance; safe to pass around as a `Translator`. */
  readonly t: Translator;

  private current: Locale;
  private readonly listeners = new Set<LocaleListener>();

  constructor(locale: Locale | string | null | undefined = DefaultLocale) {
    this.current = normalizeLocale(locale);
    this.t = (key, params) => translate(this.current, key, params);
  }

  /** The active locale code. */
  get locale(): Locale {
    return this.current;
  }

  /** Every locale a switcher may offer, with its native and English names. */
  get locales(): readonly LocaleInfo[] {
    return LocaleInfos;
  }

  /**
   * Switches locale and notifies every subscriber. Returns the locale that is now active, i.e. the
   * normalized form of the request; an unknown or already-active code notifies nobody.
   */
  setLocale(locale: Locale | string | null | undefined): Locale {
    const next = normalizeLocale(locale);
    if (next === this.current) return this.current;

    const previous = this.current;
    this.current = next;
    for (const listener of [...this.listeners]) listener(next, previous);
    return next;
  }

  /** Registers a change listener; returns the unsubscribe function. */
  subscribe(listener: LocaleListener): () => void {
    this.listeners.add(listener);
    return () => {
      this.listeners.delete(listener);
    };
  }
}

export { DefaultLocale, Locales, LocaleInfos, stringTables };
export type { Locale, LocaleInfo, StringKey, StringTable } from "./strings";
