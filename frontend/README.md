# Nelsar.Axia console

The web console for [Nelsar.Axia](../README.md) — React, TypeScript, built with Vite. It talks to
`Nelsar.Axia.Api` over HTTP and holds no state of its own beyond the session.

Types are strict by configuration — `strict`, `noUncheckedIndexedAccess`,
`exactOptionalPropertyTypes`, `noUnusedLocals/Parameters` (see `tsconfig.json`) — and there is no
`any` in the source. Response and request shapes live in `src/api/dto.ts`, hand-translated from the
C# types in `Nelsar.Axia.Contracts` rather than guessed from observed JSON.

## Running it

```bash
npm ci
npm run dev          # Vite on http://localhost:5500
```

Other scripts: `npm run typecheck` (`tsc --noEmit`), `npm run build` (typecheck, then a production
build into `dist/`), `npm run preview` (serve what `build` produced).

On the sign-in screen, expand **API address** and give it the base URL of `Nelsar.Axia.Api`. The
default is `http://localhost:5063`, the `http` profile's port in
`Nelsar.Axia.Api/Properties/launchSettings.json`.

The API admits a browser on any loopback port by default, so 5500 needs no configuration on that
side; a console served from anywhere else has to be named in `Cors:AllowedOrigins`. See
[Security defaults](../README.md#security-defaults).

## Screens

- **Dashboard** — real counts (users, providers, role mappings) and the most recent audit events.
  Widgets the signed-in role or the API cannot supply are labelled as unavailable rather than
  filled with plausible numbers.
- **Users** — search and list, create, edit, delete, bulk operations (synchronous, and
  asynchronous with status polling), and the login generator: pattern or regular expression, with
  transliteration and a conflict check against the directory.
- **Organizational units** — search by name, DN or description; create, rename, move, delete. The
  `id` is a hash of the entry's DN, so it changes when an entry is renamed or moved, and the
  screen always works from identifiers in a freshly fetched list. Deleting a non-empty unit is
  refused by the directory with a 409, which the screen shows rather than deleting the contents
  recursively. A truncated enumeration is marked as incomplete instead of being presented as the
  whole list.
- **Groups** — the same CRUD over `/api/groups`, plus membership. Members are entry DNs (the
  `member` attribute) and saving replaces the membership wholesale; an empty membership cannot be
  submitted, because the `groupOfNames` schema requires at least one. Against a Windows
  `ActiveDirectory` provider the API answers 501 — it does not manage groups there — and the
  screen shows that answer rather than an empty list.
- **LDAP providers, role mappings, rate limiting** — CRUD over `/api/v1/providers`,
  `/api/v1/role-mappings` and `/api/v1/rate-limit-settings`.
- **Audit log** — paged `/api/v1/audit-events`, for administrators.

## Structure

Logic lives in hooks, markup in presentational components, and the wiring between them in
containers. A component that renders takes its data through props and fetches nothing.

```
frontend/
  index.html            Vite entry point (<script src="/src/main.tsx">)
  css/styles.css        the design system: CSS custom properties, light and dark theme
  src/
    main.tsx            shell (sidebar, header), routes, role-based menu filtering, mount
    api/
      dto.ts              response and request shapes, translated from Nelsar.Axia.Contracts
      client.ts           fetch plus ApiError; the only place a response is cast to its type
      index.ts            one typed method per controller
    lib/                  auth, config, router, ui (toast/confirm), errors, actions, i18n
    hooks/                all the logic: requests, state, validation, task polling
      usePagedList.ts       <T>: debounced search with paging (users, groups, OUs, computers, audit)
      useCollection.ts      <T>: a whole collection at once (providers, mappings, rate limits)
      useEntityForm.ts      <TValues>: field values, validation, submission, API error
      use*.ts               domain hooks built on those three
    components/           presentational; data arrives through props
      Tabs.tsx              compound tabs (Tabs.List / Tab / Panel, two variants)
      Modal.tsx             compound modal (Modal.Section / Modal.Actions)
      DataTable.tsx         <T>: a table described by an array of column definitions
      directory/, collection/   the shared shape of directory and reference-data screens
      <screen>/             the columns and forms of one screen
    containers/           hooks joined to components, no markup of their own
```

Text comes from `src/lib/i18n/`: `messages.en.ts` defines the keys and every other language is
typed against it, so a missing translation fails `tsc`. Right-to-left languages are handled with
CSS logical properties — write `margin-inline-start`, not `margin-left`.

## Known limitations

- **The role only hides things.** `UserRole` (`Admin`, `UserCreator`, `UserEditor`, `UserDeleter`,
  `User`) comes from the sign-in response and filters the menu and the buttons. It is not a
  security control: the API resolves permissions per request from directory group membership and
  the role mappings, and refuses on its own. Hiding a button changes what is convenient, not what
  is permitted.
- **Organizational units are a flat list**, not a tree — name, DN and parent DN. The parent DN
  field is free text with suggestions, because a unit can legitimately be created in a container
  that is not in the list (`CN=Users`, or the base DN itself).
- **Response shapes are checked by the compiler only.** `request<T>` in `src/api/client.ts`
  asserts the type rather than proving it. If the API diverges from `dto.ts`, the mismatch
  surfaces at runtime; runtime validation can be added at exactly that one place if it becomes
  worth it.
- **A session cannot be refreshed or ended.** There is no refresh token and no sign-out on the API,
  so when the JWT expires (`Jwt:ExpiresInMinutes`, 60 by default) signing in again is the only
  option. See [SECURITY.md](../SECURITY.md#known-limitations).
