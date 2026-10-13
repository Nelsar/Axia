import type { DirectoryAttributeDto, UserDto } from "../../api/dto";
import { t } from "../../lib/i18n";
import type { UserAttributesState } from "../../hooks/useUserAttributes";
import type { UserDetailsStatus } from "../../hooks/useUserDetails";
import type { UserPermissions } from "../../hooks/usePermissions";
import { Modal } from "../Modal";
import { Badge, Card, ErrorBanner, Mono, Muted, Spinner } from "../primitives";
import { roleTone } from "./userFormatting";
import type { JSX } from "react";

export interface UserDetailDialogProps {
  user: UserDto;
  /** Whether the account's own read has answered: until it has, the groups are not known yet. */
  groupsStatus: UserDetailsStatus;
  groupsError: string | null;
  perms: UserPermissions;
  attributes: UserAttributesState;
  onLoadAttributes: () => void;
  onEdit: () => void;
  onDelete: () => void;
  onClose: () => void;
}

// The API used to substitute the login here when an entry had no mail, and the two were
// indistinguishable. An empty field is now an honest "the directory holds no mail", and that is
// worth saying outright rather than showing as blankness.
function EmailCell({ user }: { user: UserDto }): JSX.Element {
  if (user.email !== "") return <>{user.email}</>;
  return <Muted>{t("users.noMailAttribute")}</Muted>;
}

// An empty list means "membership was not reported", not "in no groups": in OpenLDAP memberOf is
// only populated when the memberof overlay is enabled. Passing one off as the other is not on.
// Nor is saying either before the account's own read has answered: the row the card opened
// from carries no groups at all.
function GroupsCell({
  user,
  status,
  error,
}: {
  user: UserDto;
  status: UserDetailsStatus;
  error: string | null;
}): JSX.Element {
  if (status === "loading") return <Spinner />;
  if (status === "error") return <Muted>{error}</Muted>;
  if (user.groups.length === 0) return <Muted>{t("users.noGroupsReported")}</Muted>;
  return (
    <>
      {user.groups.map((dn) => (
        <Mono key={dn} small>
          {dn}
        </Mono>
      ))}
    </>
  );
}

function AttributeValues({ attribute }: { attribute: DirectoryAttributeDto }): JSX.Element {
  // The attribute's name is shown, its value is not. Hiding the name too would leave an
  // administrator unable to tell a hidden attribute from an absent one.
  if (attribute.isRedacted) return <Muted>{t("users.valueRedacted")}</Muted>;
  if (attribute.values.length === 0) return <Muted>—</Muted>;

  return (
    <>
      {attribute.values.map((value, index) => (
        <Mono key={`${attribute.name}-${index}`} breakAll>
          {value}
          {attribute.isBinary && (
            // A binary value arrives as base64: it is not text, and showing it as text would
            // draw garbage that looks like data.
            <span className="text-muted" style={{ fontSize: "11.5px" }}>
              {" "}
              {t("users.binaryValue")}
            </span>
          )}
        </Mono>
      ))}
    </>
  );
}

function AttributesTable({ attributes }: { attributes: readonly DirectoryAttributeDto[] }): JSX.Element {
  if (attributes.length === 0) {
    return <div className="card__pad text-muted">{t("users.noAttributes")}</div>;
  }
  return (
    <div className="table-wrap">
      <table className="data-table">
        <thead>
          <tr>
            <th style={{ width: "40%" }}>{t("users.attribute")}</th>
            <th>{t("common.value")}</th>
          </tr>
        </thead>
        <tbody>
          {attributes.map((attribute) => (
            <tr key={attribute.name}>
              <td className="cell-mono">{attribute.name}</td>
              <td>
                <AttributeValues attribute={attribute} />
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

/** The user card: everything it shows arrives as props — including the attributes' state. */
export function UserDetailDialog({
  user,
  groupsStatus,
  groupsError,
  perms,
  attributes,
  onLoadAttributes,
  onEdit,
  onDelete,
  onClose,
}: UserDetailDialogProps): JSX.Element {
  return (
    <Modal title={user.name} width="560px" onClose={onClose}>
      <Modal.Section>
        <div className="kv-grid">
          <div className="k">Email</div>
          <div className="cell-mono">
            <EmailCell user={user} />
          </div>
          <div className="k">{t("login.login")}</div>
          <div className="cell-mono">{user.username || "—"}</div>
          <div className="k">{t("common.role")}</div>
          <div>
            <Badge tone={roleTone(user.role)}>{user.role}</Badge>
          </div>
          <div className="k">DN</div>
          <div className="cell-mono" style={{ fontSize: "11.5px" }}>
            {user.dn || "—"}
          </div>
          <div className="k">{t("nav.groups")}</div>
          <div>
            <GroupsCell user={user} status={groupsStatus} error={groupsError} />
          </div>
          <div className="k">Id</div>
          <div className="cell-mono" style={{ fontSize: "11.5px" }}>
            {user.id}
          </div>
        </div>
      </Modal.Section>

      <Modal.Section>
        <Card>
          <Card.Header>
            <span>{t("users.directoryAttributes")}</span>
            {attributes.status !== "ready" && (
              <button
                className="btn btn--sm"
                onClick={onLoadAttributes}
                disabled={attributes.status === "loading"}
              >{t("common.show")}</button>
            )}
          </Card.Header>
          {attributes.status === "loading" && <Spinner label={t("users.readingEntry")} />}
          {attributes.status === "error" && (
            <Card.Body>
              <ErrorBanner>{attributes.error}</ErrorBanner>
            </Card.Body>
          )}
          {attributes.status === "ready" && <AttributesTable attributes={attributes.attributes} />}
        </Card>
      </Modal.Section>

      <Modal.Actions align="start">
        {perms.canEdit && (
          <button className="btn" onClick={onEdit}>{t("common.edit")}</button>
        )}
        {perms.canDelete && (
          <button className="btn btn--danger" onClick={onDelete}>{t("common.delete")}</button>
        )}
      </Modal.Actions>
    </Modal>
  );
}
