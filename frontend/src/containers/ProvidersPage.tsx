import { useCallback, useMemo } from "react";
import { t } from "../lib/i18n";
import { api } from "../api";
import type { ProviderDto } from "../api/dto";
import { confirmAndRun } from "../lib/actions";
import { useCollection } from "../hooks/useCollection";
import { useDialog } from "../hooks/useDialog";
import { CollectionView } from "../components/collection/CollectionView";
import { providerColumns } from "../components/providers/providerColumns";
import { ProviderFormDialogContainer } from "./ProviderFormDialogContainer";

const loadProviders = (): Promise<ProviderDto[]> => api.providers.getAll();

export function ProvidersPage(): JSX.Element {
  const collection = useCollection<ProviderDto>({
    load: loadProviders,
    errorFallback: t("providers.loadFailed"),
  });
  const { dialog, open, close } = useDialog<"form", ProviderDto>();
  const { reload } = collection;

  const removeProvider = useCallback(
    async (provider: ProviderDto): Promise<void> => {
      const deleted = await confirmAndRun({
        question: t("providers.deleteConfirm", { name: provider.name }),
        run: () => api.providers.remove(provider.id),
        success: t("providers.deleted"),
        failure: t("common.deleteFailed"),
      });
      if (deleted) reload();
    },
    [reload]
  );

  const columns = useMemo(
    () =>
      providerColumns({
        onEdit: (provider) => open("form", provider),
        onDelete: (provider) => void removeProvider(provider),
      }),
    [open, removeProvider]
  );

  return (
    <CollectionView
      title={t("nav.providers")}
      hint={t("providers.hint")}
      createLabel={t("providers.create")}
      onCreate={() => open("form")}
      status={collection.status}
      error={collection.error}
      items={collection.items}
      columns={columns}
      emptyText={t("providers.empty")}
      dialog={
        dialog !== null && (
          <ProviderFormDialogContainer
            key={dialog.item?.id ?? "new"}
            provider={dialog.item}
            onSaved={reload}
            onClose={close}
          />
        )
      }
    />
  );
}
