import { useCallback, useMemo } from "react";
import { t } from "../lib/i18n";
import { api } from "../api";
import type { OrganizationalUnitDto, PagedResponse } from "../api/dto";
import { confirmAndRun } from "../lib/actions";
import { useDialog } from "../hooks/useDialog";
import { useIsAdmin } from "../hooks/usePermissions";
import { usePagedList } from "../hooks/usePagedList";
import type { PageRequest } from "../hooks/usePagedList";
import { DirectoryView } from "../components/directory/DirectoryView";
import { parentDnOptions } from "../components/directory/ParentDnField";
import { orgUnitColumns } from "../components/orgUnits/orgUnitColumns";
import { OrgUnitFormDialogContainer } from "./OrgUnitFormDialogContainer";

const fetchOrgUnits = (request: PageRequest): Promise<PagedResponse<OrganizationalUnitDto>> =>
  api.orgUnits.search(request);

export function OrgUnitsPage(): JSX.Element {
  const canManage = useIsAdmin();
  const list = usePagedList<OrganizationalUnitDto>({
    fetchPage: fetchOrgUnits,
    errorFallback: t("orgUnits.loadFailed"),
  });
  const { dialog, open, close } = useDialog<"form", OrganizationalUnitDto>();
  const { reload } = list;

  const removeUnit = useCallback(
    async (unit: OrganizationalUnitDto): Promise<void> => {
      const deleted = await confirmAndRun({
        question:
          t("orgUnits.deleteConfirm", { name: unit.name, dn: unit.distinguishedName }),
        run: () => api.orgUnits.remove(unit.id),
        success: t("orgUnits.deleted"),
        // A 409 is the directory refusing to delete a non-empty container. That is exactly the
        // answer an administrator needs, so the API's message is shown as it is.
        failure: t("common.deleteFailed"),
      });
      if (deleted) reload();
    },
    [reload]
  );

  const columns = useMemo(
    () =>
      orgUnitColumns({
        canManage,
        onEdit: (unit) => open("form", unit),
        onDelete: (unit) => void removeUnit(unit),
      }),
    [canManage, open, removeUnit]
  );

  const parentOptions = useMemo(
    () => parentDnOptions(list.items, { parents: false, ownDn: true }),
    [list.items]
  );

  return (
    <DirectoryView
      title={t("orgUnits.title")}
      hint={t("orgUnits.hint")}
      createLabel={t("orgUnits.create")}
      onCreate={() => open("form")}
      canManage={canManage}
      query={list.query}
      onQueryChange={list.setQuery}
      searchPlaceholder={t("groups.searchPlaceholder")}
      status={list.status}
      error={list.error}
      items={list.items}
      columns={columns}
      emptyText={t("orgUnits.empty")}
      incompleteNoun={t("orgUnits.noun")}
      isComplete={list.isComplete}
      hasMore={list.hasMore}
      isLoadingMore={list.isLoadingMore}
      onLoadMore={list.loadMore}
      dialog={
        dialog !== null && (
          <OrgUnitFormDialogContainer
            key={dialog.item?.id ?? "new"}
            unit={dialog.item}
            parentOptions={parentOptions}
            onSaved={reload}
            onClose={close}
          />
        )
      }
    />
  );
}
