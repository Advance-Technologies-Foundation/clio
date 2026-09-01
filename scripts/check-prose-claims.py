#!/usr/bin/env python3
"""Cross-check factual claims made in PROSE against the values they claim to describe.

Review keeps finding the same defect here: a sentence in a comment, a docblock or an MCP
[Description] states a number that the code also states, and the two disagree. It has happened
with the bundled package version, a test count, a library generation, a list of burned version
numbers, and a claim about which archive a capability shipped in. Every one was caught by a human
reading carefully. None of them had to be.

Advisory by default: prints findings and exits 0. Pass --strict to fail on BLOCKER/HIGH.
"""
import argparse
import gzip
import hashlib
import io
import json
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
KB = os.environ.get("CLIO_KNOWLEDGE_PATH", os.path.join(os.path.dirname(ROOT), "clio-knowledge"))

PIN = "clio.tests/Common/BundledProcessBuilderPackageTests.cs"
FIXTURE = "clio.tests/Command/McpServer/Fixtures/curated-knowledge-names.json"
ARCHIVE = "clio/CrtProcessBuilder/CrtProcessBuilder.gz"
BUILD_OUTPUT = "clio/bin/Release/net8.0/CrtProcessBuilder/CrtProcessBuilder.gz"

SKIP_DIRS = {".git", "bin", "obj", "node_modules", ".vs", "packages", "TestResults"}
COUNTED = re.compile(
    r"\b(\d[\d  ]{2,}|one|two|three|four|five|six|seven|eight|nine|ten)\s+"
    r"(passing|tests?|flows?|processes|records?|names?|files?|articles?|occurrences?|lists?|times)\b",
    re.IGNORECASE,
)
VERSION = re.compile(r"(?<![\w.])(\d+\.\d+\.\d+\.\d+)(?![\w.])")

findings = []


def report(severity, where, what):
    findings.append((severity, where, what))


def read(path):
    full = os.path.join(ROOT, path)
    if not os.path.exists(full):
        return None
    return io.open(full, encoding="utf-8-sig", errors="replace").read()


def const(text, name):
    match = re.search(r"\b" + name + r'\s*=\s*"([^"]+)"', text or "")
    return match.group(1) if match else None


def walk_sources():
    for base, dirs, files in os.walk(ROOT):
        dirs[:] = [d for d in dirs if d not in SKIP_DIRS]
        for name in files:
            if not name.endswith((".cs", ".md")):
                continue
            path = os.path.relpath(os.path.join(base, name), ROOT).replace("\\", "/")
            try:
                yield path, io.open(os.path.join(base, name), encoding="utf-8-sig", errors="replace").read()
            except OSError:
                continue


def in_prose(text, position):
    """True when the offset sits in a comment, a docblock, or an attribute string."""
    start = text.rfind("\n", 0, position) + 1
    end = text.find("\n", position)
    line = text[start:end if end > 0 else len(text)]
    return bool(re.match(r"\s*(//|/\*|\*|#)", line)) or "Description(" in line


def descriptor_of(path):
    """The version and stamp recorded inside a packed package archive."""
    full = os.path.join(ROOT, path)
    if not os.path.exists(full):
        return None
    try:
        text = gzip.open(full, "rb").read().decode("utf-8", errors="replace")
    except OSError:
        return None
    version = re.search(r'"PackageVersion":\s*"([0-9.]+)"', text)
    stamp = re.search(r'"ModifiedOnUtc":\s*"(/Date\(\d+\)/)"', text)
    return (version.group(1) if version else None, stamp.group(1) if stamp else None)


def digest(path):
    full = os.path.join(ROOT, path)
    if not os.path.exists(full):
        return None
    return hashlib.sha256(io.open(full, "rb").read()).hexdigest()


def check_archive_pins():
    """Every pin beside the archive must describe the archive that is actually committed."""
    pin = read(PIN)
    if not pin:
        return None
    version = const(pin, "ExpectedArchiveVersion")
    actual = descriptor_of(ARCHIVE)
    if actual:
        if version and actual[0] != version:
            report("BLOCKER", PIN, "ExpectedArchiveVersion is {}; the archive says {}".format(version, actual[0]))
        stamp = const(pin, "ExpectedDescriptorModifiedOnUtc")
        if stamp and actual[1] != stamp:
            report("BLOCKER", PIN,
                   "ExpectedDescriptorModifiedOnUtc is {}; the archive says {}".format(stamp, actual[1]))
    sha = const(pin, "ExpectedArchiveSha256")
    got = digest(ARCHIVE)
    if sha and got and got.upper() != sha.upper().replace(" ", ""):
        report("BLOCKER", PIN, "ExpectedArchiveSha256 does not match the committed archive ({}...)".format(got[:16]))
    built = digest(BUILD_OUTPUT)
    if built and got and built != got:
        report("HIGH", BUILD_OUTPUT,
               "the build output carries DIFFERENT bytes from the committed archive - an install resolves "
               "it from here, so any local verification proves nothing")
    return version


