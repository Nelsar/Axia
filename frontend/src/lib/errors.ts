import { ApiError } from "../api/client";

/**
 * `unknown` rather than `any` or `Error`: a catch can receive anything, including a string thrown
 * by somebody else's library. An ApiError's message arrives from the backend already in the
 * console's own language (see the Accept-Language header in client.ts) and explains what to do, so
 * it is shown as it is. Everything else tells an administrator nothing, and is replaced by the
 * fallback.
 */
export function messageOf(error: unknown, fallback: string): string {
  return error instanceof ApiError ? error.message : fallback;
}
