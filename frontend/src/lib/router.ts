/** A minimal hash router. Path parameters are always strings: a hash cannot yield anything else. */
export type RouteParams = Readonly<Record<string, string>>;

export type RouteHandler = (params: RouteParams) => void | Promise<void>;

interface Route {
  readonly pattern: string;
  readonly handler: RouteHandler;
}

const routes: Route[] = [];
let notFoundHandler: () => void | Promise<void> = () => {};

export function route(pattern: string, handler: RouteHandler): void {
  routes.push({ pattern, handler });
}

export function onNotFound(handler: () => void | Promise<void>): void {
  notFoundHandler = handler;
}

export function navigate(path: string): void {
  if (location.hash.slice(1) === path) {
    void resolve();
  } else {
    location.hash = path;
  }
}

export function currentQuery(): URLSearchParams {
  const hash = location.hash.slice(1) || "/";
  const index = hash.indexOf("?");
  return index === -1 ? new URLSearchParams() : new URLSearchParams(hash.slice(index + 1));
}

function matchPattern(pattern: string, path: string): RouteParams | null {
  const patternParts = pattern.split("/").filter(Boolean);
  const pathParts = path.split("/").filter(Boolean);
  if (patternParts.length !== pathParts.length) return null;

  const params: Record<string, string> = {};
  for (let i = 0; i < patternParts.length; i++) {
    const patternPart = patternParts[i];
    const pathPart = pathParts[i];
    // The lengths matched, so undefined is impossible here — but the compiler does not know that,
    // and the check costs less than loose indexing across the whole file.
    if (patternPart === undefined || pathPart === undefined) return null;

    if (patternPart.startsWith(":")) {
      params[patternPart.slice(1)] = decodeURIComponent(pathPart);
    } else if (patternPart !== pathPart) {
      return null;
    }
  }
  return params;
}

export async function resolve(): Promise<void> {
  const hash = location.hash.slice(1) || "/";
  const path = hash.split("?")[0] ?? "/";

  for (const entry of routes) {
    const params = matchPattern(entry.pattern, path);
    if (params !== null) {
      await entry.handler(params);
      return;
    }
  }
  await notFoundHandler();
}

export function startRouter(): void {
  window.addEventListener("hashchange", () => {
    void resolve();
  });
  void resolve();
}
