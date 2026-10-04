import type { ReactNode } from "react";

export interface PageHeaderProps {
  title: string;
  hint?: ReactNode;
  /** The actions slot on the right: a create button, or nothing. */
  children?: ReactNode;
}

export function PageHeader({ title, hint, children }: PageHeaderProps): JSX.Element {
  return (
    <div className="page__header">
      <div>
        <h2 className="page__title">{title}</h2>
        {hint !== undefined && hint !== null && hint !== false && <div className="page__hint">{hint}</div>}
      </div>
      {children !== undefined && children !== false && <div className="topbar__actions">{children}</div>}
    </div>
  );
}
