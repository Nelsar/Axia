import { useCallback, useEffect, useMemo, useState, type JSX } from "react";
import { api } from "../api";
import { isPermission } from "../api/dto";
import type { RolePermissionsDto, RolesOverviewDto } from "../api/dto";
import { messageOf } from "../lib/errors";
import { t } from "../lib/i18n";
import { setPermissions } from "../lib/permissions";
import { toast } from "../lib/ui";
import type { ListStatus } from "../hooks/usePagedList";
import { usePermissionSet } from "../hooks/usePermissions";
import { RolesView } from "../components/roles/RolesView";
import type { RoleColumn } from "../components/roles/RolesView";

type Grants = ReadonlyMap<string, ReadonlySet<string>>;

function grantsOf(roles: readonly RolePermissionsDto[]): Grants {
  return new Map(roles.map((role) => [role.roleName, new Set(role.permissions)]));
}

function sameSet(left: ReadonlySet<string>, right: ReadonlySet<string>): boolean {
  return left.size === right.size && [...left].every((item) => right.has(item));
}

/**
 * The roles screen: what each role holds, edited as a draft and saved role by role.
 *
 * Only roles whose draft differs from what was loaded are sent, each as its complete set — the
 * API replaces a role's permissions rather than patching them. A save that fails part-way keeps
 * what did go through and leaves the rest in the draft, so nothing the administrator ticked is
 * silently lost and nothing already saved is shown as pending.
 */
export function RolesPage(): JSX.Element {
  const [status, setStatus] = useState<ListStatus>("loading");
  const [error, setError] = useState<string | null>(null);
  const [overview, setOverview] = useState<RolesOverviewDto | null>(null);
  const [draft, setDraft] = useState<Grants>(new Map());
  const [isSaving, setIsSaving] = useState<boolean>(false);
  const [saveError, setSaveError] = useState<string | null>(null);
  const held = usePermissionSet();

  useEffect(() => {
    let active = true;
    api.roles
      .getAll()
      .then((loaded) => {
        if (!active) return;
        setOverview(loaded);
        setStatus("ready");
      })
      .catch((reason: unknown) => {
        if (!active) return;
        setError(messageOf(reason, t("roles.loadFailed")));
        setStatus("error");
      });
    return () => {
      active = false;
    };
  }, []);

  const saved = useMemo<Grants>(() => grantsOf(overview?.roles ?? []), [overview]);

  const columns = useMemo<RoleColumn[]>(
    () =>
      (overview?.roles ?? []).map((role) => ({
        name: role.roleName,
        isEditable: role.isEditable,
        granted: draft.get(role.roleName) ?? saved.get(role.roleName) ?? new Set<string>(),
      })),
    [overview, draft, saved]
  );

  const dirtyRoles = useMemo<string[]>(
    () =>
      [...draft.entries()]
        .filter(([role, granted]) => !sameSet(granted, saved.get(role) ?? new Set<string>()))
        .map(([role]) => role),
    [draft, saved]
  );

  // Mirrors the API's rules, so the grid does not offer what the save would refuse: Admin is fixed,
  // roles.manage is never granted to another role (only taken away, should one somehow hold it),
  // and nobody grants what they do not hold — taking a permission away needs no such thing.
  const canToggle = useCallback(
    (roleName: string, permission: string): boolean => {
      const column = columns.find((role) => role.name === roleName);
      if (column === undefined || !column.isEditable) return false;

      const granted = column.granted.has(permission);
      if (permission === "roles.manage") return granted;
      return granted || (isPermission(permission) && held.has(permission));
    },
    [columns, held]
  );

  const onToggle = useCallback(
    (roleName: string, permission: string, granted: boolean): void => {
      setDraft((previous) => {
        const next = new Map(previous);
        const role = new Set(previous.get(roleName) ?? saved.get(roleName) ?? []);
        if (granted) role.add(permission);
        else role.delete(permission);
        next.set(roleName, role);
        return next;
      });
    },
    [saved]
  );

  const onDiscard = useCallback((): void => {
    setDraft(new Map());
    setSaveError(null);
  }, []);

  const onSave = useCallback(async (): Promise<void> => {
    if (overview === null) return;
    setIsSaving(true);
    setSaveError(null);

    const updated: RolePermissionsDto[] = [];
    try {
      for (const roleName of dirtyRoles) {
        const granted = draft.get(roleName) ?? new Set<string>();
        // In catalogue order, the way the API lists them back.
        const permissions = overview.permissions.filter((permission) => granted.has(permission));
        updated.push(await api.roles.updatePermissions(roleName, permissions));
      }
      toast(t("roles.saved"), { type: "success" });
    } catch (reason: unknown) {
      setSaveError(messageOf(reason, t("common.saveFailed")));
    } finally {
      if (updated.length > 0) {
        const byName = new Map(updated.map((role) => [role.roleName, role]));
        setOverview({ ...overview, roles: overview.roles.map((role) => byName.get(role.roleName) ?? role) });
        setDraft((previous) => {
          const next = new Map(previous);
          for (const role of updated) next.delete(role.roleName);
          return next;
        });

        // The administrator's own roles may be among those changed; the menu should know.
        void api.me
          .permissions()
          .then(setPermissions)
          .catch((): void => undefined);
      }
      setIsSaving(false);
    }
  }, [dirtyRoles, draft, overview]);

  return (
    <RolesView
      status={status}
      error={error}
      permissions={overview?.permissions ?? []}
      roles={columns}
      canToggle={canToggle}
      dirtyRoles={dirtyRoles}
      isSaving={isSaving}
      saveError={saveError}
      onToggle={onToggle}
      onSave={() => void onSave()}
      onDiscard={onDiscard}
    />
  );
}
