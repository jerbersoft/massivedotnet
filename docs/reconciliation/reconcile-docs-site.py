#!/usr/bin/env python3
"""Reconcile the Massive docs site against specs/openapi.json.

Constitution rule 1 promises every non-deprecated REST operation in the description is
reachable from the public API. That guarantee is only as wide as the description itself,
so this script asks the question the description cannot answer: does the docs site
document a route the description omits?

It joins on the route, not on prose. Every docs-site REST page carries an
`**Endpoint:** ` + "`GET /path`" + ` line whose path template matches the description's path key
character for character, including the parameter name -- which matters, because the
description declares the same wire route once per asset class, distinguished only by
that name (`/v3/quotes/{stockTicker}` beside `/v3/quotes/{optionsTicker}`).

Reads the network, so it is never part of the test suite (rule 13). Run it by hand:

    python3 docs/reconciliation/reconcile-docs-site.py --out docs/reconciliation/<date>-report.md

Needs no API key: llms.txt and the .md pages are public. Confirming whether an
undocumented route is actually *served* does need one, and is a separate step the
report's own notes describe.
"""

from __future__ import annotations

import argparse
import collections
import concurrent.futures
import json
import pathlib
import re
import ssl
import sys
import urllib.request

INDEX = "https://massive.com/docs/llms.txt"
PAGE = "https://massive.com/docs/rest/{slug}.md"
REPO = pathlib.Path(__file__).resolve().parents[2]

# The index lists every documented page as a markdown link. The description text after the
# URL is optional -- three futures pages carry none -- so the trailing group must be too.
LINK = re.compile(r"^- \[([^\]]*)\]\(https://massive\.com/docs/rest/([^)]*)\.md\)(?:: ?(.*))?$")

# Each page names the route it documents exactly once, in a fenced inline code span.
ENDPOINT = re.compile(r"\*\*Endpoint:\*\*\s*`([A-Z]+)\s+([^`]+)`")


def _trust_store() -> ssl.SSLContext:
    """A context that verifies, on a python.org build with no system CA bundle too.

    Those builds ship an empty trust store until someone runs `Install Certificates.command`,
    and every request fails verification. Falling back to certifi keeps the check on rather
    than reaching for the unverified context, which would make this script a place where
    turning off TLS verification looks normal.
    """
    context = ssl.create_default_context()
    if context.cert_store_stats()["x509_ca"] == 0:
        try:
            import certifi
        except ImportError:
            raise RuntimeError(
                "No trusted CA certificates are installed. Run "
                "'/Applications/Python 3.x/Install Certificates.command', or 'pip install certifi'."
            ) from None
        context = ssl.create_default_context(cafile=certifi.where())
    return context


TRUST = _trust_store()


def fetch(url: str) -> str:
    with urllib.request.urlopen(url, timeout=60, context=TRUST) as response:
        if response.status != 200:
            raise RuntimeError(f"{url} answered {response.status}")
        return response.read().decode("utf-8")


def rest_slugs(index: str) -> list[tuple[str, str]]:
    """The (slug, title) of every page under the index's `## Rest` heading."""
    out, inside = [], False
    for line in index.splitlines():
        if line.startswith("## "):
            inside = line.strip() == "## Rest"
            continue
        if inside and (m := LINK.match(line)):
            out.append((m.group(2), m.group(1)))
    return out


def documented_routes(slugs: list[tuple[str, str]]) -> dict[str, tuple[str, str]]:
    """slug -> (method, route), fetched concurrently because 150 serial requests is a minute."""
    def one(entry: tuple[str, str]) -> tuple[str, tuple[str, str]]:
        slug, _ = entry
        found = ENDPOINT.findall(fetch(PAGE.format(slug=slug)))
        if not found:
            raise RuntimeError(f"{slug} carries no **Endpoint:** line; the page format changed")
        if len({(m, r.strip()) for m, r in found}) > 1:
            raise RuntimeError(f"{slug} names more than one endpoint: {found}")
        return slug, (found[0][0], found[0][1].strip().rstrip("/"))

    with concurrent.futures.ThreadPoolExecutor(max_workers=6) as pool:
        return dict(pool.map(one, slugs))


