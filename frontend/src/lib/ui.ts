/**
 * The imperative odds and ends that do not need React: a toast and a confirmation. Everything
 * else from the old ui.js (escapeHtml, openModal, badge, spinnerHtml, errorBannerHtml) went away
 * along with building markup out of strings — components do that now.
 */

export type ToastType = "info" | "success" | "error";

interface ToastOptions {
  type?: ToastType | undefined;
  timeout?: number | undefined;
}

let toastRoot: HTMLDivElement | null = null;

export function toast(message: string, { type = "info", timeout = 4500 }: ToastOptions = {}): void {
  if (toastRoot === null) {
    toastRoot = document.createElement("div");
    toastRoot.className = "toast-root";
    document.body.appendChild(toastRoot);
  }

  const element = document.createElement("div");
  element.className = `toast toast--${type}`;
  element.textContent = message;
  toastRoot.appendChild(element);

  requestAnimationFrame(() => element.classList.add("toast--visible"));
  setTimeout(() => {
    element.classList.remove("toast--visible");
    setTimeout(() => element.remove(), 200);
  }, timeout);
}

export function confirmDialog(message: string): Promise<boolean> {
  return Promise.resolve(window.confirm(message));
}

export function formatDateTime(value: string | null | undefined): string {
  if (value === null || value === undefined || value === "") return "—";
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return date.toLocaleString("ru-RU", {
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit",
  });
}
