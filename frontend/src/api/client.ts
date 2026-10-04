import { getApiBaseUrl } from "../lib/config";
import { getToken, logout } from "../lib/auth";
import { readLanguage, t as translate } from "../lib/i18n";

export class ApiError extends Error {
  readonly status: number;
  readonly details: unknown;

  constructor(message: string, status: number, details: unknown = null) {
    super(message);
    this.name = "ApiError";
    this.status = status;
    this.details = details;
  }
}

type QueryValue = string | number | boolean | null | undefined;

export function qs(params: Readonly<Record<string, QueryValue>>): string {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== null && value !== "") search.set(key, String(value));
  }
  const serialized = search.toString();
  return serialized ? `?${serialized}` : "";
}

type HttpMethod = "GET" | "POST" | "PUT" | "DELETE";

interface RequestOptions {
  method?: HttpMethod | undefined;
  /** The request body. unknown rather than any: we serialise what we are given, but nothing may read fields off it. */
  body?: unknown;
  auth?: boolean | undefined;
  /**
   * The directory for this request — the X-LDAP-Provider-Id header. An empty string and null
   * both mean "the default directory", which is to say the header is not sent at all.
   *
   * Exactly two calls need it: sign-in and registration. For everything else the directory is
   * named by the token itself — the backend binds a session to the directory it signed in to and
   * refuses a header naming a different one with 403. Not sending it after sign-in is not an
   * economy; it is the only way not to argue with the session.
   */
  providerId?: string | null | undefined;
}

/** An API error message arrives in an unknown shape — this digs it out without casting. */
function extractMessage(data: unknown, status: number): string {
  if (typeof data === "string" && data.length > 0) return data;
  if (typeof data === "object" && data !== null) {
    const record: Record<string, unknown> = data as Record<string, unknown>;
    if (typeof record["message"] === "string") return record["message"];
    if (typeof record["title"] === "string") return record["title"];
  }
  return translate("api.requestFailed", { status });
}

/** One network call: headers, 401, parsing the body. Returns the body parsed but unverified. */
async function send(
  path: string,
  { method = "GET", body, auth = true, providerId = null }: RequestOptions
): Promise<unknown> {
  const base = getApiBaseUrl();
  const headers: Record<string, string> = {
    "Content-Type": "application/json",
    // The interface language, not the browser's. The backend localises its own messages from this
    // header (see UseRequestLocalization and the .resx files), and were the two to diverge an
    // administrator would read an English interface with Russian errors inside it. A hard-coded
    // "ru" stood here precisely because there was nothing to choose between.
    "Accept-Language": readLanguage(),
  };

  if (auth) {
    const token = getToken();
    if (token !== null) headers["Authorization"] = `Bearer ${token}`;
  }

  if (providerId !== null && providerId !== undefined && providerId !== "") {
    headers["X-LDAP-Provider-Id"] = providerId;
  }

  // The field is assembled separately rather than written as `body: undefined`: under
  // exactOptionalPropertyTypes "the property is absent" and "the property is present and
  // undefined" are different things, and RequestInit.body accepts only the first.
  const init: RequestInit = { method, headers };
  if (body !== undefined) init.body = JSON.stringify(body);

  let response: Response;
  try {
    response = await fetch(`${base}${path}`, init);
  } catch {
    throw new ApiError(translate("api.unreachable", { base }), 0);
  }

  if (response.status === 401) {
    logout();
    if (!location.hash.startsWith("#/login")) location.hash = "#/login";
    throw new ApiError(translate("api.sessionExpired"), 401);
  }

  if (response.status === 204) return null;

  const text = await response.text();
  let data: unknown = null;
  if (text.length > 0) {
    try {
      data = JSON.parse(text) as unknown;
    } catch {
      data = text;
    }
  }

  if (!response.ok) throw new ApiError(extractMessage(data, response.status), response.status, data);
  return data;
}

/**
 * The trust boundary. TypeScript checks our code, not what the server actually sent: the `as T`
 * here is the single place where a claim about a response's shape is made without proof. The
 * shapes are written down in dto.ts from the backend contracts, and if they ever need checking at
 * run time, this one function changes rather than forty call sites.
 */
async function request<T>(path: string, options: RequestOptions = {}): Promise<T> {
  return (await send(path, options)) as T;
}

/** For 204 No Content: there is no response at all, and no reason to pretend otherwise. */
async function requestNoContent(path: string, options: RequestOptions = {}): Promise<void> {
  await send(path, options);
}

export { request, requestNoContent };
