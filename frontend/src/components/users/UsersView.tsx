import type { JSX, ReactNode } from "react";
import { t } from "../../lib/i18n";
import type { UserPermissions } from "../../hooks/usePermissions";
import { PageHeader } from "../PageHeader";
import { Tabs } from "../Tabs";
import { BulkPanel } from "./BulkPanel";
import type { BulkPanelProps } from "./BulkPanel";
import { GeneratorPanel } from "./GeneratorPanel";
import type { GeneratorPanelProps } from "./GeneratorPanel";
import { UsersListPanel } from "./UsersListPanel";
import type { UsersListPanelProps } from "./UsersListPanel";

/** The screen’s tabs. A union rather than a string: `tab="lst"` does not build. */
export type UsersTab = "list" | "bulk" | "generate";

export interface UsersViewProps {
  tab: UsersTab;
  onTabChange: (tab: UsersTab) => void;
  perms: UserPermissions;
  onCreate: () => void;
  /** The panels’ props are assembled by the container; `perms` on the list is filled in here. */
  list: Omit<UsersListPanelProps, "perms">;
  bulk: BulkPanelProps;
  generator: GeneratorPanelProps;
  /** A slot: which dialog to show is the container’s decision. The view need not know there are two. */
  dialog?: ReactNode;
}

/** A type guard for the Tabs callback: it hands back a string, and the screen knows only three tabs. */
function isUsersTab(value: string): value is UsersTab {
  return value === "list" || value === "bulk" || value === "generate";
}

/**
 * The whole screen as a presentational component: no useState, no useEffect, no calls to the api.
 * It can be rendered in a test with any data — including states a real directory makes hard to
 * reach (an LDAP failure, an empty result, a role without delete rights).
 */
export function UsersView({
  tab,
  onTabChange,
  perms,
  onCreate,
  list,
  bulk,
  generator,
  dialog,
}: UsersViewProps): JSX.Element {
  return (
    <div className="page">
      <PageHeader title={t("nav.users")}>
        {perms.canCreate && (
          <button className="btn btn--primary" onClick={onCreate}>{t("users.create")}</button>
        )}
      </PageHeader>

      <Tabs
        value={tab}
        onChange={(next: string) => {
          if (isUsersTab(next)) onTabChange(next);
        }}
      >
        <Tabs.List label={t("users.tabs")}>
          <Tabs.Tab value="list">{t("users.tabList")}</Tabs.Tab>
          {/* Offered only to whoever the endpoints behind them will serve; they used to be shown
              to everybody and answer every action with 403. */}
          {perms.canBulk && <Tabs.Tab value="bulk">{t("users.tabBulk")}</Tabs.Tab>}
          {perms.canGenerate && <Tabs.Tab value="generate">{t("users.tabGenerate")}</Tabs.Tab>}
        </Tabs.List>

        <Tabs.Panel value="list">
          <UsersListPanel {...list} perms={perms} />
        </Tabs.Panel>
        <Tabs.Panel value="bulk">
          <BulkPanel {...bulk} />
        </Tabs.Panel>
        <Tabs.Panel value="generate">
          <GeneratorPanel {...generator} />
        </Tabs.Panel>
      </Tabs>

      {dialog}
    </div>
  );
}
