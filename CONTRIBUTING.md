# Contributing

Thanks for looking. This is an early project and the useful contributions are mostly small ones:
a default that should be safer, a failure that reports itself badly, a translation that reads
wrong to someone who speaks the language.

By opening a pull request you agree that your contribution is licensed under the
[Apache License 2.0](LICENSE), the same as the rest of the repository. There is no CLA.

## Getting it running

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download) and
[Node 22](https://nodejs.org) (or 20.19 and later, which Vite 8 still accepts). The unit and integration tests need nothing else.

To see a change working end to end, the [Docker quick start](README.md#quick-start) is the
shortest way: `docker compose up --build` builds the API and the console from your working tree
(without `--build` it runs the released images)
and starts them against an OpenLDAP with demo accounts, one for each role. Its directory is
reachable only inside Docker, so to run the API from source, point it at an LDAP server or an
Active Directory you can reach, or at the [local test directory](docs/local-directory.md) in
`deploy/dev`: an OpenLDAP on `127.0.0.1:389` with an AD-shaped domain of ~1,500 accounts and
~2,000 computers, and the PostgreSQL on `127.0.0.1:5432` that the Development example
configuration uses. The full documentation is in [`docs/`](docs/README.md).

```bash
cp Nelsar.Axia.Api/appsettings.json.example Nelsar.Axia.Api/appsettings.json
dotnet test
cd frontend && npm ci && npm run build
```

`appsettings.json` is git-ignored on purpose, and so is every other `appsettings*.json`. Only the
`.example` files are tracked. Nothing that identifies a real directory — a host name, a domain, a
login, a password — belongs in a commit, in test data, or in a code comment. It is far easier to
keep out than to take out: the history here was restarted once already because an internal domain
name reached a commit.

## What CI will check

Everything in [`.github/workflows/ci.yml`](.github/workflows/ci.yml) is runnable locally, and
running it first is faster than waiting for the answer:

```bash
dotnet build Nelsar.Axia.sln -warnaserror && dotnet test
```

```bash
cd frontend && npm run build
```

The build is warning-free and CI treats a new warning as an error. This is not tidiness: the three
`CA1416` warnings this repository carried for a while were saying that a Windows-only provider was
being constructed on Linux, and they were read as noise because they had company.

CI also fails on a NuGet or npm dependency with a known high-severity advisory.

## Conventions worth knowing before you write

Most of these are enforced by a test, which is the honest way to state a convention. The test
named is the one that will tell you.

- **No user-facing text in code.** API messages come from the `.resx` resources
  (`ApplicationMessages`, `ApiMessages`, `DomainMessages`); console text comes from
  `frontend/src/lib/i18n/`. English is the source: `messages.en.ts` defines the keys, every other
  language is typed against it, and a missing key fails `tsc`. On the API side,
  `ResourceMessageTests` checks that every accessor resolves, that the satellite assemblies load,
  and that a translation keeps its placeholders. Adding a language is one dictionary file and
  three `.resx` files.
- **No logic that depends on text.** Identity is an id or an enum, never a label — a label is
  translated and the comparison then fails in Hebrew. This one has no test; it has a history, which
  is why it is written down.
- **Comments and commit messages in English**, whatever language the discussion happens in.
- **Handlers, validators and request types stay internal to the Application layer.**
  `LayerBoundaryTests` fails if one becomes reachable, because a public handler can be constructed
  and called directly, skipping the MediatR pipeline and with it validation and the permission
  check.
- **A guarded use case needs an authorisation test.** `UseCaseAuthorizationTests` covers the
  pattern — refused when nobody is signed in, refused without the permission, and a refusal that
  does not name the permission it wanted.
- **Secrets stay out of the log.** `SensitivePropertyRedactor` redacts properties whose names read
  like a secret, including a bare `key`. If you add a log property that carries one, the name
  should trip that list rather than be an exception to it.
- **A change to the model is a migration for every database.** PostgreSQL and SQLite each keep
  their own; `EachDatabasesMigrations_DescribeTheModel` fails while either is behind. The commands
  are in [Development → Database migrations](docs/development.md#database-migrations).
- **Behaviour changes come with a test that fails without them.** The tests exist mostly because
  things that fail quietly — authorisation, directory isolation, what reaches a sink, translation
  completeness — are the things worth pinning. A useful habit: break your own fix on purpose and
  check that the test notices.

## Pull requests

- One subject per pull request. A refactor and a fix in one branch are hard to review and harder
  to revert.
- Say why in the commit message, not what — the diff says what. If a default changed, say what the
  old one allowed. If a failure mode is now reported differently, say how it used to surface.
- Security-relevant changes: say which of the properties in [SECURITY.md](SECURITY.md) you think
  you are holding up, and whether a test now asserts it.
- Found something exploitable? Don't open a pull request first — see
  [reporting a vulnerability](SECURITY.md#reporting-a-vulnerability).

## Translations

Corrections are especially welcome. Everything except English and Russian is machine-quality and
has had no native review, so a translation that reads oddly, or politely-wrong, is a real bug.
Changing wording needs no discussion; changing a key does, because the key is the contract between
the two sides.

Right-to-left (Arabic, Hebrew) is handled with CSS logical properties — `margin-inline-start`,
`inset-inline-end`, `text-align: start`. If you write a physical `left` or `right` in a stylesheet,
it will look correct to you and wrong to half the supported languages.

## Documentation

`docs/` is published to <https://nelsar.github.io/Axia/> by GitHub Pages on every push to `main`.
It stays plain Markdown that reads the same on GitHub: a page's title is its first heading, and its
place in the navigation and the description search engines show are set under `defaults` in
`docs/_config.yml` — a new page needs an entry there. Link to anything outside `docs/` by its full
GitHub URL: a relative `../` link works on GitHub and breaks on the site. Jekyll reads `{{` and
`{%` as template syntax even inside code blocks, so a sample containing them breaks the build.

## Being civil

Disagree about the code as much as you like. Be decent to the person. Maintainers will edit or
remove anything that makes this an unpleasant place to be, and will say so when they do. The
[code of conduct](CODE_OF_CONDUCT.md) says the same at more length, and where to report it.

## Releasing

For maintainers. Versions follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html), and
the version is written in three places: `Directory.Build.props`, which every .NET project
inherits, `frontend/package.json`, and the image tags the quick start runs in `docker-compose.yml`.

1. In `CHANGELOG.md`, give the section being released its version and date —
   `## [x.y.z] - YYYY-MM-DD` — and add its link at the bottom.
2. Set the same version in `Directory.Build.props`, in the console with
   `npm version x.y.z --no-git-tag-version` in `frontend/`, which updates the lockfile too, and as
   the `AXIA_VERSION` default of both images in `docker-compose.yml`. Until step 4 has published
   them, `docker compose up` on `main` builds from source instead.

   The compose default moves earlier when a change on `main` makes `docker-compose.yml` something
   the published images cannot run — the move to PostgreSQL did. That pull request sets it to the
   coming version, so `docker compose up` builds from source until the release instead of pulling
   images that fail.
3. Merge that as a pull request, with CI green.
4. Tag the merge commit on `main` and push the tag:

   ```bash
   git tag -a vx.y.z -m "Nelsar.Axia x.y.z"
   git push origin vx.y.z
   ```

   The tag starts the `Images` workflow, which builds `ghcr.io/nelsar/axia-api` and
   `ghcr.io/nelsar/axia-console` from it, for amd64 and arm64, and publishes them as `x.y.z`,
   `x.y` and `latest`, and pushes the same to Docker Hub as `nelsar/axia-api` and
   `nelsar/axia-console`, updating their overview there from `.github/dockerhub/README.md`. Wait
   for it to finish before announcing the release; should it fail, run it again from the Actions
   tab with the tag as its input.

   Docker Hub is published to when the repository's `DOCKERHUB_USERNAME` variable is set, with an
   access token of that account — read, write and delete scope — in the `DOCKERHUB_TOKEN` secret.
   Without the variable, the workflow publishes to ghcr.io alone.

5. Publish the release from the tag, with that version's changelog section as its notes:

   ```bash
   gh release create vx.y.z --verify-tag --title "Nelsar.Axia x.y.z" --notes-file notes.md
   ```

   Publishing it starts the `Installers` workflow, which builds the Windows installers from the
   tag, installs, upgrades and removes the x64 one on its runner, and attaches
   `Nelsar.Axia-x.y.z-x64.msi` and `Nelsar.Axia-x.y.z-arm64.msi` to the release a few minutes
   later, with an attestation of their build provenance. It then adds a *Windows installers*
   section to the release notes: the edit is also what makes SourceForge, which copied the release
   when it was published, copy the installers too. Should it fail, run it again from the Actions
   tab with the tag as its input; the section is added once.
