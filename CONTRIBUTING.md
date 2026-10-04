# Contributing

Thanks for looking. This is an early project and the useful contributions are mostly small ones:
a default that should be safer, a failure that reports itself badly, a translation that reads
wrong to someone who speaks the language.

By opening a pull request you agree that your contribution is licensed under the
[Apache License 2.0](LICENSE), the same as the rest of the repository. There is no CLA.

## Getting it running

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download) and
[Node 20 or newer](https://nodejs.org). You also need a directory to point at — an LDAP server or
an Active Directory you can reach. The compose files in the repository are not currently in a
working state, so this part is on you; see [Not in scope yet](README.md#not-in-scope-yet).

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
- **Behaviour changes come with a test that fails without them.** 545 tests exist mostly because
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

## Being civil

Disagree about the code as much as you like. Be decent to the person. Maintainers will edit or
remove anything that makes this an unpleasant place to be, and will say so when they do.
