## What and why

<!-- What changes and why it should. The diff says what; the why is what a reviewer needs from you.
     If a default changed, say what the old one allowed. See CONTRIBUTING.md. -->

## How it was tested

<!-- The tests added, and anything checked by hand: which directory, which steps. -->

## Checklist

- [ ] `dotnet build Nelsar.Axia.sln -warnaserror && dotnet test` passes
- [ ] `cd frontend && npm run build` passes, if the console changed
- [ ] A behaviour change comes with a test that fails without it
- [ ] User-facing text comes from the resources (`.resx`, `frontend/src/lib/i18n/`), not from code
- [ ] `README.md`, `docs/` and `CHANGELOG.md` say what users will notice
- [ ] Nothing identifies a real directory: no host names, domains, logins or passwords
- [ ] If it touches security: which property in `SECURITY.md` it holds up, and which test asserts it

<!-- Found something exploitable? Please report it privately first — see SECURITY.md. -->
