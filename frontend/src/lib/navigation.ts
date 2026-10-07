import type { Permission } from "../api/dto";
import type { MessageKey } from "./i18n";

export interface NavItem {
  readonly path: string;
  /** A dictionary key rather than a finished label: a section is called different things and stays the same section. */
  readonly label: MessageKey;
  readonly icon: string;
  /**
   * The item is hidden without this permission — the one the page's own listing needs. It used
   * to be a role, which cannot express "anyone whose roles add up to this". The real check
   * belongs to the backend.
   */
  readonly permission?: Permission;
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
 * The map of console sections is data rather than markup: the hook reads it to filter by
 * permission, and main.tsx guards each route with the same permission.
 */
export const NAV_GROUPS: readonly NavGroup[] = [
  { id: "root", title: null, items: [{ path: "/", label: "nav.dashboard", icon: "▣" }] },
  {
    id: "directory",
    title: "nav.section.directory",
    items: [
      { path: "/users", label: "nav.users", icon: "◉", permission: "users.read" },
      { path: "/groups", label: "nav.groups", icon: "◎", permission: "groups.read" },
      { path: "/org-units", label: "nav.orgUnits", icon: "▤", permission: "orgunits.read" },
      { path: "/computers", label: "nav.computers", icon: "▢", permission: "computers.read" },
    ],
  },
  {
    id: "management",
    title: "nav.section.management",
    items: [
      { path: "/providers", label: "nav.providers", icon: "▧", permission: "providers.manage" },
      { path: "/role-mappings", label: "nav.roleMappings", icon: "◇", permission: "roles.manage" },
      { path: "/roles", label: "nav.roles", icon: "◈", permission: "roles.manage" },
      { path: "/rate-limits", label: "nav.rateLimits", icon: "◆", permission: "ratelimits.manage" },
    ],
  },
  {
    id: "system",
    title: "nav.section.system",
    items: [{ path: "/audit", label: "nav.audit", icon: "▥", permission: "audit.read" }],
  },
];
