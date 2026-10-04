import type { UserRole } from "../api/dto";
import type { MessageKey } from "./i18n";

export interface NavItem {
  readonly path: string;
  /** A dictionary key rather than a finished label: a section is called different things and stays the same section. */
  readonly label: MessageKey;
  readonly icon: string;
  /** The item is hidden without the role. The real permission check belongs to the backend. */
  readonly roles?: readonly UserRole[];
}

export interface NavGroup {
  /**
   * The group's permanent name, never shown.
   *
   * A group used to be identified by its heading: the heading was also the React key
   * (`key={group.section}`) and also the marker for "a group with no heading" (an empty string).
   * Translating the menu therefore changed the identity of its groups, and "" staying "" in
   * English would have been coincidence. The heading is now just text that gets drawn; identity
   * is held by the id.
   */
  readonly id: string;
  /** null is a group with no heading (the dashboard), not a heading somebody forgot to fill in. */
  readonly title: MessageKey | null;
  readonly items: readonly NavItem[];
}

/**
 * The map of console sections is data rather than markup: the hook reads it to filter by role
 * and the tests read it to check that audit stays invisible to an operator.
 */
export const NAV_GROUPS: readonly NavGroup[] = [
  { id: "root", title: null, items: [{ path: "/", label: "nav.dashboard", icon: "▣" }] },
  {
    id: "directory",
    title: "nav.section.directory",
    items: [
      { path: "/users", label: "nav.users", icon: "◉" },
      { path: "/groups", label: "nav.groups", icon: "◎" },
      { path: "/org-units", label: "nav.orgUnits", icon: "▤" },
      { path: "/computers", label: "nav.computers", icon: "▢" },
    ],
  },
  {
    id: "management",
    title: "nav.section.management",
    items: [
      { path: "/providers", label: "nav.providers", icon: "▧", roles: ["Admin"] },
      { path: "/role-mappings", label: "nav.roleMappings", icon: "◇", roles: ["Admin"] },
      { path: "/rate-limits", label: "nav.rateLimits", icon: "◆", roles: ["Admin"] },
    ],
  },
  {
    id: "system",
    title: "nav.section.system",
    items: [{ path: "/audit", label: "nav.audit", icon: "▥", roles: ["Admin"] }],
  },
];
