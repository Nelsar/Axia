import { useCallback, useState } from "react";
import type { JSX, ReactNode } from "react";
import { useBulkOperations } from "../hooks/useBulkOperations";
import { useUserDialogs } from "../hooks/useUserDialogs";
import { useUsernameGenerator } from "../hooks/useUsernameGenerator";
import { useUserPermissions } from "../hooks/usePermissions";
import { useUsersList } from "../hooks/useUsersList";
import { UsersView } from "../components/users/UsersView";
import type { UsersTab } from "../components/users/UsersView";
import { UserDetailDialogContainer } from "./UserDetailDialogContainer";
import { UserFormDialogContainer } from "./UserFormDialogContainer";

/**
 * The container for the Users screen: it joins the hooks to the presentational <UsersView>. No
 * markup, no class names, no direct calls to the api — only wiring.
 */
export function UsersPage(): JSX.Element {
  const perms = useUserPermissions();
  const [tab, setTab] = useState<UsersTab>("list");

  // The "Bulk operations" and "Generator" tabs request nothing until a button is pressed, so their
  // hooks can live here: an inactive tab costs not a single request, while the text typed into it
  // survives switching tabs.
  const list = useUsersList();
  const bulk = useBulkOperations();
  const generator = useUsernameGenerator();
  const dialogs = useUserDialogs();

  const openedUser = dialogs.dialog?.item ?? null;

  const deleteFromDetail = useCallback(async (): Promise<void> => {
    if (openedUser === null) return;
    const deleted = await list.removeUser(openedUser);
    if (deleted) dialogs.close();
  }, [dialogs, list, openedUser]);

  let dialog: ReactNode = null;
  if (dialogs.dialog !== null) {
    dialog =
      dialogs.dialog.kind === "form" ? (
        <UserFormDialogContainer
          key={openedUser?.id ?? "new"}
          user={openedUser}
          onSaved={list.reload}
          onClose={dialogs.close}
        />
      ) : (
        openedUser !== null && (
          <UserDetailDialogContainer
            key={openedUser.id}
            user={openedUser}
            perms={perms}
            onEdit={() => dialogs.openEdit(openedUser)}
            onDelete={() => void deleteFromDetail()}
            onClose={dialogs.close}
          />
        )
      );
  }

  return (
    <UsersView
      tab={tab}
      onTabChange={setTab}
      perms={perms}
      onCreate={dialogs.openCreate}
      dialog={dialog}
      list={{
        query: list.query,
        onQueryChange: list.setQuery,
        onRefresh: list.reload,
        status: list.status,
        error: list.error,
        items: list.items,
        isComplete: list.isComplete,
        hasMore: list.hasMore,
        isLoadingMore: list.isLoadingMore,
        onLoadMore: list.loadMore,
        onSelect: dialogs.openDetail,
        onEdit: dialogs.openEdit,
        onDelete: (user) => void list.removeUser(user),
      }}
      bulk={{
        input: bulk.input,
        onInputChange: bulk.setInput,
        status: bulk.status,
        isBusy: bulk.isBusy,
        notice: bulk.notice,
        error: bulk.error,
        result: bulk.result,
        onRunSync: () => void bulk.runSync(),
        onRunAsync: () => void bulk.runAsync(),
      }}
      generator={{
        values: generator.values,
        onFieldChange: generator.setField,
        status: generator.status,
        result: generator.result,
        error: generator.error,
        onRun: () => void generator.run(),
      }}
    />
  );
}