def check_superseded_versions(current):
    """A version number in prose that this branch has already moved past is a stale claim, or history."""
    if not current:
        return
    try:
        family, tail = current.rsplit(".", 1)
        ceiling = int(tail)
    except ValueError:
        return
    for path, text in walk_sources():
        for match in VERSION.finditer(text):
            value = match.group(1)
            if value == current or not value.startswith(family + "."):
                continue
            try:
                if int(value.rsplit(".", 1)[1]) >= ceiling:
                    continue
            except ValueError:
                continue
            if not in_prose(text, match.start()):
                continue
            line = text[:match.start()].count("\n") + 1
            report("CHECK", "{}:{}".format(path, line),
                   "names {} in prose while the shipped archive is {} - deliberate history, or stale?"
                   .format(value, current))


def check_guidance_generation():
    """The curated-name fixture must BE a regeneration of the library, not a hand edit of one."""
    raw = read(FIXTURE)
    bundle_path = os.path.join(KB, "bundle-source.json")
    if not raw or not os.path.exists(bundle_path):
        return
    fixture = json.loads(raw)
    bundle = json.load(io.open(bundle_path, encoding="utf-8"))
    if fixture.get("libraryVersion") != bundle.get("libraryVersion"):
        report("BLOCKER", FIXTURE,
               "pinned to {}; the knowledge branch ships {}"
               .format(fixture.get("libraryVersion"), bundle.get("libraryVersion")))
    gated = set(fixture.get("featureGatedNames") or [])
    expected = {value
                for resource in bundle.get("resources", [])
                if resource.get("role") == "guidance"
                for value in (resource.get("itemId"), resource.get("topicId"))
                if value} - gated
    have = set(fixture.get("availableNames") or [])
    missing, extra = sorted(expected - have), sorted(have - expected)
    if missing or extra:
        detail = "{} missing, {} extra".format(len(missing), len(extra))
        if missing:
            detail += " (first missing: {})".format(missing[0])
        report("HIGH", FIXTURE, "not a regeneration of bundle-source.json: " + detail)


def check_counted_claims():
    """A counted claim in prose has no oracle. List them so each one is chosen, not inherited."""
    for path, text in walk_sources():
        if not path.startswith(("clio/", "clio.tests/", "docs/")):
            continue
        for match in COUNTED.finditer(text):
            if in_prose(text, match.start()):
                report("COUNTED", "{}:{}".format(path, text[:match.start()].count("\n") + 1),
                       match.group(0).strip())


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--strict", action="store_true", help="exit non-zero on BLOCKER/HIGH")
    parser.add_argument("--counted", action="store_true", help="also list counted claims, which need a human")
    parser.add_argument("--versions", action="store_true",
                        help="also list versions named in prose below the shipped one (noisy: most are history)")
    args = parser.parse_args()

    current = check_archive_pins()
    check_guidance_generation()
    # Opt-in, both of them, and deliberately. Run over this repository they produce a dozen findings
    # that are all legitimate - a version named because it is the RequiresPackage floor, a count that
    # is correct. A check with that signal-to-noise ratio is not a gate: it teaches the reader to skip
    # the output, which is the same failure as the stale list this script replaced. The exact
    # cross-checks above have no false positives and are the part worth running every time.
    if args.versions:
        check_superseded_versions(current)
    if args.counted:
        check_counted_claims()

    order = {"BLOCKER": 0, "HIGH": 1, "CHECK": 2, "COUNTED": 3}
    for severity, where, what in sorted(findings, key=lambda f: (order.get(f[0], 9), f[1])):
        print("{:8} {}".format(severity, where))
        print("         {}".format(what))
    blocking = [f for f in findings if f[0] in ("BLOCKER", "HIGH")]
    print("\n{} finding(s); {} at BLOCKER/HIGH.".format(len(findings), len(blocking)))
    return 1 if (args.strict and blocking) else 0


if __name__ == "__main__":
    sys.exit(main())
