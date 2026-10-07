import type { ChangeEvent } from "react";
import { isPermission } from "../../api/dto";
import { t } from "../../lib/i18n";
import type { ListStatus } from "../../hooks/usePagedList";
import { PageHeader } from "../PageHeader";
import { Card, ErrorBanner, InfoBanner, Spinner } from "../primitives";

/** One role as the matrix draws it: what it holds now, in the draft, and whether it may change. */
export interface RoleColumn {
  readonly name: string;
  readonly granted: ReadonlySet<string>;
  readonly isEditable: boolean;
}

export interface RolesViewProps {
  status: ListStatus;
  error: string | null;
  permissions: readonly string[];
  roles: readonly RoleColumn[];
  /** Whether a cell may be toggled — false for Admin, for roles.manage, and for anything the caller lacks. */
  canToggle: (role: string, permission: string) => boolean;
  dirtyRoles: readonly string[];
  isSaving: boolean;
  saveError: string | null;
  onToggle: (role: string, permission: string, granted: boolean) => void;
  onSave: () => void;
  onDiscard: () => void;
}

/** The catalogue description, or nothing for a permission this console does not know yet. */
function describe(permission: string): string {
  return isPermission(permission) ? t(`permission.${permission}`) : "";
}

/**
 * The permission × role matrix. A grid rather than a form per role, because the question an
 * administrator brings here is usually about a permission — "who can delete accounts?" — and a
 * row answers it at a glance.
 */
export function RolesView({
  status,
  error,
  permissions,
  roles,
  canToggle,
  dirtyRoles,
  isSaving,
  saveError,
  onToggle,
  onSave,
  onDiscard,
}: RolesViewProps): JSX.Element {
  const isDirty = dirtyRoles.length > 0;

  return (
    <div className="page">
      <PageHeader title={t("nav.roles")} hint={t("roles.hint")}>
        {isDirty && (
          <>
            <button className="btn" onClick={onDiscard} disabled={isSaving}>
              {t("roles.discard")}
            </button>
            <button className="btn btn--primary" onClick={onSave} disabled={isSaving}>
              {isSaving ? t("common.saving") : t("roles.save")}
            </button>
          </>
        )}
      </PageHeader>

      {status === "loading" && <Spinner />}
      {status === "error" && <ErrorBanner>{error}</ErrorBanner>}
      {status === "ready" && (
        <div style={{ display: "flex", flexDirection: "column", gap: "16px" }}>
          {saveError !== null && <ErrorBanner>{saveError}</ErrorBanner>}
          {isDirty && <InfoBanner>{t("roles.unsaved", { roles: dirtyRoles.join(", ") })}</InfoBanner>}

          <Card>
            <div className="table-wrap">
              <table className="data-table">
                <thead>
                  <tr>
                    <th>{t("roles.permissionColumn")}</th>
                    {roles.map((role) => (
                      <th key={role.name} style={{ textAlign: "center" }}>
                        {role.name}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {permissions.map((permission) => (
                    <tr key={permission}>
                      <td>
                        <div className="cell-mono">{permission}</div>
                        <div className="text-muted" style={{ fontSize: "12.5px" }}>
                          {describe(permission)}
                        </div>
                      </td>
                      {roles.map((role) => (
                        <td key={role.name} style={{ textAlign: "center" }}>
                          <input
                            type="checkbox"
                            aria-label={`${role.name}: ${permission}`}
                            checked={role.granted.has(permission)}
                            disabled={isSaving || !canToggle(role.name, permission)}
                            onChange={(event: ChangeEvent<HTMLInputElement>) =>
                              onToggle(role.name, permission, event.target.checked)
                            }
                          />
                        </td>
                      ))}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </Card>

          <div className="text-muted" style={{ fontSize: "12.5px", display: "grid", gap: "4px" }}>
            <div>{t("roles.adminNote")}</div>
            <div>{t("roles.rolesManageNote")}</div>
            <div>{t("roles.userNote")}</div>
          </div>
        </div>
      )}
    </div>
  );
}
