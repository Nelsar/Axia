import { createContext, useContext, useId, useMemo, useState } from "react";
import { t } from "../lib/i18n";
import type { JSX, ReactNode } from "react";

/**
 * Compound components: <Tabs> holds the selected tab in context, and <Tabs.List>/<Tabs.Tab>/
 * <Tabs.Panel> agree with it implicitly.
 *
 * One set of tab mechanics, two looks: an underline (the console screens) and a pill switch (the
 * sign-in screen).
 */
export type TabsVariant = "underline" | "pill";

interface VariantClasses {
  readonly list: string;
  readonly tab: string;
  readonly active: string;
}

const VARIANTS: Readonly<Record<TabsVariant, VariantClasses>> = {
  underline: { list: "tabs", tab: "tabs__tab", active: "tabs__tab--active" },
  pill: { list: "login-tabs", tab: "login-tab", active: "active" },
};

interface TabsContextValue {
  readonly active: string;
  readonly baseId: string;
  readonly classes: VariantClasses;
  readonly select: (next: string) => void;
}

// A null default is the only way to tell "inside <Tabs>" from "outside it": otherwise a component
// outside the provider would quietly receive a made-up context.
const TabsContext = createContext<TabsContextValue | null>(null);

function useTabsContext(componentName: string): TabsContextValue {
  const context = useContext(TabsContext);
  if (context === null) {
    throw new Error(t("shell.tabsOutsideProvider", { component: componentName, parent: "Tabs" }));
  }
  return context;
}

export interface TabsProps {
  /** Controlled mode: the caller supplies the value. */
  value?: string | undefined;
  /** Uncontrolled mode: the selected tab stays an internal detail of the component. */
  defaultValue?: string | undefined;
  onChange?: ((value: string) => void) | undefined;
  variant?: TabsVariant | undefined;
  children: ReactNode;
}

function TabsRoot({ value, defaultValue = "", onChange, variant = "underline", children }: TabsProps): JSX.Element {
  const [internalValue, setInternalValue] = useState<string>(defaultValue);
  const isControlled = value !== undefined;
  const active = isControlled ? value : internalValue;
  const baseId = useId();

  const context = useMemo<TabsContextValue>(
    () => ({
      active,
      baseId,
      classes: VARIANTS[variant],
      select: (next: string): void => {
        if (!isControlled) setInternalValue(next);
        onChange?.(next);
      },
    }),
    [active, baseId, isControlled, onChange, variant]
  );

  return <TabsContext.Provider value={context}>{children}</TabsContext.Provider>;
}

export interface TabsListProps {
  label?: string | undefined;
  children: ReactNode;
}

function TabsList({ label = t("shell.tabsDefaultLabel"), children }: TabsListProps): JSX.Element {
  const { classes } = useTabsContext("Tabs.List");
  return (
    <div className={classes.list} role="tablist" aria-label={label}>
      {children}
    </div>
  );
}

export interface TabProps {
  value: string;
  children: ReactNode;
}

function Tab({ value, children }: TabProps): JSX.Element {
  const { active, baseId, classes, select } = useTabsContext("Tabs.Tab");
  const selected = active === value;

  return (
    <button
      type="button"
      role="tab"
      id={`${baseId}-tab-${value}`}
      aria-selected={selected}
      aria-controls={`${baseId}-panel-${value}`}
      tabIndex={selected ? 0 : -1}
      className={`${classes.tab}${selected ? ` ${classes.active}` : ""}`}
      onClick={() => select(value)}
    >
      {children}
    </button>
  );
}

export interface TabsPanelProps {
  value: string;
  children: ReactNode;
}

function TabsPanel({ value, children }: TabsPanelProps): JSX.Element | null {
  const { active, baseId } = useTabsContext("Tabs.Panel");
  if (active !== value) return null;
  return (
    <div role="tabpanel" id={`${baseId}-panel-${value}`} aria-labelledby={`${baseId}-tab-${value}`}>
      {children}
    </div>
  );
}

export const Tabs = Object.assign(TabsRoot, { List: TabsList, Tab, Panel: TabsPanel });
