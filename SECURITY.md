# Security policy

## Reporting a vulnerability

**[Report it privately through GitHub.](https://github.com/jerbersoft/massivedotnet/security/advisories/new)**
That opens a draft advisory visible only to you and the maintainer, and it is the right channel even
if you are not sure the thing you found is a vulnerability.

**Please do not open a public issue for a suspected vulnerability.** A public issue discloses the
flaw to everyone who consumes these packages before there is a version they can move to. If you have
already opened one, that is not a disaster — say so in a private report and it will be handled from
there.

There is no email address here on purpose. The private channel above is enabled, integrated with the
advisory and CVE workflow, and does not require publishing anyone's inbox on a public repository.

### What to include

- The affected version — a package version, or a commit SHA.
- What an attacker can do, and what they need in order to do it.
- A minimal reproduction if you have one. A malformed response body is often enough; the
  `StubHandler` in `tests/MassiveDotNet.Rest.Tests` is the shortest way to feed one to the client.

### Never include a real API key

The SDK is built so that an unmodified stack trace is safe to attach. The key travels as an
`Authorization: Bearer` header by default (D2), so no URL the SDK builds carries it; exception
messages never render it (rule 11); and the WebSocket authentication frame is assembled in a rented
buffer, sent, and wiped rather than kept anywhere it could be printed (D35).

If you believe you have exposed a key while testing, rotate it with Massive first, then report.

### What to expect

This is a single-maintainer project, so the honest answer is best effort rather than a service
level: an acknowledgement within about a week, and an assessment once the report has been
reproduced. If a report is confirmed, the fix, the advisory and the released version are published
together — an advisory naming a flaw with no version to upgrade to helps nobody.

You will be credited in the advisory unless you would rather not be. Say which.

## Supported versions

| Version | Supported |
|---|---|
| The tip of `master` | ✅ |
| Anything older | ❌ — fixes land on `master` and publish as a new version |

Pre-1.0 and single-maintainer, so there are no maintenance branches and no backports. This table
gains a "latest release" row when the first package reaches nuget.org.

## What is in scope

This is a client library. It holds a credential, opens connections, and parses bytes it did not
produce — which is where its real surface is:

- **Credential disclosure.** Any path that puts the API key into a log line, an exception message, a
  URL, a process argument list, a file, or a `ToString()`. Rule 11 is absolute, and it has been
  broken before by an indirect route worth knowing about: `MassiveAuthenticationHandler` mutates the
  request URI under the query-string scheme, so a retry handler composed *outside* it appended
  `apiKey=` once per attempt — the request still succeeded, so nothing surfaced while the key landed
  in the access log three times (D30). Handler ordering is a security control here, not a
  preference.
- **Following a server-chosen URL.** `next_url` is absolute, carries no key, and is chosen by the
  response body — so a client that follows it unconditionally sends the caller's credential to
  whatever host a server names. Traversal refuses a cursor whose origin does not match the
  configured `BaseAddress` (D14). A path that defeats that check, or that reaches a different host
  than the one the check approved, is in scope and is the most valuable thing to look at.
- **Denial of service against the consuming host.** Unbounded memory on a large response or a long
  traversal; a server-supplied value that stalls the process. Two guards exist for this: a
  self-referential cursor is refused rather than followed forever (D29), and a `Retry-After` longer
  than `MaxBackoff` surfaces as a 429 rather than parking the caller's task for an hour on a number
  a server chose (D30). Streaming applies a third: each topic owns a bounded buffer that drops the
  oldest event rather than blocking the read loop (D34).
- **Deserialization flaws** reachable from a response body or a stream frame. Struct models are read
  straight off `Utf8JsonReader` by generated converters rather than through the reflection-free
  built-in path (D32), and streaming converters walk their objects through one shared cursor (D36) —
  both are hand-auditable code operating on untrusted bytes, and a reader left mid-value
  deserializes the *next* field from the wrong token without throwing.
- **Path or query injection** through a caller-supplied ticker, date, filter or parameter.
- **The published packages themselves** — a package that carries something this repository does not,
  or that fails to match its source.

## What is not in scope

- **Massive's own service and API.** Report those to Massive; this repository has no control over
  them and no ability to fix them.
- **Vulnerabilities in dependencies** — NodaTime and `System.Threading.RateLimiting` are the only
  two core takes (rule 7). Report those upstream, but do tell us, because we decide what version to
  depend on. Advisories against any package, direct or transitive, already **fail the build**:
  NuGetAudit's `NU19xx` codes are warnings, and `TreatWarningsAsErrors` is on (rule 9).
- **A caller's own key management.** The samples read `MASSIVE_API_KEY` from the environment and
  nothing else; what a consuming application does with its credential is that application's
  responsibility.
- **Entitlement.** A `403` for a plan that does not cover an endpoint is the server's answer, not a
  defect — the SDK ships every operation the description declares and lets the service decide (D9).
- **A route that answers `404`.** Several mapped operations are declared by Massive's description
  but not served by Massive's API. They ship as declared, with the status and the date observed
  pinned in a live test (D21).
- Anything requiring the attacker to already control the configuration or the process.

## Handling your own key

Not a vulnerability class, but the most likely way an incident actually happens:

- **Prefer the default.** Authentication defaults to `Authorization: Bearer`, not the `apiKey` query
  parameter Massive's own description declares, because query strings leak into access logs,
  proxies, and browser history (D2). `MassiveAuthenticationScheme.QueryString` exists for the cases
  that need it — treat every URL the client builds under it as sensitive.
- **`.env` is git-ignored.** Keep it that way. A key in git history is a key that has to be rotated.
- **CI holds no key, by construction.** Tests that call the live service are committed and run
  locally; CI excludes them by category, holds no credential secret, and a CI job asserts that no
  workflow references one (rule 13). If you fork this and add a key secret, that job fails — which
  is the point.

## Hardening already in place

Not a guarantee, but useful context for anyone looking:

- Secret scanning and push protection are on, `.env` is git-ignored, and the repository's full
  history was audited for credential material before it was made public.
- No reflection-based serialization anywhere in shipped code (rule 3), and every shipped library is
  `IsAotCompatible` and `IsTrimmable` (rule 4) — verified by a Native AOT publish that must emit
  zero IL warnings.
- Builds are warning-free and deterministic (rules 6 and 9).
- `CredentialLoggingTests` asserts the DI package's logging bridge never renders a key.
- The 147-operation surface is generated from a vendored OpenAPI description and the generated
  output is committed, so a change to what the SDK exposes arrives as a reviewable diff (D1).
