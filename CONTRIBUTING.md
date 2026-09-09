# Contributing

A .NET 10 SDK for the [Massive](https://massive.com) market data platform, covering the REST API,
WebSocket streams, and S3 flat files. Contributions are welcome, and this is a `0.x` release
specifically so the public API can still be argued with — an awkward signature is far cheaper to
change now than after 1.0.

**This file is a signpost, not the rules.** The conventions live in [`CLAUDE.md`](CLAUDE.md) and
there is deliberately only one copy of them.

## Where to go

| | |
|---|---|
| A bug, or an endpoint you want covered | [Open an issue](https://github.com/jerbersoft/massivedotnet/issues/new/choose) — there is a template for each |
| The API is awkward to use | An issue, and please do. That is what `0.x` is *for* |
| A question about the market data itself | [Massive's own documentation](https://massive.com/docs). This repository has no control over the service |
| A security vulnerability | [Privately](https://github.com/jerbersoft/massivedotnet/security/advisories/new), **never** a public issue. See [`SECURITY.md`](SECURITY.md) |
| Someone's behaviour | [`CODE_OF_CONDUCT.md`](CODE_OF_CONDUCT.md) names the channels, including one that reaches GitHub rather than anyone here. Not an issue either — a report about a person should not be the first thing a stranger reads |

## The one rule worth knowing before you start

**An issue exists before the work does.** Every change here begins with one — features, bugs,
chores, documentation — and commits reference it (`Fixes #12`, `Refs #12`). Issues carry one `type:`
label and at least one `area:` label, and open with a BLUF: one or two sentences at the top saying
what ships and why.

This is stated here because it is the rule that wastes your time if you meet it late. A finished
pull request with no issue behind it has been done in the wrong order, and unpicking that afterwards
is worse for you than for anyone else. Open the issue first — it can be two lines, and it is where
the "should this exist at all" conversation happens while it is still cheap.

## The second rule, which is specific to this repository

**Endpoints are generated, not written.** All 147 REST operations come from `specs/openapi.json`
plus a curated `specs/endpoints.map.json`, and the generated output is committed. So:

- Files ending `.g.cs` are never hand-edited. Hand-written members go in the matching `partial`.
  CI regenerates on every push and fails on any diff.
- To add or change an endpoint you edit the **map**, not the C#. `CLAUDE.md` has the sequence.
- The generator must be deterministic — same inputs, byte-identical output. CI checks that too.

A pull request that edits generated code directly cannot be merged, and the check that says so runs
before a human looks at it.

## Building it

```sh
dotnet build MassiveDotNet.slnx
dotnet test MassiveDotNet.slnx --filter "Category!=Integration"
```

The .NET 10 SDK, and nothing else — no local service, no code generation step during the build, no
credentials.

That filter is the whole story on credentials. Tests that call the live Massive API are committed
and run **locally**; CI excludes them by category, holds no API key, and a CI job asserts that no
workflow references one. Running `dotnet test` without a key skips them with a message naming the
variable to set. Nothing calls the service, and nothing spends your quota, unless you ask it to.

Before opening a pull request:

```sh
dotnet build MassiveDotNet.slnx                                    # must be warning-free
dotnet test MassiveDotNet.slnx
dotnet run --project tools/MassiveDotNet.CodeGen && git diff --exit-code src/  # must be clean
dotnet publish samples/MassiveDotNet.AotSmokeTest -r <rid> -c Release          # zero IL warnings
```

## Why the contributor guide is written for an AI assistant

Because that is who does most of the work in this repository, and pretending otherwise would produce
two guides that disagree. `CLAUDE.md` is the operating guide, its conventions are the project's
conventions regardless of who is reading, and a human contributor loses nothing by reading a
document addressed to someone else.

## Where everything is written down

Each fact lives in exactly one place. Please keep it that way.

- [`CLAUDE.md`](CLAUDE.md) — the constitution. Thirteen non-negotiable rules, each naming how it is
  enforced, and every architecture decision with the reasoning that produced it. If you are about to
  argue with one of those decisions, the "why" column is the argument you need to defeat.
- [`specs/`](specs) — the vendored OpenAPI description and the curated endpoint map.
- [`docs/`](docs) — performance figures, reconciliation notes, and design specs.

The API reference is generated from the XML documentation comments, which `dotnet pack` ships inside
each package — so it reaches IntelliSense at the call site from the same source that cannot drift.

**Documentation lives in the repository, not in a wiki**, so that a behaviour change and the page
describing it land in the same pull request.
