import type { ChangeEvent, CSSProperties, JSX, ReactNode } from "react";
import { t } from "../lib/i18n";

/**
 * Small presentational primitives on top of the design system (css/styles.css).
 * A badge's tone is a union rather than a string: `tone="succes"` is now a compile error rather
 * than a quietly non-existent CSS class.
 */
export type BadgeTone = "neutral" | "accent" | "success" | "warning" | "danger";

export interface BadgeProps {
  tone?: BadgeTone | undefined;
  children: ReactNode;
}

export function Badge({ tone = "neutral", children }: BadgeProps): JSX.Element {
  return <span className={`badge badge--${tone}`}>{children}</span>;
}

export interface SpinnerProps {
  label?: string | undefined;
}

export function Spinner({ label = t("common.loading") }: SpinnerProps): JSX.Element {
  return (
    <div className="loading">
      <span className="spinner" />
      {label}
    </div>
  );
}

export interface ErrorBannerProps {
  children: ReactNode;
}

export function ErrorBanner({ children }: ErrorBannerProps): JSX.Element | null {
  if (children === null || children === undefined || children === false) return null;
  return <div className="alert alert--error">{children}</div>;
}

export interface InfoBannerProps {
  children: ReactNode;
}

export function InfoBanner({ children }: InfoBannerProps): JSX.Element | null {
  if (children === null || children === undefined || children === false) return null;
  return <div className="alert alert--info">{children}</div>;
}

export interface MutedProps {
  children: ReactNode;
}

export function Muted({ children }: MutedProps): JSX.Element {
  return <span className="text-muted">{children}</span>;
}

export interface MonoProps {
  small?: boolean | undefined;
  breakAll?: boolean | undefined;
  children: ReactNode;
}

export function Mono({ small = false, breakAll = false, children }: MonoProps): JSX.Element {
  const style: CSSProperties = {};
  if (small) style.fontSize = "11.5px";
  if (breakAll) style.wordBreak = "break-all";
  return (
    <div className="cell-mono" style={style}>
      {children}
    </div>
  );
}

export interface CardProps {
  className?: string | undefined;
  children: ReactNode;
}

export interface CardHeaderProps {
  children: ReactNode;
}

export interface CardBodyProps {
  className?: string | undefined;
  style?: CSSProperties | undefined;
  children: ReactNode;
}

function CardRoot({ className = "", children }: CardProps): JSX.Element {
  return <div className={`card ${className}`.trim()}>{children}</div>;
}

function CardHeader({ children }: CardHeaderProps): JSX.Element {
  return <div className="card__header">{children}</div>;
}

function CardBody({ className = "", style, children }: CardBodyProps): JSX.Element {
  return (
    <div className={`card__pad ${className}`.trim()} style={style}>
      {children}
    </div>
  );
}

/**
 * A compound component via Object.assign rather than a namespace or separate exports: that way
 * `Card.Body` is typed as an ordinary component and needs no hand-written interface of
 * "component-valued properties".
 */
export const Card = Object.assign(CardRoot, { Header: CardHeader, Body: CardBody });

export interface FormRowProps {
  label: ReactNode;
  hint?: ReactNode;
  error?: string | undefined;
  children: ReactNode;
}

export function FormRow({ label, hint, error, children }: FormRowProps): JSX.Element {
  return (
    <div className="form-row">
      <label className="form-label">{label}</label>
      {children}
      {hint !== undefined && hint !== null && (
        <div className="text-muted" style={{ fontSize: "12px" }}>
          {hint}
        </div>
      )}
      {error !== undefined && (
        <div className="text-danger" style={{ fontSize: "12.5px" }}>
          {error}
        </div>
      )}
    </div>
  );
}

export interface FormHintProps {
  children: ReactNode;
}

export function FormHint({ children }: FormHintProps): JSX.Element {
  return <div className="form-hint">{children}</div>;
}

export interface IncompleteNoticeProps {
  /** The noun as it reads inside the sentence: "the absence of a group from it proves nothing". */
  noun: string;
}

// "The directory was not read in full": the walk stopped early, and an entry missing from the
// list proves nothing. Every directory screen has to say so — and say it the same way.
export function IncompleteNotice({ noun }: IncompleteNoticeProps): JSX.Element {
  return (
    <div className="stub-notice" style={{ marginBottom: "16px" }}>
      <strong>{t("list.incompleteTitle")}</strong>
      <br />
      {t("list.incompleteBody", { noun })}
    </div>
  );
}

export interface LoadMoreProps {
  onClick: () => void;
  isLoading: boolean;
}

export function LoadMore({ onClick, isLoading }: LoadMoreProps): JSX.Element {
  return (
    <div style={{ padding: "14px 20px" }}>
      <button className="btn" onClick={onClick} disabled={isLoading}>
        {isLoading ? t("common.loadingData") : t("common.loadMore")}
      </button>
    </div>
  );
}

export interface SearchBoxProps {
  value: string;
  onChange: (value: string) => void;
  placeholder?: string | undefined;
}

export function SearchBox({ value, onChange, placeholder }: SearchBoxProps): JSX.Element {
  return (
    <div className="search-box">
      <span>🔍</span>
      <input
        type="search"
        value={value}
        placeholder={placeholder}
        onChange={(event: ChangeEvent<HTMLInputElement>) => onChange(event.target.value)}
      />
    </div>
  );
}
