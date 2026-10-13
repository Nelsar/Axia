import { useCallback, useMemo, type JSX } from "react";
import { t } from "../lib/i18n";
import { api } from "../api";
import type { ComputerDto, PagedResponse } from "../api/dto";
import { confirmAndRun } from "../lib/actions";
import { useDialog } from "../hooks/useDialog";
import { useCan } from "../hooks/usePermissions";
import { usePagedList } from "../hooks/usePagedList";
import type { PageRequest } from "../hooks/usePagedList";
import { DirectoryView } from "../components/directory/DirectoryView";
import { parentDnOptions } from "../components/directory/ParentDnField";
import { computerColumns } from "../components/computers/computerColumns";
import { ComputerFormDialogContainer } from "./ComputerFormDialogContainer";

const fetchComputers = (request: PageRequest): Promise<PagedResponse<ComputerDto>> =>
  api.computers.search(request);

export function ComputersPage(): JSX.Element {
  const canManage = useCan("computers.manage");
  const list = usePagedList<ComputerDto>({
    fetchPage: fetchComputers,
    errorFallback: t("computers.loadFailed"),
  });
  const { dialog, open, close } = useDialog<"form", ComputerDto>();
  const { reload } = list;

  const removeComputer = useCallback(
    async (computer: ComputerDto): Promise<void> => {
      const deleted = await confirmAndRun({
        // Spelled out rather than "are you sure?": deleting the entry is not a bookkeeping edit,
        // it breaks the machine's trust with the domain.
        question:
          t("computers.deleteConfirm", { name: computer.name, dn: computer.distinguishedName }),
        run: () => api.computers.remove(computer.id),
        success: t("computers.deleted"),
        failure: t("common.deleteFailed"),
      });
      if (deleted) reload();
    },
    [reload]
  );

  const columns = useMemo(
    () =>
      computerColumns({
        canManage,
        onEdit: (computer) => open("form", computer),
        onDelete: (computer) => void removeComputer(computer),
      }),
    [canManage, open, removeComputer]
  );

  const parentOptions = useMemo(() => parentDnOptions(list.items), [list.items]);

  return (
    <DirectoryView
      title={t("nav.computers")}
      hint={t("computers.hint")}
      createLabel={t("computers.create")}
      onCreate={() => open("form")}
      canManage={canManage}
      query={list.query}
      onQueryChange={list.setQuery}
      searchPlaceholder={t("computers.searchPlaceholder")}
      status={list.status}
      error={list.error}
      items={list.items}
      columns={columns}
      emptyText={t("computers.empty")}
      incompleteNoun={t("computers.noun")}
      isComplete={list.isComplete}
      hasMore={list.hasMore}
      isLoadingMore={list.isLoadingMore}
      onLoadMore={list.loadMore}
      dialog={
        dialog !== null && (
          <ComputerFormDialogContainer
            key={dialog.item?.id ?? "new"}
            computer={dialog.item}
            parentOptions={parentOptions}
            onSaved={reload}
            onClose={close}
          />
        )
      }
    />
  );
}
