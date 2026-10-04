import { useEffect } from "react";
import { t } from "../lib/i18n";
import { createPortal } from "react-dom";
import type { CSSProperties, MouseEvent, ReactNode } from "react";

/**
 * The modal as a compound component: <Modal> is responsible only for the overlay, the portal,
 * Esc and clicking outside; the content is assembled by the caller from <Modal.Section> and
 * <Modal.Actions>.
 */
export interface ModalProps {
  title: string;
  width?: string | undefined;
  onClose: () => void;
  children: ReactNode;
}

function ModalRoot({ title, width = "480px", onClose, children }: ModalProps): JSX.Element {
  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent): void => {
      if (event.key === "Escape") onClose();
    };
    document.addEventListener("keydown", onKeyDown);
    return () => document.removeEventListener("keydown", onKeyDown);
  }, [onClose]);

  // currentTarget is typed as HTMLDivElement, so comparing it with target (an EventTarget) needs
  // an explicit widening — otherwise TypeScript refuses to compare the two types.
  const onOverlayClick = (event: MouseEvent<HTMLDivElement>): void => {
    if (event.target === event.currentTarget) onClose();
  };

  return createPortal(
    <div className="modal-overlay" onClick={onOverlayClick}>
      <div className="modal" style={{ width }} role="dialog" aria-modal="true" aria-label={title}>
        <div className="modal__header">
          <div className="modal__title">{title}</div>
          <button className="modal__close" type="button" aria-label={t("common.close")} onClick={onClose}>
            ✕
          </button>
        </div>
        <div className="modal__body">{children}</div>
      </div>
    </div>,
    document.body
  );
}

export interface ModalSectionProps {
  style?: CSSProperties | undefined;
  children: ReactNode;
}

function ModalSection({ style, children }: ModalSectionProps): JSX.Element {
  return <div style={{ marginBottom: "18px", ...style }}>{children}</div>;
}

export interface ModalActionsProps {
  align?: "start" | "end" | undefined;
  children: ReactNode;
}

function ModalActions({ align = "end", children }: ModalActionsProps): JSX.Element {
  return (
    <div className="form-actions" style={align === "start" ? { justifyContent: "flex-start" } : undefined}>
      {children}
    </div>
  );
}

export const Modal = Object.assign(ModalRoot, { Section: ModalSection, Actions: ModalActions });
