import { useCallback, useMemo } from "react";
import { t } from "../lib/i18n";
import { api } from "../api";
import type { GroupDto, PagedResponse } from "../api/dto";
import { confirmAndRun } from "../lib/actions";
import { useDialog } from "../hooks/useDialog";
import { useIsAdmin } from "../hooks/usePermissions";
import { usePagedList } from "../hooks/usePagedList";
import type { PageRequest } from "../hooks/usePagedList";
import { DirectoryView } from "../components/directory/DirectoryView";
import { parentDnOptions } from "../components/directory/ParentDnField";
import { groupColumns } from "../components/groups/groupColumns";
import { GroupMembersDialog } from "../components/groups/GroupMembersDialog";
import { GroupFormDialogContainer } from "./GroupFormDialogContainer";

/** The screen’s dialogs: the form and the member list. */
type GroupDialogKind = "form" | "members";

// The reference is stable between renders — otherwise usePagedList would refetch the list forever.
const fetchGroups = (request: PageRequest): Promise<PagedResponse<GroupDto>> => api.groups.search(request);

export function GroupsPage(): JSX.Element {
  const canManage = useIsAdmin();
  // A 501 lands here when the directory is served by the Windows ActiveDirectory provider: it does
  // not manage groups, and its message is more use than an empty list.
  const list = usePagedList<GroupDto>({
    fetchPage: fetchGroups,
    errorFallback: t("groups.loadFailed"),
  });
  const { dialog, open, close } = useDialog<GroupDialogKind, GroupDto>();
  const { reload } = list;

  const removeGroup = useCallback(
    async (group: GroupDto): Promise<void> => {
      const deleted = await confirmAndRun({
        question:
          t("groups.deleteConfirm", {
            name: group.name,
            dn: group.distinguishedName,
            count: group.members.length,
          }),
        run: () => api.groups.remove(group.id),
        success: t("groups.deleted"),
        failure: t("common.deleteFailed"),
      });
      if (deleted) reload();
    },
    [reload]
  );

  const columns = useMemo(
    () =>
      groupColumns({
        canManage,
        onShowMembers: (group) => open("members", group),
        onEdit: (group) => open("form", group),
        onDelete: (group) => void removeGroup(group),
      }),
    [canManage, open, removeGroup]
  );

  const parentOptions = useMemo(() => parentDnOptions(list.items, { ownDn: true }), [list.items]);

  const renderDialog = (): JSX.Element | null => {
    if (dialog === null) return null;
    if (dialog.kind === "members") {
      return dialog.item === null ? null : (
        <GroupMembersDialog key={dialog.item.id} group={dialog.item} onClose={close} />
      );
    }
    return (
      <GroupFormDialogContainer
        key={dialog.item?.id ?? "new"}
        group={dialog.item}
        parentOptions={parentOptions}
        onSaved={reload}
        onClose={close}
      />
    );
  };

  return (
    <DirectoryView
      title={t("nav.groups")}
      hint={t("groups.hint")}
      createLabel={t("groups.create")}
      onCreate={() => open("form")}
      canManage={canManage}
      query={list.query}
      onQueryChange={list.setQuery}
      searchPlaceholder={t("groups.searchPlaceholder")}
      searchHint={t("groups.searchHint")}
      status={list.status}
      error={list.error}
      items={list.items}
      columns={columns}
      emptyText={t("groups.empty")}
      incompleteNoun={t("groups.noun")}
      isComplete={list.isComplete}
      hasMore={list.hasMore}
      isLoadingMore={list.isLoadingMore}
      onLoadMore={list.loadMore}
      dialog={renderDialog()}
    />
  );
}
