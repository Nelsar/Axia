import type { GroupDto } from "../../api/dto";
import { t } from "../../lib/i18n";
import { Modal } from "../Modal";
import { FormHint } from "../primitives";

export interface GroupMembersDialogProps {
  group: GroupDto;
  onClose: () => void;
}

export function GroupMembersDialog({ group, onClose }: GroupMembersDialogProps): JSX.Element {
  return (
    <Modal title={t("groups.membersDialogTitle", { name: group.name })} onClose={onClose}>
      <Modal.Section style={{ marginBottom: "12px" }}>
        <FormHint>
          {t("groups.membersDialogHint")}
        </FormHint>
      </Modal.Section>
      <ul className="dn-list">
        {group.members.map((dn) => (
          <li key={dn} className="cell-mono">
            {dn}
          </li>
        ))}
      </ul>
      <Modal.Actions>
        <button type="button" className="btn" onClick={onClose}>{t("common.close")}</button>
      </Modal.Actions>
    </Modal>
  );
}