def is_experimental(path: str, operation: dict) -> bool:
    """Stability as D18 reads it, so this report agrees with what the generator emits.

    The extension alone is not enough -- it appears on two of the fourteen `vX` routes -- so
    the path is the primary signal, `vX` by prefix (D23) and `dev` by exact match (D22).
    """
    return "x-polygon-experimental" in operation or any(
        segment.startswith("vX") or segment == "dev" for segment in path.split("/")
    )


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", type=pathlib.Path, help="write the markdown report here")
    args = parser.parse_args()

    spec = json.loads((REPO / "specs/openapi.json").read_text())
    mapped = {
        e["operationId"]: f"{e['group']}.{e['method']}"
        for e in json.loads((REPO / "specs/endpoints.map.json").read_text())["endpoints"]
    }
    operations = {p: item["get"] for p, item in spec["paths"].items() if "get" in item}

    slugs = rest_slugs(fetch(INDEX))
    titles = dict(slugs)
    routes = documented_routes(slugs)

    pages_by_path: dict[str, list[str]] = collections.defaultdict(list)
    orphans: list[tuple[str, str, str]] = []
    for slug, (method, route) in sorted(routes.items()):
        if method == "GET" and route in operations:
            pages_by_path[route].append(slug)
        else:
            orphans.append((slug, method, route))

    lines: list[str] = []
    w = lines.append
    w(f"- docs-site REST pages: **{len(slugs)}**")
    w(f"- description GET operations: **{len(operations)}**")
    w(f"- pages whose route matches no operation: **{len(orphans)}**")
    duplicates = sum(len(v) - 1 for v in pages_by_path.values() if len(v) > 1)
    w(f"- pages that re-document a route already documented: **{duplicates}**")
    w(f"- operations with no page: **{len(operations) - len(pages_by_path)}**")
    w("")

    if orphans:
        w("## Documented, absent from the description")
        w("")
        w("Each of these is a hole in the coverage guarantee: rule 1 cannot reach an")
        w("operation the description never declares.")
        w("")
        for slug, method, route in orphans:
            w(f"- `{method} {route}` — [{titles[slug]}](https://massive.com/docs/rest/{slug})")
        w("")

    w("## Declared, undocumented")
    w("")
    w("| Route | Operation | Stability | SDK |")
    w("|---|---|---|---|")
    for path in sorted(set(operations) - set(pages_by_path)):
        op = operations[path]
        stability = (
            "deprecated" if "x-polygon-deprecation" in op
            else "experimental" if is_experimental(path, op)
            else "stable"
        )
        oid = op.get("operationId", "")
        w(f"| `{path}` | `{oid}` | {stability} | {mapped.get(oid, '—')} |")
    w("")

    w("## Routes documented once per asset class")
    w("")
    for path, pages in sorted(pages_by_path.items()):
        if len(pages) > 1:
            w(f"- `{path}` — {', '.join(f'`{p}`' for p in pages)}")
    w("")

    w("## Every page, joined")
    w("")
    w("| Page | Route | Operation | SDK |")
    w("|---|---|---|---|")
    for slug, _ in sorted(slugs):
        method, route = routes[slug]
        oid = operations.get(route, {}).get("operationId", "") if method == "GET" else ""
        w(f"| `{slug}` | `{route}` | `{oid}` | {mapped.get(oid, '—')} |")

    report = "\n".join(lines) + "\n"
    if args.out:
        args.out.write_text(report)
        print(f"wrote {args.out}", file=sys.stderr)
    else:
        print(report)
    return 1 if orphans else 0


if __name__ == "__main__":
    raise SystemExit(main())
