import { useCallback, useMemo } from "react";
import { t } from "../lib/i18n";
import { api } from "../api";
import type { ProviderDto, RoleMappingDto } from "../api/dto";
import { confirmAndRun } from "../lib/actions";
import { useCollection } from "../hooks/useCollection";
import { useDialog } from "../hooks/useDialog";
import { CollectionView } from "../components/collection/CollectionView";
import { roleMappingColumns } from "../components/roleMappings/roleMappingColumns";
import { RoleMappingFormDialogContainer } from "./RoleMappingFormDialogContainer";

const loadMappings = (): Promise<RoleMappingDto[]> => api.roleMappings.getAll();
const loadProviders = (): Promise<ProviderDto[]> => api.providers.getAll();

export function RoleMappingsPage(): JSX.Element {
  const mappings = useCollection<RoleMappingDto>({
    load: loadMappings,
    errorFallback: t("roleMappings.loadFailed"),
  });
  // The provider list is only needed for a label and a dropdown. If it fails to load the screen
  // keeps working: the mappings do not depend on it, and a provider shows as its id.
  const providers = useCollection<ProviderDto>({ load: loadProviders });
  const { dialog, open, close } = useDialog<"form", RoleMappingDto>();
  const { reload } = mappings;

  const removeMapping = useCallback(
    async (mapping: RoleMappingDto): Promise<void> => {
      const deleted = await confirmAndRun({
        question: t("roleMappings.deleteConfirm", { group: mapping.groupName, role: mapping.roleName }),
        run: () => api.roleMappings.remove(mapping.id),
        success: t("roleMappings.deleted"),
        failure: t("common.deleteFailed"),
      });
      if (deleted) reload();
    },
    [reload]
  );

  const columns = useMemo(
    () =>
      roleMappingColumns({
        providers: providers.items,
        onEdit: (mapping) => open("form", mapping),
        onDelete: (mapping) => void removeMapping(mapping),
      }),
    [open, providers.items, removeMapping]
  );

  // The order in the table is the order the rules are applied in, not the order the API answered in.
  const sorted = useMemo(
    () => mappings.items.slice().sort((a, b) => a.priority - b.priority),
    [mappings.items]
  );

  return (
    <CollectionView
      title={t("nav.roleMappings")}
      hint={t("roleMappings.hint")}
      createLabel={t("roleMappings.create")}
      onCreate={() => open("form")}
      status={mappings.status}
      error={mappings.error}
      items={sorted}
      columns={columns}
      emptyText={t("roleMappings.empty")}
      dialog={
        dialog !== null && (
          <RoleMappingFormDialogContainer
            key={dialog.item?.id ?? "new"}
            mapping={dialog.item}
            providers={providers.items}
            onSaved={reload}
            onClose={close}
          />
        )
      }
    />
  );
}
