#!/usr/bin/env python3
"""
Cross-platform installer for Creatio Engineering AI Plugin Marketplace.

Installs plugins directly to Claude Code — no Node.js or npm required.

Usage:
  macOS/Linux:  curl -fsSL https://ai-instructions-rnd.creatio.com/install.py | python3
  Windows:      irm https://ai-instructions-rnd.creatio.com/install.py -OutFile install.py; python install.py

Requirements: Python 3.8+ only. Zero external dependencies.
"""

import argparse
import hashlib
import json
import os
import platform
import re
import socket
import sys
import tarfile
import tempfile
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path
from typing import Any, List, Optional

# ---- Console encoding ----
#
# Force UTF-8 on stdout/stderr. The installer prints non-ASCII characters
# (em-dash "—", arrow "→", etc.) in its status/progress messages. On Windows
# the console defaults to a legacy codepage (cp1251, cp866, cp1252, …) whose
# `charmap` codec cannot encode those characters, so a bare `print()` raises
# `UnicodeEncodeError` and aborts the whole install mid-run. Reconfiguring to
# UTF-8 with errors="replace" makes every print safe regardless of the active
# codepage: a proper UTF-8 terminal shows the glyphs correctly, and anything
# that still can't render them degrades to a replacement char instead of
# crashing. `reconfigure` exists on io.TextIOWrapper (Python 3.7+); guard it so
# an unusual stream object (already-wrapped, redirected) can't break startup.
for _stream in (sys.stdout, sys.stderr):
    try:
        _stream.reconfigure(encoding="utf-8", errors="replace")
    except (AttributeError, ValueError, OSError):
        pass

# ---- Configuration ----

BASE_URL = os.environ.get(
    "CREATIO_MARKETPLACE_URL",
    "https://ai-instructions-rnd.creatio.com/marketplace",
)
REGISTRY_URL = f"{BASE_URL}/registry.json"
HEALTH_URL = "https://ai-instructions-rnd.creatio.com/api/v1/health"
MARKETPLACE_NAME = "creatio-engineering"
GHE_HOST = "creatio.ghe.com"
GHE_REPO = "engineering/ai-instructions"
# Canonical marketplace clone URLs, derived from GHE_HOST + GHE_REPO so the
# host/repo are defined exactly once. This is the single source of truth; the
# mirror in ../lib/core/plugin-installer.js and the marketplace README must
# stay in sync with these values.
GHE_HTTPS_CLONE_URL = f"https://{GHE_HOST}/{GHE_REPO}.git"
GHE_SSH_CLONE_URL = f"creatio@{GHE_HOST}:{GHE_REPO}.git"
MIN_PYTHON = (3, 8)

_MARKETPLACE_CLONE_ATTEMPTS = set()
_GHE_SSH_REPO_ACCESS = None
_GHE_GH_AUTH = None


# ---- Internal DNS pin ----
#
# The installer talks to https://ai-instructions-rnd.creatio.com which only
# resolves correctly when the corporate VPN is up. If the VPN is down, the
# public creatio.com authoritative DNS can return an unrelated public IP,
# the OS resolver caches that answer for the TTL, and the installer then
# keeps hitting a public 404 even after the VPN comes back up.
#
# Pinning the IP at the `socket.getaddrinfo` level bypasses the OS
# resolver entirely. TLS still negotiates SNI against the original
# hostname (urllib passes the URL host through unchanged), so the
# internal load balancer at the pinned IP — which hosts many domains —
# routes to the correct backend and the certificate validates normally.
# Off VPN the TCP connect itself fails cleanly (connection refused /
# timeout) which the calling code already handles.
#
# Override:
#   CREATIO_DNS_PIN_DISABLE=1                       -> skip the patch.
#   CREATIO_DNS_PIN_OVERRIDE="host:ip,host:ip"      -> extend / override
#                                                       the default map.

_DEFAULT_DNS_PIN = {
    "ai-instructions-rnd.creatio.com": "10.48.14.53",
}


def _parse_dns_pin_override(raw: str) -> dict:
    """Parse `host:ipv4` pairs from `CREATIO_DNS_PIN_OVERRIDE`.

    IPv4 only. The format is intentionally simple — we split on the last
    `:` so that hostnames containing dots are fine, but bare IPv6
    literals (`2001:db8::1`) are NOT supported because they share the
    same separator. If we ever ship an IPv6 pin we will require RFC 3986
    bracket notation (`host:[2001:db8::1]`) and parse it here
    explicitly; today the override is rejected on the first stray colon
    by `socket.getaddrinfo` downstream.
    """
    if not raw:
        return {}
    out: dict = {}
    for pair in raw.split(","):
        host, sep, ip = pair.rpartition(":")
        host, ip = host.strip(), ip.strip()
        if sep and host and ip:
            out[host] = ip
    return out


def _resolve_dns_pins() -> dict:
    if os.environ.get("CREATIO_DNS_PIN_DISABLE") == "1":
        return {}
    pins = dict(_DEFAULT_DNS_PIN)
    pins.update(_parse_dns_pin_override(os.environ.get("CREATIO_DNS_PIN_OVERRIDE", "")))
    return pins


def _install_dns_pin() -> None:
    """Monkey-patch socket.getaddrinfo so listed hosts resolve to a fixed IP.

    Idempotent: if a previous call already patched, we leave the existing
    closure in place rather than chaining patches that bury the original
    resolver.
    """
    if getattr(socket, "_creatio_dns_pinned", False):
        return
    pins = _resolve_dns_pins()
    if not pins:
        return
    original = socket.getaddrinfo

    def patched(host, *args, **kwargs):
        if isinstance(host, str) and host in pins:
            host = pins[host]
        return original(host, *args, **kwargs)

    socket.getaddrinfo = patched
    socket._creatio_dns_pinned = True  # type: ignore[attr-defined]


_install_dns_pin()


# ---- Output helpers ----


def _safe_print(text: str, *, file=None) -> None:
    """print() that can't die on a legacy console codepage.

    The startup `reconfigure(..., errors="replace")` normally makes stdout/
    stderr UTF-8, but if that didn't take (very old Python, an exotic wrapped
    stream), a non-ASCII status message would still raise UnicodeEncodeError
    on a cp1251/cp866 Windows console. Fall back to encoding the line through
    the stream's own codec with errors="replace" so output degrades to a
    placeholder char instead of aborting the install.
    """
    stream = file if file is not None else sys.stdout
    try:
        print(text, file=stream)
    except UnicodeEncodeError:
        enc = getattr(stream, "encoding", None) or "ascii"
        stream.write(text.encode(enc, errors="replace").decode(enc, errors="replace") + "\n")


def info(msg: str) -> None:
    _safe_print(f"  [+] {msg}")


def warn(msg: str) -> None:
    _safe_print(f"  [!] {msg}", file=sys.stderr)


def fail(msg: str) -> None:
    _safe_print(f"\n  [ERROR] {msg}", file=sys.stderr)
    sys.exit(1)


# ---- Network ----


def fetch(url: str, timeout: int = 30) -> bytes:
    req = urllib.request.Request(
        url, headers={"User-Agent": "creatio-marketplace-installer/1.0"}
    )
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        return resp.read()


def fetch_json(url: str) -> Any:
    return json.loads(fetch(url).decode("utf-8"))


def check_network() -> None:
    try:
        fetch(HEALTH_URL, timeout=10)
        info("Corporate network: reachable")
    except (urllib.error.URLError, OSError):
        fail(
            "Cannot reach ai-instructions-rnd.creatio.com.\n"
            "  Ensure you are connected to the corporate network or VPN."
        )


# ---- JSON file helpers ----


def read_json(path: Path) -> Any:
    if not path.exists():
        return None
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (json.JSONDecodeError, OSError):
        return None


def write_json(path: Path, data: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")


# ---- Legacy npm install cleanup (one-shot preflight) ----
#
# Migration path away from the retired npm `@creatio/dev-skills` distribution.
# Users upgrading from that package have three artefacts on their machine that
# must be torn down before the Claude-plugin installer can take over cleanly:
#
#   1. The scheduler entry  — cron job `creatio-skills auto-sync` (Linux/macOS)
#                             or Task Scheduler task `CreatioDevSkillsAutoSync`
#                             (Windows). Left in place, this would fire every
#                             6 hours alongside our own scheduler and re-create
#                             the flat skill tree.
#   2. The flat skill tree  — Each skill lives at `~/.agents/skills/<name>/`
#                             (mirrored from the npm package). The Claude-plugin
#                             flow installs into `~/.claude/plugins/cache/...`
#                             instead, so the flat copies become dead weight.
#   3. The npm global pkg   — `npm uninstall -g @creatio/dev-skills` removes the
#                             `creatio-skills` CLI from PATH; otherwise users
#                             would still see the (now broken) command.
#
# Idempotent via a marker file written under `~/.claude/.creatio-marketplace/`
# so subsequent install.py runs (especially the every-6-hours cron tick) skip
# the cleanup entirely once it has succeeded once.

LEGACY_NPM_CRON_NEEDLE = "creatio-skills auto-sync"
LEGACY_NPM_TASK_NAME = "CreatioDevSkillsAutoSync"
LEGACY_NPM_PACKAGE = "@creatio/dev-skills"


def _legacy_npm_cleanup_marker() -> Path:
    return Path.home() / ".claude" / ".creatio-marketplace" / "legacy-npm-cleaned"


def _legacy_npm_state_dir() -> Path:
    return Path.home() / ".creatio-dev-skills"


def _legacy_npm_installed_skill_names() -> List[str]:
    """Read every skill name recorded by the legacy npm install.

    The legacy package wrote two slightly different shapes:

    * ``installed-skills.json`` — flat list of skill names (or objects with a
      ``name`` field) under a ``skills`` key.
    * ``installed-plugins.json`` — older plugin-keyed mapping where each value
      is a dict containing a ``skills`` list of names.

    Both files are optional. We walk whichever exists, dedupe the names, and
    return the result so the caller can delete the matching skill directories.
    Malformed JSON is treated as "nothing to remove" — the directory delete in
    step 3 below will still take care of leftover artefacts.
    """
    state_dir = _legacy_npm_state_dir()
    names: set = set()

    skills_file = state_dir / "installed-skills.json"
    raw = read_json(skills_file)
    if isinstance(raw, dict):
        entries = raw.get("skills")
        if isinstance(entries, list):
            for entry in entries:
                if isinstance(entry, str):
                    names.add(entry)
                elif isinstance(entry, dict):
                    nm = entry.get("name")
                    if isinstance(nm, str):
                        names.add(nm)

    plugins_file = state_dir / "installed-plugins.json"
    raw = read_json(plugins_file)
    if isinstance(raw, dict):
        plugins_map = raw.get("plugins")
        if isinstance(plugins_map, dict):
            for plugin_entry in plugins_map.values():
                if not isinstance(plugin_entry, dict):
                    continue
                plugin_skills = plugin_entry.get("skills")
                if isinstance(plugin_skills, list):
                    for nm in plugin_skills:
                        if isinstance(nm, str):
                            names.add(nm)

    return sorted(names)


def _legacy_npm_install_detected() -> bool:
    """Detect whether the legacy npm distribution left artefacts on this host.

    Three independent signals — any one is enough to trigger cleanup:

    * ``~/.creatio-dev-skills/`` state directory exists.
    * ``creatio-skills`` CLI is on PATH (npm-global bin shim still wired up).
    * ``npm ls -g @creatio/dev-skills --depth=0`` exits 0 (package still
      registered in the npm-global tree even if the bin shim went stale).
    """
    import shutil
    import subprocess as sp

    if _legacy_npm_state_dir().is_dir():
        return True
    if shutil.which("creatio-skills"):
        return True

    npm = shutil.which("npm")
    if npm:
        try:
            result = sp.run(
                [npm, "ls", "-g", LEGACY_NPM_PACKAGE, "--depth=0", "--json"],
                capture_output=True,
                text=True,
                timeout=30,
            )
            if result.returncode == 0:
                return True
        except (sp.SubprocessError, OSError):
            pass
    return False


def _legacy_npm_remove_scheduler() -> None:
    """Step 1 of cleanup — kill the legacy auto-sync scheduler."""
    import shutil
    import subprocess as sp

    if platform.system() == "Windows":
        try:
            query = sp.run(
                ["schtasks", "/Query", "/TN", LEGACY_NPM_TASK_NAME],
                capture_output=True,
                text=True,
                timeout=30,
            )
        except (sp.SubprocessError, OSError) as exc:
            warn(f"  legacy cleanup: schtasks /Query failed ({exc})")
            return
        if query.returncode != 0:
            info("  legacy cleanup: no Windows Task Scheduler entry to remove")
            return
        try:
            sp.run(
                ["schtasks", "/Delete", "/TN", LEGACY_NPM_TASK_NAME, "/F"],
                check=False,
                capture_output=True,
                text=True,
                timeout=30,
            )
            info(f"  legacy cleanup: removed Task Scheduler task {LEGACY_NPM_TASK_NAME}")
        except (sp.SubprocessError, OSError) as exc:
            warn(f"  legacy cleanup: schtasks /Delete failed ({exc})")
        return

    crontab = shutil.which("crontab")
    if not crontab:
        info("  legacy cleanup: crontab not available; skipping scheduler removal")
        return
    try:
        listing = sp.run(
            [crontab, "-l"], capture_output=True, text=True, timeout=15
        )
    except (sp.SubprocessError, OSError) as exc:
        warn(f"  legacy cleanup: crontab -l failed ({exc})")
        return
    if listing.returncode != 0:
        # No crontab for this user — nothing to scrub.
        info("  legacy cleanup: no crontab entry to remove")
        return
    lines = listing.stdout.splitlines()
    kept = [ln for ln in lines if LEGACY_NPM_CRON_NEEDLE not in ln]
    if len(kept) == len(lines):
        info("  legacy cleanup: no legacy cron entry found")
        return
    new_crontab = "\n".join(kept)
    if new_crontab and not new_crontab.endswith("\n"):
        new_crontab += "\n"
    try:
        sp.run(
            [crontab, "-"],
            input=new_crontab,
            text=True,
            capture_output=True,
            timeout=15,
            check=False,
        )
        info(
            f"  legacy cleanup: removed {len(lines) - len(kept)} legacy cron "
            f"line(s) containing '{LEGACY_NPM_CRON_NEEDLE}'"
        )
    except (sp.SubprocessError, OSError) as exc:
        warn(f"  legacy cleanup: crontab rewrite failed ({exc})")


def _legacy_npm_remove_flat_skills_and_state() -> None:
    """Step 2 of cleanup — remove flat skills under `~/.agents/skills` and the
    `~/.creatio-dev-skills/` state directory.
    """
    import shutil

    agents_skills = Path.home() / ".agents" / "skills"
    removed_skills = 0
    if agents_skills.is_dir():
        for skill_name in _legacy_npm_installed_skill_names():
            # Defensive: refuse path separators / parents in skill names so a
            # poisoned state file cannot make us shutil.rmtree something
            # outside the agents tree.
            if (
                not skill_name
                or "/" in skill_name
                or "\\" in skill_name
                or skill_name in {".", ".."}
                or skill_name.startswith(".")
            ):
                continue
            candidate = agents_skills / skill_name
            try:
                candidate.resolve().relative_to(agents_skills.resolve())
            except (ValueError, OSError):
                continue
            if candidate.is_dir() and not candidate.is_symlink():
                try:
                    shutil.rmtree(candidate)
                    removed_skills += 1
                except OSError as exc:
                    warn(
                        f"  legacy cleanup: could not remove "
                        f"~/.agents/skills/{skill_name} ({exc})"
                    )
        if removed_skills:
            info(
                f"  legacy cleanup: removed {removed_skills} flat skill "
                f"director(y/ies) from ~/.agents/skills/"
            )

    state_dir = _legacy_npm_state_dir()
    if state_dir.is_dir():
        try:
            shutil.rmtree(state_dir)
            info("  legacy cleanup: removed state directory ~/.creatio-dev-skills/")
        except OSError as exc:
            warn(
                f"  legacy cleanup: could not remove {state_dir} ({exc}); "
                "delete it manually and re-run install.py"
            )


def _legacy_npm_uninstall_package() -> None:
    """Step 3 of cleanup — uninstall the npm global package."""
    import shutil
    import subprocess as sp

    npm = shutil.which("npm")
    if not npm:
        info(
            f"  legacy cleanup: npm not on PATH; cannot run "
            f"`npm uninstall -g {LEGACY_NPM_PACKAGE}` (already gone, or manual cleanup needed)"
        )
        return
    try:
        result = sp.run(
            [npm, "uninstall", "-g", LEGACY_NPM_PACKAGE],
            check=False,
            capture_output=True,
            text=True,
            timeout=180,
        )
    except (sp.SubprocessError, OSError) as exc:
        warn(f"  legacy cleanup: npm uninstall failed ({exc})")
        return
    if result.returncode == 0:
        info(f"  legacy cleanup: ran `npm uninstall -g {LEGACY_NPM_PACKAGE}`")
    else:
        stderr = (result.stderr or "").strip() or (result.stdout or "").strip()
        warn(
            f"  legacy cleanup: `npm uninstall -g {LEGACY_NPM_PACKAGE}` exited "
            f"{result.returncode}: {stderr[:300]}"
        )


def cleanup_legacy_npm_install() -> None:
    """One-shot preflight that removes the legacy npm `@creatio/dev-skills`
    install.

    Order:
      1. Short-circuit if the marker file already exists (idempotent — we ran
         once on a previous tick).
      2. Detect whether anything legacy is on disk; if not, write the marker
         and return.
      3. Step through scheduler removal, flat-skill + state-dir removal, and
         finally `npm uninstall -g`. Each step is independently safe and
         best-effort — warnings instead of fatals so a partial cleanup never
         blocks the new Claude-only install.
      4. Write the marker file so subsequent runs skip the entire dance.
    """
    marker = _legacy_npm_cleanup_marker()
    if marker.exists():
        return

    if not _legacy_npm_install_detected():
        # Nothing to do, but write the marker so we do not re-scan every run.
        try:
            marker.parent.mkdir(parents=True, exist_ok=True)
            marker.write_text(
                json.dumps(
                    {"completedAt": _now(), "legacyDetected": False}, indent=2
                )
                + "\n",
                encoding="utf-8",
            )
        except OSError as exc:
            warn(f"  legacy cleanup: could not write marker file ({exc})")
        return

    info(
        f"Detected legacy {LEGACY_NPM_PACKAGE} install — "
        "removing scheduler / flat skills / npm package..."
    )

    _legacy_npm_remove_scheduler()
    _legacy_npm_remove_flat_skills_and_state()
    _legacy_npm_uninstall_package()

    try:
        marker.parent.mkdir(parents=True, exist_ok=True)
        marker.write_text(
            json.dumps(
                {"completedAt": _now(), "legacyDetected": True}, indent=2
            )
            + "\n",
            encoding="utf-8",
        )
        info("Legacy npm cleanup complete (marker written; will not re-run).")
    except OSError as exc:
        warn(
            f"  legacy cleanup: could not write marker file ({exc}); "
            "preflight may re-run on the next tick"
        )


# ---- Duplicate skill-symlink cleanup (every install / update) ----
#
# The retired npm `@creatio/dev-skills` flow installed skills through the
# third-party `skills` CLI, which materialized each skill under
# ~/.agents/skills/<name> and symlinked it into ~/.claude/skills/. Claude Code
# now gets those same skills from the plugin marketplace
# (~/.claude/plugins/cache/...), so each surviving symlink is a pure duplicate:
# the skill loads twice — once namespaced from the plugin, once bare from the
# symlink.
#
# `cleanup_legacy_npm_install` removes the symlink *targets* under
# ~/.agents/skills but never the ~/.claude/skills symlinks themselves, and it is
# one-shot (gated by the `legacy-npm-cleaned` marker) so it does not re-run on
# machines migrated before this fix shipped. This routine therefore runs
# unconditionally on every install / auto-update tick, independent of that
# marker: it is idempotent (no matching symlinks → no-op, no output) and so
# self-heals the existing fleet on the next run.


def _installed_plugin_skill_names(claude_target: dict) -> set:
    """Skill names physically present in the installed Claude marketplace plugins.

    Scans the plugin cache —
    ~/.claude/plugins/cache/<marketplace>/<plugin>/<version>/skills/<skill>/SKILL.md —
    and returns the skill directory names found there.

    This on-disk check (rather than trusting the registry or a state file) is the
    safety anchor for duplicate-symlink removal: a bare ~/.claude/skills symlink
    is only ever dropped when the SAME skill is verifiably installed here as a
    plugin, so the developer is never left without a copy — no matter whether
    they installed the full bundle, a selective --plugin subset, hit a partial
    install failure, or arrived via the auto-update tick.
    """
    cache = claude_target.get("plugin_cache")
    names: set = set()
    if not cache:
        return names
    cache_dir = Path(cache)
    if not cache_dir.is_dir():
        return names
    for plugin_dir in cache_dir.iterdir():
        if not plugin_dir.is_dir():
            continue
        for version_dir in plugin_dir.iterdir():
            skills_dir = version_dir / "skills"
            if not skills_dir.is_dir():
                continue
            for skill_dir in skills_dir.iterdir():
                if skill_dir.is_dir() and (skill_dir / "SKILL.md").exists():
                    names.add(skill_dir.name)
    return names


def _unlink_symlink(link: Path) -> bool:
    """Remove a symlink, never its target. Returns True on success.

    On Windows a *directory* symlink is a reparse point that `unlink` refuses;
    `rmdir` removes the link itself (again without touching the target). Try
    unlink first (file symlinks, POSIX), fall back to rmdir.
    """
    try:
        link.unlink()
        return True
    except OSError:
        try:
            link.rmdir()
            return True
        except OSError as exc:
            warn(f"  could not remove duplicate skill symlink {link} ({exc})")
            return False


def remove_duplicate_agent_skill_symlinks(claude_target: Optional[dict]) -> None:
    """Delete ~/.claude/skills/<name> symlinks already provided by an installed plugin.

    Call this AFTER the plugin install loop so the cache reflects this run.

    Safety invariant — a bare symlink is removed ONLY when ALL of these hold, so
    the developer can never end up with a skill that has no copy at all and a
    hand-authored skill is never touched:

      * the SAME skill is physically present in the installed plugin cache
        (verified on disk by ``_installed_plugin_skill_names``). If the plugin
        did not install — selective ``--plugin`` subset, a download/SHA failure,
        no Claude target — the bare symlink is kept as the working fallback;
      * the entry is a symlink (a real directory authored by hand is left alone);
      * its raw, unresolved target points into ~/.agents/skills (links into any
        other root are untouched; a dangling link from a prior partial cleanup
        is still caught).

    Version-agnostic: matching is by skill NAME, so a developer on any prior npm
    skill-package version converges to the single plugin copy. Idempotent and
    silent when there is nothing to remove, so the auto-update tick stays quiet.
    """
    if not claude_target:
        return
    claude_skills = Path.home() / ".claude" / "skills"
    if not claude_skills.is_dir():
        return
    plugin_skills = _installed_plugin_skill_names(claude_target)
    if not plugin_skills:
        return

    removed: List[str] = []
    for entry in sorted(claude_skills.iterdir()):
        if entry.name not in plugin_skills:
            continue
        if not entry.is_symlink():
            continue
        try:
            raw_target = os.readlink(entry)
        except OSError:
            continue
        # Normalize separators so the substring match holds on Windows (\) too.
        if "/.agents/skills/" not in raw_target.replace("\\", "/"):
            continue
        if _unlink_symlink(entry):
            removed.append(entry.name)

    if removed:
        info(
            f"Removed {len(removed)} duplicate skill symlink(s) from "
            "~/.claude/skills/ (now provided by marketplace plugins): "
            + ", ".join(removed)
        )


# ---- Target detection ----


def detect_targets() -> dict:
    home = Path.home()
    targets = {}

    claude_dir = home / ".claude"
    if claude_dir.is_dir():
        targets["claude"] = {
            "name": "Claude Code",
            "home": claude_dir,
            "plugin_cache": claude_dir / "plugins" / "cache" / MARKETPLACE_NAME,
            "settings": claude_dir / "settings.json",
            "installed_plugins": claude_dir / "plugins" / "installed_plugins.json",
            "known_marketplaces": claude_dir / "plugins" / "known_marketplaces.json",
        }

    return targets


# ---- Claude Code installation ----


def install_plugin_claude(plugin: dict, tarball_bytes: bytes, target: dict) -> None:
    name = plugin["name"]
    version = plugin["version"]
    cache_dir = target["plugin_cache"] / name / version

    # Extract tarball to cache
    if cache_dir.exists():
        import shutil

        shutil.rmtree(cache_dir)
    cache_dir.mkdir(parents=True, exist_ok=True)

    with tempfile.NamedTemporaryFile(suffix=".tar.gz", delete=False) as tmp:
        tmp.write(tarball_bytes)
        tmp_path = tmp.name

    try:
        with tarfile.open(tmp_path, "r:gz") as tar:
            # Extract, stripping the top-level directory name
            members = tar.getmembers()
            prefix = ""
            if members:
                first = members[0].name
                if "/" in first:
                    prefix = first.split("/")[0] + "/"
                elif members[0].isdir():
                    prefix = first + "/"

            for member in members:
                if member.name == prefix.rstrip("/"):
                    continue
                member.name = (
                    member.name[len(prefix) :]
                    if member.name.startswith(prefix)
                    else member.name
                )
                if member.name:
                    if not (member.isfile() or member.isdir()):
                        warn(f"  Skipping unsupported tar entry: {member.name}")
                        continue
                    # Validate against path traversal
                    resolved = (cache_dir / member.name).resolve()
                    try:
                        resolved.relative_to(cache_dir.resolve())
                    except ValueError:
                        warn(f"  Skipping path-traversal entry: {member.name}")
                        continue
                    # PEP 706 hardening: `filter="data"` strips owner/group,
                    # rejects setuid/setgid bits, and refuses path-traversal
                    # at the libc layer. Fallback to plain extract on Python
                    # 3.11 where the kwarg is unsupported.
                    try:
                        tar.extract(member, cache_dir, filter="data")
                    except TypeError:
                        tar.extract(member, cache_dir)
    finally:
        os.unlink(tmp_path)

    # Register known marketplace + clone repo for native auto-update
    marketplace_dir = (
        Path.home() / ".claude" / "plugins" / "marketplaces" / MARKETPLACE_NAME
    )
    if not (marketplace_dir / ".git").exists():
        _clone_marketplace_repo(marketplace_dir, "Claude Code")

    known = read_json(target["known_marketplaces"]) or {}
    # GitHub Enterprise marketplace: register a `git` source with the full
    # HTTPS clone URL. Claude Code clones it over HTTPS via the user's git
    # credential helper and reads `.claude-plugin/marketplace.json`.
    #   - A `github` source resolves `repo` against public github.com and
    #     ignores any extra `host` field, so it would clone
    #     `git@github.com:engineering/ai-instructions` over SSH and fail
    #     host-key verification on machines without a github.com SSH key.
    #   - A `url` source is NOT a git clone: `/plugin` refresh treats it as a
    #     raw document, so the catalog parses to a string and validation fails
    #     ("Invalid marketplace schema from URL: : expected object, received
    #     string"). `git` is the documented source type for GHES hosts.
    known[MARKETPLACE_NAME] = {
        "source": {"source": "git", "url": GHE_HTTPS_CLONE_URL},
        "installLocation": str(marketplace_dir),
        "lastUpdated": _now(),
    }
    write_json(target["known_marketplaces"], known)

    # Register installed plugin (Claude Code v2 format: {"version": 2, "plugins": {"key": [...]}})
    installed = read_json(target["installed_plugins"]) or {}
    plugin_key = f"{name}@{MARKETPLACE_NAME}"
    entry = {
        "scope": "user",
        "installPath": str(cache_dir),
        "version": version,
        "installedAt": _now(),
        "lastUpdated": _now(),
    }

    # Ensure v2 format: {"version": 2, "plugins": {"key": [...]}}
    if installed.get("version") != 2:
        installed = {"version": 2, "plugins": {}}
    plugins_map = installed.setdefault("plugins", {})
    existing = plugins_map.get(plugin_key, [{}])
    if existing and isinstance(existing, list):
        entry["installedAt"] = existing[0].get("installedAt", entry["installedAt"])
    plugins_map[plugin_key] = [entry]
    write_json(target["installed_plugins"], installed)

    # Enable plugin in settings.json
    settings = read_json(target["settings"]) or {}
    if "enabledPlugins" not in settings:
        settings["enabledPlugins"] = {}
    settings["enabledPlugins"][plugin_key] = True
    write_json(target["settings"], settings)


# ---- Marketplace repo clone ----


def _non_interactive_git_env(method: str) -> dict:
    env = os.environ.copy()
    env["GIT_TERMINAL_PROMPT"] = "0"
    env["GH_PROMPT_DISABLED"] = "1"
    if method == "SSH":
        ssh_command = env.get("GIT_SSH_COMMAND", "ssh")
        env["GIT_SSH_COMMAND"] = (
            f"{ssh_command} -o BatchMode=yes -o ConnectTimeout=10 "
            "-o StrictHostKeyChecking=accept-new"
        )
    return env


def _has_ghe_ssh_repo_access(git: str, ssh_available: bool) -> bool:
    """Return whether this machine can access the marketplace repo over SSH."""
    global _GHE_SSH_REPO_ACCESS

    if _GHE_SSH_REPO_ACCESS is not None:
        return _GHE_SSH_REPO_ACCESS
    if not ssh_available:
        _GHE_SSH_REPO_ACCESS = False
        return False

    import subprocess as sp

    try:
        result = sp.run(
            [git, "ls-remote", "--heads", GHE_SSH_CLONE_URL],
            capture_output=True,
            text=True,
            timeout=30,
            env=_non_interactive_git_env("SSH"),
        )
        _GHE_SSH_REPO_ACCESS = result.returncode == 0
        return _GHE_SSH_REPO_ACCESS
    except (sp.SubprocessError, OSError):
        _GHE_SSH_REPO_ACCESS = False
        return False


def _has_ghe_gh_auth(gh: str) -> bool:
    """Return whether GitHub CLI has non-interactive auth for the GHE host."""
    global _GHE_GH_AUTH

    if _GHE_GH_AUTH is not None:
        return _GHE_GH_AUTH

    import subprocess as sp

    try:
        result = sp.run(
            [gh, "auth", "status", "--hostname", GHE_HOST],
            capture_output=True,
            text=True,
            timeout=15,
            env=_non_interactive_git_env("HTTPS"),
        )
        _GHE_GH_AUTH = result.returncode == 0
        return _GHE_GH_AUTH
    except (sp.SubprocessError, OSError):
        _GHE_GH_AUTH = False
        return False


def _clone_marketplace_repo(dest: Path, label: str = "Marketplace") -> bool:
    """Clone the GHE repo so Claude Code can read marketplace.json locally."""
    import shutil
    import subprocess as sp

    dest = dest.expanduser().resolve()
    if (dest / ".git").exists():
        return True
    if dest in _MARKETPLACE_CLONE_ATTEMPTS:
        return False
    _MARKETPLACE_CLONE_ATTEMPTS.add(dest)

    git = shutil.which("git")
    if not git:
        warn("git not found — skipping marketplace repo clone")
        return False

    if dest.exists() and any(dest.iterdir()):
        warn(f"  {label}: marketplace directory exists but is not a git repo: {dest}")
        return False

    dest.parent.mkdir(parents=True, exist_ok=True)

    def clone_with(method: str, tool: str, clone_url: str) -> bool:
        if dest.exists() and any(dest.iterdir()):
            warn(f"  {label}: marketplace directory exists but is not a git repo: {dest}")
            return False

        env = _non_interactive_git_env(method)
        if tool == "git":
            command = [git, "clone", "--depth", "1", clone_url, str(dest)]
        else:
            command = [tool, "repo", "clone", clone_url, str(dest), "--", "--depth=1"]
        try:
            result = sp.run(
                command,
                capture_output=True,
                text=True,
                timeout=60,
                env=env,
            )
            if result.returncode == 0:
                info(f"  {label}: marketplace repo cloned via {method} to {dest}")
                return True
            output = (result.stderr or result.stdout or "").strip().splitlines()
            detail = f" ({output[-1]})" if output else ""
            warn(f"  {label}: {method} clone failed{detail}")
        except (sp.SubprocessError, OSError) as e:
            warn(f"  {label}: {method} clone failed: {e}")
        return False

    ssh_available = shutil.which("ssh") is not None
    if _has_ghe_ssh_repo_access(git, ssh_available):
        if clone_with("SSH", "git", GHE_SSH_CLONE_URL):
            return True

    gh = shutil.which("gh")
    if gh and _has_ghe_gh_auth(gh):
        if clone_with("GitHub CLI", gh, GHE_HTTPS_CLONE_URL):
            return True

    if clone_with("HTTPS", "git", GHE_HTTPS_CLONE_URL):
        return True

    warn(
        f"  {label}: could not clone marketplace repo. "
        "Skipping native marketplace clone; downloaded skills were still installed."
    )
    warn(
        "  To enable native marketplace updates, configure one of: "
        "SSH key access (`ssh -T -o BatchMode=yes creatio@creatio.ghe.com`), "
        "GitHub CLI auth (`gh auth login --hostname creatio.ghe.com`), "
        "or HTTPS Git credentials/PAT for creatio.ghe.com."
    )
    return False


def _pull_marketplace_repo(targets: dict) -> None:
    """Pull latest changes in the marketplace repo clone for Claude Code."""
    if "claude" not in targets:
        return
    marketplace_dir = (
        Path.home() / ".claude" / "plugins" / "marketplaces" / MARKETPLACE_NAME
    )
    if not (marketplace_dir / ".git").exists():
        return
    import shutil
    import subprocess as sp

    git = shutil.which("git")
    if not git:
        return
    try:
        sp.run(
            [git, "-C", str(marketplace_dir), "pull", "--ff-only", "-q"],
            capture_output=True,
            timeout=30,
        )
    except (sp.SubprocessError, OSError):
        pass  # Best-effort


# ---- Global MCP configuration ----

# Defaults + prompt copy for env vars referenced by MCP server configs.
# Used when a server's config contains `${VAR}` placeholders. We prompt the
# developer once at install time and persist values into Claude Code's
# `~/.claude/settings.json` `env` block so Claude resolves them when it
# spawns each MCP process.
_MCP_ENV_VAR_DEFAULTS = {
    "CREATIO_MCP_BASE_URL": {
        "default": "http://localhost:8080/",
        "prompt": "Creatio MCP base URL",
        "secret": False,
    },
    "CREATIO_MCP_LOGIN": {
        "default": "Supervisor",
        "prompt": "Creatio MCP login",
        "secret": False,
    },
    "CREATIO_MCP_PASSWORD": {
        "default": "Supervisor",
        "prompt": "Creatio MCP password",
        "secret": True,
    },
    "GHE_MCP_TOKEN": {
        "default": "",
        "prompt": "Creatio GHE Copilot MCP token (https://copilot-api.creatio.ghe.com)",
        "secret": True,
    },
}

# Regex finds `${VAR}` and `${VAR:-default}` placeholders inside any string
# field of an MCP server config (url, args items, header values, etc.).
_ENV_PLACEHOLDER_RE = re.compile(r"\$\{([A-Z_][A-Z0-9_]*)(?::-[^}]*)?\}")


def _collect_env_placeholders(value) -> set:
    """Recursively scan an MCP server config and return every `${VAR}` name."""
    found: set = set()
    if isinstance(value, str):
        for match in _ENV_PLACEHOLDER_RE.finditer(value):
            found.add(match.group(1))
    elif isinstance(value, dict):
        for v in value.values():
            found |= _collect_env_placeholders(v)
    elif isinstance(value, list):
        for item in value:
            found |= _collect_env_placeholders(item)
    return found


def _select_required_mcp_servers(registry: dict, state: dict) -> dict:
    """Narrow the registry MCP catalog to only the servers actually needed by
    the user's installed plugins. The registry has `mcp: [names]` per plugin
    entry; we union those across `state["plugins"]` and pick from the catalog.

    Falls back to the full catalog when no plugins are installed yet (first
    run before the install loop populated state) or when no plugin declares
    `mcp:` — that preserves prior behavior for legacy registries.
    """
    catalog = registry.get("mcp_servers", {})
    if not catalog:
        return {}

    installed = set((state or {}).get("plugins", {}).keys())
    plugin_entries = registry.get("plugins", []) or []

    declared_any = any(isinstance(p.get("mcp"), list) and p["mcp"] for p in plugin_entries)
    if not declared_any:
        # Legacy registry without per-plugin mcp lists — keep the old "install
        # everything" behavior so we do not silently drop MCP for old users.
        return dict(catalog)

    needed: set = set()
    for p in plugin_entries:
        if installed and p.get("name") not in installed:
            continue
        mcp_list = p.get("mcp") or []
        if isinstance(mcp_list, list):
            for name in mcp_list:
                if isinstance(name, str):
                    needed.add(name)

    return {name: catalog[name] for name in sorted(needed) if name in catalog}


def _prompt_for_mcp_env_vars(needed_vars: set, existing: dict) -> dict:
    """Prompt the developer for any unset MCP env vars. Returns a dict of
    newly-collected values to persist. Values already present in `existing`
    (or in os.environ) are kept and not re-prompted.

    Non-interactive runs (e.g. `curl ... | python3` or scheduled auto-sync)
    fall back to defaults + os.environ; required-but-missing vars are warned
    about so the user knows to re-run with a TTY.
    """
    if not needed_vars:
        return {}

    interactive = sys.stdin and sys.stdin.isatty()
    collected: dict = {}
    missing_required: list = []

    for var in sorted(needed_vars):
        meta = _MCP_ENV_VAR_DEFAULTS.get(var, {})
        prompt_label = meta.get("prompt", var)
        default = meta.get("default", "")
        is_secret = meta.get("secret", False)

        # Existing value in Claude env block or process env wins — no re-prompt.
        current = existing.get(var) or os.environ.get(var)
        if current:
            continue

        if not interactive:
            if default:
                collected[var] = default
            else:
                missing_required.append(var)
            continue

        # Always show the actual default in brackets so the developer knows
        # what Enter will give them. For "secret" vars with factory defaults
        # (e.g. Supervisor/Supervisor for a local Creatio dev instance), the
        # default is not a real secret — surfacing it is more user-friendly
        # than hiding it behind `(unchanged)`. Real secrets (GHE_MCP_TOKEN)
        # have no default, so the prompt shows `leave blank to skip`.
        shown_default = default if default else "leave blank to skip"
        try:
            if is_secret:
                import getpass

                value = getpass.getpass(f"  {prompt_label} [{shown_default}]: ")
            else:
                value = input(f"  {prompt_label} [{shown_default}]: ")
        except (EOFError, KeyboardInterrupt):
            value = ""

        # Strip whitespace BEFORE the `or default` fallback. With the old
        # order `(value or default).strip()`, whitespace-only input like "  "
        # was truthy → kept the spaces → stripped to "" → the default never
        # applied. Stripping first means whitespace-only input collapses to
        # empty *before* the default fallback, so Supervisor/localhost/etc.
        # land on the var as the user expects when they hit Enter.
        value = value.strip() or default
        if value:
            collected[var] = value
        elif not default:
            missing_required.append(var)

    if missing_required:
        warn(
            "  MCP env vars left unset: "
            + ", ".join(missing_required)
            + " — the affected MCP servers will fail to start until you re-run "
            "install.py in a terminal (or set those vars in ~/.claude/settings.json "
            "`env` block)."
        )

    return collected


def _persist_claude_env_vars(target: dict, env_vars: dict) -> None:
    """Write collected MCP env vars into `~/.claude/settings.json` -> `env`.
    Claude Code substitutes these when spawning MCP processes, so `.mcp.json`
    placeholders like `${GHE_MCP_TOKEN}` get resolved at runtime.

    If `env` already exists but is the wrong shape (e.g. a string or array
    from a malformed hand-edit), we don't blow up the whole install — we warn
    and leave the malformed value untouched. The MCP servers needing those
    vars will fail to start until the user fixes their settings.json, but the
    rest of the install run continues.
    """
    if not env_vars:
        return
    settings = read_json(target["settings"]) or {}
    existing = settings.get("env")
    if existing is not None and not isinstance(existing, dict):
        warn(
            "  Claude Code: settings.json 'env' is not a mapping "
            f"(got {type(existing).__name__}); leaving as-is. Fix it by hand to enable "
            "MCP env-var substitution (GHE_MCP_TOKEN, Creatio creds, etc.)."
        )
        return
    env_block = existing or {}
    env_block.update(env_vars)
    settings["env"] = env_block
    write_json(target["settings"], settings)
    info(f"  Claude Code: persisted {len(env_vars)} MCP env var(s) to settings.json `env`")


# MCP provisioning preference, persisted in ~/.creatio-dev-skills/mcp-prefs.json.
# Two booleans drive whether and when configure_mcp prompts:
#   enabled     — master switch. --no-mcp sets it false; --configure-mcp sets
#                 it true. When false, configure_mcp does nothing.
#   provisioned — set true after the first successful provisioning. Gates the
#                 first-time interactive prompt: once provisioned, normal
#                 install/update/auto-sync runs stay silent. Re-prompt only
#                 via --configure-mcp.
def _mcp_prefs_path() -> Path:
    return Path.home() / ".creatio-dev-skills" / "mcp-prefs.json"


def _read_mcp_prefs() -> dict:
    prefs = read_json(_mcp_prefs_path())
    if not isinstance(prefs, dict):
        return {"enabled": True, "provisioned": False}
    prefs.setdefault("enabled", True)
    prefs.setdefault("provisioned", False)
    return prefs


def _write_mcp_prefs(prefs: dict) -> None:
    write_json(_mcp_prefs_path(), prefs)


# Env vars consumed by an MCP server through an HTTP header rather than a
# spawned stdio subprocess. Claude Code reliably expands ${VAR} from the
# settings.json `env` block into stdio server command/args (so the Creatio
# creds resolve), but HTTP-header expansion is not guaranteed — so for these
# we ALSO write the value into the real OS user environment.
_GLOBAL_ENV_VARS = {"GHE_MCP_TOKEN"}


def _unix_shell_rc() -> Path:
    """Pick the shell rc file to persist exports into, based on $SHELL."""
    shell = os.environ.get("SHELL", "")
    home = Path.home()
    if shell.endswith("zsh"):
        return home / ".zshrc"
    if shell.endswith("bash"):
        return home / ".bashrc"
    return home / ".profile"


def _persist_unix_shell_env(name: str, value: str) -> bool:
    """Append (or refresh) an `export NAME="value"` line in the user's shell rc.
    Idempotent: a prior line we wrote for the same var is replaced, not
    duplicated. Best-effort — never raises.
    """
    rc = _unix_shell_rc()
    marker = f"# creatio-marketplace env: {name}"
    line = f'export {name}="{value}"  {marker}'
    try:
        existing = rc.read_text(encoding="utf-8").splitlines() if rc.exists() else []
    except OSError:
        existing = []
    kept = [ln for ln in existing if marker not in ln]
    kept.append(line)
    try:
        rc.parent.mkdir(parents=True, exist_ok=True)
        rc.write_text("\n".join(kept) + "\n", encoding="utf-8")
        os.environ[name] = value
        info(f"  {name}: added to {rc.name} — open a new shell or `source` it to load.")
        return True
    except OSError as exc:
        warn(f"  Could not write {name} to {rc}: {exc}")
        return False


def _persist_global_env_var(name: str, value: str) -> bool:
    """Best-effort persist a user-level OS environment variable so HTTP-header
    `${VAR}` expansion resolves regardless of how Claude Code reads it, and the
    value survives restarts. Returns True if it (likely) wrote. Never raises —
    a failure here must not abort the install.

    Windows uses `setx` (writes HKCU\\Environment for FUTURE processes — the
    current shell is unaffected, which is expected). macOS/Linux append an
    export to the shell rc.
    """
    if not value:
        return False
    # Already current in this process env → assume the OS env is set; skip the
    # write to avoid churn on every run (keeps updates quiet).
    if os.environ.get(name) == value:
        return False

    if platform.system() == "Windows":
        import subprocess as sp
        try:
            res = sp.run(["setx", name, value], capture_output=True, text=True)
        except Exception as exc:
            warn(f"  Could not set {name} globally via setx: {exc}")
            return False
        if res.returncode == 0:
            os.environ[name] = value  # reflect in-process so we don't re-run
            info(f"  {name}: set as a user environment variable (new terminals/apps will see it).")
            return True
        warn(f"  setx failed for {name} (exit {res.returncode}); set it manually if the GHE MCP can't authenticate.")
        return False

    return _persist_unix_shell_env(name, value)


def _sync_managed_mcp_servers(
    target: dict, needed_servers: dict, catalog: dict, prefs: dict
) -> None:
    """Mirror the deduplicated MCP server set into `~/.claude/settings.json`
    -> `mcpServers` at user scope, so duplicate per-plugin copies collapse.

    Claude Code's scope precedence is local > project > user > plugin: a server
    defined here (user scope) shadows every per-plugin `.mcp.json` entry with
    the same name, and Claude Code then connects to it exactly once instead of
    spawning one instance per declaring plugin. That is what removes the
    cross-plugin duplicate registrations when several Creatio plugins are
    installed together, while each plugin's own `.mcp.json` still keeps a
    standalone (`/plugin`) install self-contained. Those per-plugin `.mcp.json`
    copies are intentionally left on disk and only shadowed (not deleted), so a
    later bare `/plugin` install that bypasses this installer remains usable.

    Ownership is tracked in prefs `managedMcpServers`, a map of each server name
    we wrote to the exact config we last wrote for it. Storing the config (not
    just the name) lets us tell our own untouched entry apart from one the user
    hand-edited, so we never clobber or prune a user's customization:

      - a name absent from `mcpServers` is created and taken into ownership;
      - a name we own whose on-disk value still equals what we wrote is
        refreshed when the catalog config changes (and ownership is kept);
      - a name we own that the user has since edited is left untouched and we
        drop our ownership claim, so it is never reverted or later pruned;
      - a name present but never ours (user-defined) is never touched or claimed.

    Servers we own that are no longer needed are pruned, again only when the
    on-disk value still equals what we wrote.
    """
    settings = read_json(target["settings"]) or {}
    raw = settings.get("mcpServers")
    if raw is not None and not isinstance(raw, dict):
        warn(
            "  Claude Code: settings.json 'mcpServers' is not a mapping "
            f"(got {type(raw).__name__}); skipping MCP dedup sync. Fix it by hand."
        )
        return
    servers = raw or {}

    # Resolve the Claude variant of each needed server from the registry catalog.
    desired: dict = {}
    for name in needed_servers:
        entry = catalog.get(name)
        claude_cfg = entry.get("claude") if isinstance(entry, dict) else None
        if isinstance(claude_cfg, dict):
            desired[name] = claude_cfg

    # `managedMcpServers` maps owned name -> config we last wrote. Migrate the
    # legacy list-of-names form by adopting the current on-disk value as the
    # baseline (the prior installer wrote the catalog config verbatim, so the
    # on-disk value is what we would have recorded).
    managed_raw = prefs.get("managedMcpServers")
    if isinstance(managed_raw, dict):
        managed = managed_raw
    elif isinstance(managed_raw, list):
        managed = {
            n: servers.get(n)
            for n in managed_raw
            if isinstance(n, str) and n in servers
        }
    else:
        managed = {}

    new_managed: dict = {}
    changed = False

    for name, cfg in desired.items():
        if name not in servers:
            # Genuinely new — create it and take ownership.
            servers[name] = json.loads(json.dumps(cfg))
            new_managed[name] = json.loads(json.dumps(cfg))
            changed = True
        elif name in managed and servers[name] == managed[name]:
            # Ours and untouched since we wrote it — refresh on catalog change,
            # keep ownership.
            if servers[name] != cfg:
                servers[name] = json.loads(json.dumps(cfg))
                changed = True
            new_managed[name] = json.loads(json.dumps(servers[name]))
        # else: present but not ours (user-defined), or ours-but-user-edited —
        # leave untouched and do not claim ownership, so we never clobber it.

    # Prune servers we own that are no longer needed, only when still untouched.
    for name, written in managed.items():
        if name in desired:
            continue
        if servers.get(name) == written:
            servers.pop(name, None)
            changed = True
        # else: user edited a now-unneeded managed server — leave it; ownership
        # is dropped by virtue of not being copied into new_managed.

    if changed:
        settings["mcpServers"] = servers
        write_json(target["settings"], settings)
        info(
            f"  Claude Code: synced {len(new_managed)} MCP server(s) to settings.json "
            "`mcpServers` (deduplicated across plugins)"
        )

    if managed_raw != new_managed:
        prefs["managedMcpServers"] = new_managed
        _write_mcp_prefs(prefs)


def configure_mcp(
    targets: dict,
    registry: dict,
    state: Optional[dict] = None,
    configure: bool = False,
    disable: bool = False,
) -> None:
    """Provision the env vars the installed plugins' MCP servers need, and
    deduplicate the servers themselves across plugins.

    Each plugin still ships its own `.mcp.json` (generated by
    `build/generate-manifests.js` from its `mcp:` list) so a standalone
    `/plugin` install is self-contained. But when several plugins declare the
    same server, Claude Code would register one instance per plugin. To collapse
    those duplicates, `_sync_managed_mcp_servers` writes the union of servers the
    installed plugins need into `~/.claude/settings.json` `mcpServers` at user
    scope, which outranks plugin scope and shadows every per-plugin copy to a
    single connection.

    What the installer owns: the `${VAR}` placeholders (GHE token, Creatio
    creds) those `.mcp.json` files reference. Claude Code injects
    `settings.json` `env` into the MCP subprocess environment, so the
    placeholders resolve at server-spawn time.

    Prompt gating (silent updates):
      - `disable=True`  (--no-mcp): record the preference and skip entirely.
      - `configure=True` (--configure-mcp): re-enable and prompt now (on-demand
        reconfigure — change a token, set up after a silent install).
      - otherwise: prompt ONLY on the very first install (prefs.provisioned is
        false) and only when interactive. Every later run — manual re-run,
        plugin update, or the 6-hour auto-sync tick — provisions silently:
        already-set vars stay, vars with defaults are applied without asking,
        and vars without a default (GHE token) are left unset without nagging.
    """
    if "claude" not in targets:
        return

    prefs = _read_mcp_prefs()

    if disable:
        if prefs.get("enabled", True):
            prefs["enabled"] = False
            _write_mcp_prefs(prefs)
            info("  MCP provisioning disabled (re-enable with --configure-mcp).")
        return

    if configure and not prefs.get("enabled", True):
        # --configure-mcp implies (re-)enable. Persist immediately: the early
        # returns below (no needed servers / no placeholders) would otherwise
        # exit before the end-of-function write, silently dropping the
        # re-enable. A later MCP-plugin install / auto-sync would then stay
        # disabled despite the explicit --configure-mcp.
        prefs["enabled"] = True
        _write_mcp_prefs(prefs)

    if not prefs.get("enabled", True):
        # Previously disabled via --no-mcp and not re-enabled this run.
        return

    needed_servers = _select_required_mcp_servers(registry, state or {})

    target = targets["claude"]

    # Deduplicate the servers across plugins at user scope. Runs even when
    # `needed_servers` is empty so removing the last MCP plugin still prunes the
    # servers we previously managed.
    _sync_managed_mcp_servers(
        target, needed_servers, registry.get("mcp_servers", {}), prefs
    )

    if not needed_servers:
        return

    # settings.json["env"] should be a mapping. If the user hand-edited it to a
    # string or list, we cannot read it like a dict — coerce to {} and let
    # `_persist_claude_env_vars` issue the user-facing warning later. Without
    # this guard, `_prompt_for_mcp_env_vars` would crash at `.get(var)` before
    # any warning could be shown.
    raw_env = (read_json(target["settings"]) or {}).get("env")
    existing_env = raw_env if isinstance(raw_env, dict) else {}
    needed_vars: set = set()
    for server in needed_servers.values():
        needed_vars |= _collect_env_placeholders(server.get("claude") or server)
    if not needed_vars:
        return

    # Decide whether this run may prompt. Explicit --configure-mcp always may;
    # otherwise only the first-ever provisioning may. _prompt_for_mcp_env_vars
    # itself still no-ops on a non-interactive stdin, so a first-time auto-sync
    # tick won't block — it just applies defaults silently.
    allow_prompt = configure or not prefs.get("provisioned", False)
    if allow_prompt:
        collected = _prompt_for_mcp_env_vars(needed_vars, existing_env)
    else:
        # Silent run: never prompt. Apply defaults for any still-unset var that
        # has one (so Creatio creds land); leave the rest untouched.
        collected = _silent_mcp_env_defaults(needed_vars, existing_env)

    if collected:
        _persist_claude_env_vars(target, collected)

    # Mirror HTTP-header credentials into the real OS user environment too.
    # `ghe` is an HTTP MCP server whose Authorization header is `Bearer
    # ${GHE_MCP_TOKEN}`; Claude Code reliably expands ${VAR} from the
    # settings.json `env` block into stdio server args (so the Creatio creds
    # work), but HTTP-header expansion is not guaranteed. Persisting the token
    # to the OS user environment guarantees it resolves however Claude reads
    # it, and survives restarts / other tools. Take the value from this run's
    # input or whatever is already configured.
    for var in _GLOBAL_ENV_VARS:
        if var not in needed_vars:
            continue
        value = collected.get(var) or existing_env.get(var) or os.environ.get(var)
        if value:
            _persist_global_env_var(var, value)

    # Mark provisioned once we've gone through a real provisioning pass (prompt
    # or silent-default), so future runs stay quiet unless --configure-mcp.
    if not prefs.get("provisioned", False):
        prefs["provisioned"] = True
    _write_mcp_prefs(prefs)


def _silent_mcp_env_defaults(needed_vars: set, existing: dict) -> dict:
    """Non-prompting counterpart to `_prompt_for_mcp_env_vars`: for each needed
    var that is not already set (in settings env or the process env), apply its
    factory default when it has one. Vars without a default (e.g. GHE token)
    are skipped silently — a silent update must never nag.
    """
    collected: dict = {}
    for var in sorted(needed_vars):
        if existing.get(var) or os.environ.get(var):
            continue
        default = _MCP_ENV_VAR_DEFAULTS.get(var, {}).get("default", "")
        if default:
            collected[var] = default
    return collected


# ---- SHA256 verification ----


def verify_sha256(data: bytes, expected: Optional[str] = None) -> bool:
    if not expected:
        warn("SHA256 checksum not provided — integrity not verified")
        return True
    actual = hashlib.sha256(data).hexdigest()
    return actual == expected


# ---- Local state ----


def _state_path() -> Path:
    return Path.home() / ".creatio-dev-skills" / "installed-plugins.json"


def _now() -> str:
    from datetime import datetime, timezone

    return datetime.now(timezone.utc).isoformat()


def read_state() -> dict:
    return read_json(_state_path()) or {"version": 1, "lastPoll": None, "plugins": {}}


def write_state(state: dict) -> None:
    write_json(_state_path(), state)


# ---- Install profile (selective vs bulk) ----
#
# The installer remembers what the operator asked for last time so that the
# cron-driven self-update preserves their intent instead of defaulting to a
# full bulk install on every tick.
#
# Semantics:
#   * Selective (`--plugin X [--plugin Y]`) → cron updates ONLY those plugins.
#     New plugins added to the registry are NOT auto-installed.
#   * Bulk (`--all`, or no `--plugin` and no profile) → cron updates the full
#     non-beta set. New plugins added to the registry ARE auto-installed.
#   * Legacy (no profile file yet) → bulk fallback. First successful run
#     persists the profile, so subsequent cron ticks honor it.

PROFILE_VERSION = 1


def _profile_path() -> Path:
    return _state_path().parent / "profile.json"


def load_profile() -> Optional[dict]:
    """Load and validate the persisted install profile.

    Returns the dict on success or None when no profile exists or the file
    is corrupt / has an unknown schema. Validation failure is non-fatal:
    the run continues with bulk fallback so a broken profile cannot brick
    the cron loop.
    """
    raw = read_json(_profile_path())
    if raw is None:
        return None
    if not isinstance(raw, dict):
        warn(f"Profile {_profile_path()} is not a JSON object; ignoring")
        return None
    if raw.get("version") != PROFILE_VERSION:
        warn(
            f"Profile {_profile_path()} has unknown version "
            f"{raw.get('version')!r}; ignoring"
        )
        return None
    if raw.get("mode") not in ("bulk", "selective"):
        warn(
            f"Profile {_profile_path()} has invalid mode "
            f"{raw.get('mode')!r}; ignoring"
        )
        return None
    return raw


def save_profile(
    mode: str,
    plugins: List[str],
    allow_beta: bool,
) -> None:
    """Write the install profile atomically. Best-effort: a failed write
    only warns — the install itself already succeeded by this point.

    ``targets`` is no longer accepted as a CLI knob (Claude-only since the
    2026-05-27 sync), but the field is preserved in the persisted JSON as
    an empty list so a downgrade or external tool that reads profile.json
    still sees the expected schema shape.
    """
    payload = {
        "version": PROFILE_VERSION,
        "mode": mode,
        "plugins": list(plugins),
        "targets": [],
        "allow_beta": bool(allow_beta),
        "updated_at": _now(),
    }
    try:
        write_json(_profile_path(), payload)
    except OSError as exc:
        warn(f"Failed to persist install profile: {exc}")


LEGACY_BULK_MIN_PLUGINS = 2


def _looks_like_legacy_bulk_install() -> bool:
    """True when the installed-plugins state suggests a pre-profile bulk run.

    Pre-profile-system installs persisted every successfully installed
    plugin into ``installed-plugins.json`` but never wrote
    ``profile.json``. A modern dev who genuinely went selective always
    has ``profile.json`` because we save it at the end of every
    successful run. So an absent profile combined with two or more
    installed plugins is a strong signal that the dev was historically
    on bulk and should keep that contract — the
    ``LEGACY_BULK_MIN_PLUGINS`` threshold avoids the rare edge of "fresh
    install of a single plugin" being misread as legacy.

    Safe to call even when the state file does not exist; ``read_state``
    returns an empty default in that case so the helper still says
    ``False`` and the first-time-install path is untouched.
    """
    try:
        state = read_state()
    except Exception:
        # State read errors must not block install resolution; fall back
        # to the conservative "no legacy hint" answer so the caller
        # treats the user as a fresh install.
        return False
    plugins = state.get("plugins") or {}
    if not isinstance(plugins, dict):
        return False
    return len(plugins) >= LEGACY_BULK_MIN_PLUGINS


def _cli_changes_mode(cli: "argparse.Namespace") -> bool:
    """True when the user picked the install **mode** explicitly this run.

    Only ``--plugin`` and ``--all`` change the mode (selective vs bulk).
    ``--allow-beta`` is a field-level knob that can be combined with a
    saved profile — passing it alone must NOT reset a selective profile
    to bulk. A bare ``install.py`` (cron) returns False; everything is
    hydrated from the profile.
    """
    return bool(cli.plugin) or cli.all


def resolve_effective_cli(
    cli: "argparse.Namespace",
) -> "tuple[argparse.Namespace, str]":
    """Merge the saved profile into ``cli`` so the run reflects user intent.

    Rules:
      * ``--plugin`` / ``--all`` overrides the profile's *mode*. Fields
        the user did not repeat on the same command (``--allow-beta``)
        are merged in from the saved profile and re-persisted that way —
        same "field-level knob" rule as the bare path. The persisted
        plugin list is not merged because ``--plugin`` and ``--all`` are
        themselves the mode signal.
      * EXCEPTION — bulk preservation: when the prior state is bulk
        (either an explicit ``profile.mode == "bulk"`` or the
        ``installed-plugins.json`` legacy fingerprint from a pre-profile
        install) and the user passes ``--plugin X`` without ``--all``,
        we install ``X`` *this run* but persist the profile as bulk so
        the next cron tick still refreshes the full pack. This shields
        developers who already opted into the full pack (or who were on
        it before the profile system existed) from a footgun where a
        casual ``--plugin foo`` call would quietly demote them to a
        selective subscription that stops receiving new plugins.
        Communicated to the caller via ``cli.persisted_mode_override``
        so the save_profile call site can honour it.
      * ``--allow-beta`` alone is *merged* on top of the profile, so a
        user can keep their selective set and just say "this run, allow
        beta" without losing the pin.
      * Bare CLI + profile → hydrate everything from the profile.
      * Bare CLI + no profile → bulk default (legacy behavior).

    Returns ``(effective_cli, log_message)``. The caller logs the
    message so operators can see at a glance which path was taken.
    """
    profile = load_profile()
    mode_explicit = _cli_changes_mode(cli)
    # Default: caller respects whatever mode the run resolves to.
    cli.persisted_mode_override = None
    # Devs who installed before the profile system existed have no
    # profile.json on disk but they were effectively on bulk (every old
    # `install.py` invocation installed every plugin). We detect that
    # legacy bulk state from the installed-plugins state file so that a
    # `--plugin X` request from such a dev does not silently demote them
    # to a one-plugin subscription either.
    inferred_bulk = profile is None and _looks_like_legacy_bulk_install()
    prior_was_bulk = (profile is not None and profile.get("mode") == "bulk") or inferred_bulk
    if mode_explicit:
        # User asked for an exact new shape this run. We still merge
        # allow-beta with the profile only when the user did NOT repeat
        # it — same "field-level knob" rule as the bare path.
        if profile is not None:
            if not cli.allow_beta:
                cli.allow_beta = bool(profile.get("allow_beta"))
        if prior_was_bulk and cli.plugin and not cli.all:
            # Bulk preservation path. Install just the named plugin
            # this run, but instruct the save_profile caller to keep
            # (or write, for the legacy case) a bulk profile so the
            # cron tick continues to refresh every plugin (including
            # newcomers).
            cli.persisted_mode_override = "bulk"
            names = ", ".join(cli.plugin)
            source = "legacy bulk install detected" if inferred_bulk else "bulk profile preserved"
            return cli, (
                f"Profile: {source} — installing {names} this run; "
                f"full pack continues to auto-update on the next cron tick"
            )
        if profile is not None:
            return cli, "Profile: mode overridden by CLI; allow-beta merged"
        return cli, ""
    if profile is None:
        # Bare CLI may still carry --allow-beta; it lives on the bulk
        # default in that case.
        return cli, (
            "Profile: none → bulk default "
            "(pass --plugin <name> to switch to selective)"
        )
    if profile["mode"] == "bulk":
        # Keep CLI-provided allow-beta if it was passed; fill the rest
        # from the profile. cli.plugin stays empty → bulk.
        if not cli.allow_beta:
            cli.allow_beta = bool(profile.get("allow_beta"))
        msg = "Profile: bulk → installing full non-beta set"
        if cli.allow_beta:
            msg += " (allow_beta=yes)"
        return cli, msg
    # selective
    cli.plugin = list(profile.get("plugins") or [])
    if not cli.allow_beta:
        cli.allow_beta = bool(profile.get("allow_beta"))
    return cli, (
        f"Profile: selective {cli.plugin} → updating only persisted set "
        f"(pass --all to switch to bulk)"
    )


# ---- Installer self-update ----

SELF_UPDATE_SKIP_ENV = "CREATIO_SKIP_SELF_UPDATE"
SELF_UPDATE_GUARD_ENV = "CREATIO_SELF_UPDATE_RAN"
SELF_UPDATE_TIMEOUT = 15
SELF_UPDATE_MIN_BYTES = 10_240
SELF_UPDATE_SHEBANG = b"#!/usr/bin/env python3"
INSTALL_PY_URL = os.environ.get(
    "CREATIO_INSTALL_PY_URL",
    f"{BASE_URL.rsplit('/marketplace', 1)[0]}/install.py",
)


class _HttpsOnlyRedirectHandler(urllib.request.HTTPRedirectHandler):
    """Reject any 3xx redirect that would leave https://."""

    def redirect_request(self, req, fp, code, msg, headers, newurl):
        scheme = urllib.parse.urlsplit(newurl).scheme.lower()
        if scheme != "https":
            raise urllib.error.URLError(
                f"refusing non-https redirect to {newurl!r}"
            )
        return super().redirect_request(req, fp, code, msg, headers, newurl)


def _https_only_fetch(url: str, timeout: int) -> bytes:
    """Fetch URL enforcing https:// for the initial request and every redirect."""
    if urllib.parse.urlsplit(url).scheme.lower() != "https":
        raise urllib.error.URLError(
            f"non-https self-update URL rejected: {url!r}"
        )
    opener = urllib.request.build_opener(_HttpsOnlyRedirectHandler())
    req = urllib.request.Request(
        url, headers={"User-Agent": "creatio-marketplace-installer/1.0"}
    )
    with opener.open(req, timeout=timeout) as resp:
        return resp.read()


def _payload_looks_like_install_py(data: bytes) -> bool:
    """Cheap structural check that remote bytes resemble our published install.py.

    Defense-in-depth on top of the registry SHA pin: catches accidental WAF
    block pages, captive portals, login redirects, and truncated proxy
    responses if the integrity pin somehow lined up by coincidence.
    """
    if len(data) < SELF_UPDATE_MIN_BYTES:
        return False
    if not data.startswith(SELF_UPDATE_SHEBANG):
        return False
    return True


def self_update_if_needed() -> None:
    """Refresh install.py from the canonical URL and re-exec if it changed.

    Trust model:
      1. fetch registry.json — provides an out-of-band SHA256 pin.
      2. fetch install.py over strict https-only (no redirects to http).
      3. verify SHA256 against the registry pin — fail-closed.
      4. structural sanity-check + Python parse-check.
      5. backup current script, atomic-replace, re-exec.
      6. on re-exec failure, restore from backup.

    Fail-open on any earlier failure (network, IO, permissions, missing pin):
    keep the current version running so a transient problem does not break
    cron forever. Quiet on the steady-state same-SHA path.
    """
    if os.environ.get(SELF_UPDATE_SKIP_ENV) == "1":
        return
    if os.environ.get(SELF_UPDATE_GUARD_ENV) == "1":
        # We re-exec'd already in this generation; do not re-enter to avoid loops.
        return
    # Piped from stdin (first-time `curl ... | python3`): nothing to update on disk.
    if not __file__ or not os.path.isfile(__file__):
        return

    script_path = os.path.abspath(__file__)

    # 1. Fetch trusted SHA pin from registry.
    try:
        registry = fetch_json(REGISTRY_URL)
    except (urllib.error.URLError, OSError, json.JSONDecodeError) as exc:
        warn(f"Self-update: cannot fetch registry pin ({exc}), keeping current version")
        return
    expected_sha = registry.get("installer_sha256") if isinstance(registry, dict) else None
    if not expected_sha:
        warn(
            "Self-update: registry has no installer_sha256 field; "
            "keeping current version (server may be older than installer)"
        )
        return

    # 2. Fetch install.py with strict https-only policy.
    try:
        remote_bytes = _https_only_fetch(INSTALL_PY_URL, SELF_UPDATE_TIMEOUT)
    except (urllib.error.URLError, OSError) as exc:
        warn(f"Self-update check failed, continuing with current version: {exc}")
        return

    # 3. Verify integrity against the registry pin (fail-closed).
    actual_sha = hashlib.sha256(remote_bytes).hexdigest()
    if actual_sha != expected_sha:
        warn(
            f"Self-update: SHA pin mismatch "
            f"(registry expects {expected_sha[:8]}, got {actual_sha[:8]}); "
            f"keeping current version"
        )
        return

    # 4. Compare against local — quick exit if we are already on the pinned version.
    try:
        local_sha = hashlib.sha256(Path(script_path).read_bytes()).hexdigest()
    except OSError as exc:
        warn(f"Self-update: cannot read local script ({exc}), continuing")
        return
    if actual_sha == local_sha:
        return

    # 5. Defense-in-depth structural sanity (should always pass given the SHA pin).
    if not _payload_looks_like_install_py(remote_bytes):
        warn(
            f"Self-update: rejected remote payload despite matching pin "
            f"(size={len(remote_bytes)} bytes, prefix={remote_bytes[:16]!r}); "
            f"keeping current version"
        )
        return

    # 6. Validate that the bytes parse as Python (cheap, no execution).
    try:
        compile(remote_bytes, script_path, "exec")
    except (SyntaxError, ValueError) as exc:
        warn(f"Self-update: candidate failed to parse as Python ({exc}), keeping current version")
        return

    # 7. Backup current script, write new bytes, atomic replace.
    import shutil

    backup_path = Path(script_path + ".bak")
    try:
        shutil.copy2(script_path, backup_path)
    except OSError as exc:
        warn(f"Self-update: failed to create backup ({exc}), aborting update")
        return

    new_path = Path(script_path + ".new")
    try:
        new_path.write_bytes(remote_bytes)
        os.replace(new_path, script_path)
    except OSError as exc:
        warn(f"Self-update: failed to write new install.py ({exc}), continuing with old version")
        try:
            new_path.unlink(missing_ok=True)
        except OSError:
            pass
        backup_path.unlink(missing_ok=True)
        return

    info(f"Self-update applied (sha {local_sha[:8]} -> {actual_sha[:8]}); re-executing")

    # 8. Re-exec with rollback on failure.
    new_env = os.environ.copy()
    new_env[SELF_UPDATE_GUARD_ENV] = "1"
    try:
        os.execvpe(sys.executable, [sys.executable, script_path, *sys.argv[1:]], new_env)
    except OSError as exc:
        warn(f"Self-update: re-exec failed ({exc}), rolling back to previous version")
        try:
            os.replace(backup_path, script_path)
        except OSError as rollback_exc:
            warn(
                f"Self-update: rollback failed ({rollback_exc}); "
                f"install.py may be in inconsistent state — re-run the curl installer"
            )
        # Fall through — let the still-running old in-memory logic finish this tick.
        return


# ---- Auto-update scheduler ----

CRON_MARKER = "# creatio-marketplace-auto-update"
CRON_EXPRESSION = "0 */6 * * *"
TASK_NAME = "CreatioMarketplaceAutoUpdate"


def configure_scheduler(script_path: str) -> None:
    """Set up periodic auto-update via cron (Unix) or Task Scheduler (Windows)."""
    system = platform.system()
    if system == "Windows":
        _configure_windows_scheduler(script_path)
    else:
        _configure_unix_scheduler(script_path)


def _extract_cron_script_path(cron_line: str) -> Optional[str]:
    """Return the install.py path from a marker'd cron line, or None if unparseable.

    Uses shlex.split so quoted paths containing whitespace round-trip correctly
    (we now write the cron line with shlex.quote on every component).
    """
    import shlex

    try:
        tokens = shlex.split(cron_line, comments=False)
    except ValueError:
        return None
    for token in tokens:
        if token.endswith("install.py"):
            return token
    return None


def _parse_schtasks_run_target(query_stdout: str) -> Optional[str]:
    """Extract 'Task To Run' value from `schtasks /Query /V /FO LIST` output."""
    for line in query_stdout.splitlines():
        if ":" not in line:
            continue
        key, _, val = line.partition(":")
        if key.strip().lower() == "task to run":
            return val.strip()
    return None


def _extract_schtasks_script_path(run_target: str) -> Optional[str]:
    """Return install.py path from a Task To Run command, or None if unparseable.

    schtasks echoes the stored Task To Run with its own normalization
    (path casing, 8.3 form, internal whitespace), so we compare extracted
    path tokens rather than full shell strings to keep idempotency on Windows.
    """
    import shlex

    try:
        tokens = shlex.split(run_target, posix=False)
    except ValueError:
        return None
    for token in tokens:
        stripped = token.strip('"')
        if stripped.lower().endswith("install.py"):
            return stripped
    return None


def _extract_schtasks_interpreter(run_target: str) -> Optional[str]:
    """Return the interpreter (first token) from a Task To Run command, or None.

    Used to detect a python.exe -> pythonw.exe switch so existing scheduled
    tasks get reconciled to the windowless interpreter instead of being left
    flashing a console window on every auto-update tick.
    """
    import shlex

    try:
        tokens = shlex.split(run_target, posix=False)
    except ValueError:
        return None
    return tokens[0].strip('"') if tokens else None


def _paths_equivalent(a: Optional[str], b: Optional[str]) -> bool:
    """Compare two filesystem paths after normcase + normpath.

    On Windows this collapses case differences and short-8.3 quirks; on
    POSIX it normalizes redundant separators. Either-side None returns False.
    """
    if not a or not b:
        return False
    return os.path.normcase(os.path.normpath(a)) == os.path.normcase(os.path.normpath(b))


def _find_prior_mailto(lines: List[str]) -> Optional[str]:
    """Returns the last active MAILTO= line (if any) to restore after our cron entry.

    Skips comment lines (stripped lines that start with #) so a commented-out
    MAILTO directive does not shadow a real one or produce a spurious restore line.
    """
    for line in reversed(lines):
        stripped = line.strip()
        if not stripped.startswith("#") and stripped.startswith("MAILTO="):
            return stripped
    return None


def _configure_unix_scheduler(script_path: str) -> None:
    import shlex
    import shutil
    import subprocess as sp

    crontab = shutil.which("crontab")
    if not crontab:
        warn("crontab not found — auto-update not configured")
        return

    # Read current crontab. Use splitlines() (not strip().split("\n")) so empty
    # stdout produces [] rather than [""] — the latter would inject a leading
    # blank line into the rewritten crontab.
    result = sp.run([crontab, "-l"], capture_output=True, text=True)
    lines = result.stdout.splitlines() if result.returncode == 0 else []

    marker_lines = [ln for ln in lines if CRON_MARKER in ln]
    other_lines = [ln for ln in lines if CRON_MARKER not in ln]

    # Idempotent fast path: exactly one marker line, target script_path matches
    # after path normalization, AND the entry is already MAILTO-protected.
    # If the path matches but MAILTO="" is missing, fall through to heal it.
    if len(marker_lines) == 1:
        existing = _extract_cron_script_path(marker_lines[0])
        if _paths_equivalent(existing, script_path):
            marker_idx = next(i for i, ln in enumerate(lines) if CRON_MARKER in ln)
            already_protected = (
                marker_idx > 0 and lines[marker_idx - 1].strip() == 'MAILTO=""'
            )
            if already_protected:
                info("Auto-update scheduler: already configured")
                return
            # Fall through to the reconcile block below, which will log after write.

    # Reconcile: drop every existing marker line, write a fresh one. Covers
    # missing entry, stale path, accidental duplicates, and upgrade healing.
    # shlex.quote each component so spaces/metachars in $HOME or python path
    # cannot break /bin/sh -c parsing when cron runs the line.
    python = shutil.which("python3") or sys.executable
    log_path = str(_state_path().parent / "auto-update.log")
    new_line = (
        f"{CRON_EXPRESSION} {shlex.quote(python)} {shlex.quote(script_path)} "
        f">>{shlex.quote(log_path)} 2>&1 {CRON_MARKER}"
    )
    prior_mailto = _find_prior_mailto(other_lines)
    # Do NOT strip MAILTO lines from other_lines: they are positional and govern
    # the user's own jobs above our managed entry.  We insert MAILTO="" only
    # immediately before our cron line, then restore the prior value after it so
    # any jobs the user adds below us inherit the correct setting.
    # Guard: skip the restore when prior is already MAILTO="" — our inserted
    # directive already covers it and a second copy adds noise.
    new_entries = ['MAILTO=""', new_line]
    if prior_mailto and prior_mailto != 'MAILTO=""':
        new_entries.append(prior_mailto)
    new_crontab = other_lines + new_entries

    import tempfile

    with tempfile.NamedTemporaryFile(mode="w", suffix=".crontab", delete=False) as tmp:
        tmp.write("\n".join(new_crontab) + "\n")
        tmp_path = tmp.name

    try:
        sp.run([crontab, tmp_path], check=True, capture_output=True)
        if not marker_lines:
            info(f"Auto-update scheduler: cron every 6 hours ({CRON_EXPRESSION})")
        elif len(marker_lines) > 1:
            info(
                f"Auto-update scheduler: collapsed {len(marker_lines)} duplicate cron entries "
                f"and reconciled to {script_path}"
            )
        elif _paths_equivalent(_extract_cron_script_path(marker_lines[0]), script_path):
            # Heal case: path was already correct, only MAILTO protection was missing.
            info('Auto-update scheduler: healed MAILTO="" protection on existing cron entry')
        else:
            stale = _extract_cron_script_path(marker_lines[0]) or "<unparseable>"
            info(
                f"Auto-update scheduler: reconciled cron entry "
                f"({stale} -> {script_path})"
            )
    except sp.CalledProcessError:
        warn("Failed to configure crontab")
    finally:
        os.unlink(tmp_path)


def _configure_windows_scheduler(script_path: str) -> None:
    import subprocess as sp

    # Prefer pythonw.exe: it is the GUI-subsystem interpreter and runs without a
    # console window, so the 6-hourly auto-update tick stays silent instead of
    # flashing a cmd window that steals keyboard focus. Fall back to the regular
    # (console) interpreter only if the windowless one is missing.
    python = sys.executable
    pythonw = Path(python).with_name("pythonw.exe")
    if pythonw.exists():
        python = str(pythonw)
    cmd = f'"{python}" "{script_path}"'

    # Query existing task with verbose list output so we can compare the
    # registered Task To Run against our target — by extracted script_path
    # token, not by full shell string. schtasks echoes the stored Task To Run
    # back with its own normalization (case, 8.3 form, whitespace), so a raw
    # string compare would oscillate (delete + create on every install) instead
    # of no-op'ing, breaking AC5 idempotency.
    query = sp.run(
        ["schtasks", "/Query", "/TN", TASK_NAME, "/V", "/FO", "LIST"],
        capture_output=True,
        text=True,
    )
    if query.returncode == 0:
        existing_target = _parse_schtasks_run_target(query.stdout)
        existing_path = _extract_schtasks_script_path(existing_target or "")
        existing_interp = _extract_schtasks_interpreter(existing_target or "")
        # Reconcile not only on a stale script path but also on a stale
        # interpreter — an older task registered with python.exe must be
        # rewritten to the windowless pythonw.exe so it stops flashing a
        # console window every 6 hours.
        if _paths_equivalent(existing_path, script_path) and _paths_equivalent(
            existing_interp, python
        ):
            info("Auto-update scheduler: already configured")
            return
        # Stale or mismatched task — delete so the create below replaces it
        # cleanly. check=True so a silent delete failure does not leave the
        # stale task in place while we log "reconciled".
        try:
            sp.run(
                ["schtasks", "/Delete", "/TN", TASK_NAME, "/F"],
                check=True,
                capture_output=True,
                text=True,
            )
        except sp.CalledProcessError as exc:
            stderr = (exc.stderr or "").strip() or str(exc)
            warn(f"Failed to delete stale Task Scheduler entry: {stderr}")
            return
        reconciled = True
    else:
        reconciled = False

    try:
        sp.run(
            [
                "schtasks",
                "/Create",
                "/TN",
                TASK_NAME,
                "/SC",
                "HOURLY",
                "/MO",
                "6",
                "/TR",
                cmd,
                "/F",
            ],
            check=True,
            capture_output=True,
        )
        if reconciled:
            info(f"Auto-update scheduler: reconciled Task Scheduler entry to {script_path}")
        else:
            info(f"Auto-update scheduler: Task Scheduler every 6 hours")
    except (sp.CalledProcessError, OSError):
        warn("Failed to configure Task Scheduler")


def _cleanup_stale_self_update_artifacts() -> None:
    """Remove leftover install.py.bak and install.py.* tempfiles in the state dir.

    Two sources:
      - self_update_if_needed leaves install.py.bak / install.py.new on a failed
        path (writes a backup, atomic-replaces, restores on execvpe failure).
      - _get_script_path uses tempfile.mkstemp() so unique-suffix tempfiles like
        install.py.<rand>.new accumulate if the process is killed between
        write and os.replace.

    Both are safe to drop on every install run: the backup is only consulted
    inline by self_update_if_needed and never read across process boundaries,
    and tempfiles are by definition orphans by the time we get here.
    """
    state_dir = _state_path().parent
    if not state_dir.exists():
        return
    candidates = [state_dir / "install.py.bak", state_dir / "install.py.new"]
    candidates.extend(state_dir.glob("install.py.*.new"))
    for stale in candidates:
        try:
            stale.unlink(missing_ok=True)
        except OSError:
            pass


def _get_script_path(registry: Optional[dict] = None) -> Optional[str]:
    """Resolve install.py path for the auto-update scheduler.

    File mode (`python3 install.py`): return abspath of __file__.

    Pipe mode (`curl ... | python3`): the script bytes are not on disk yet, so
    we materialize them into the state dir using the same trust model as
    self_update_if_needed — registry SHA pin, https-only fetch, structural
    sanity, parse check, atomic replace via tempfile.mkstemp. This is what
    makes a single `curl | python3` re-run sufficient to refresh a stale
    state-dir copy without manual `rm` (#145).

    Pass a pre-fetched ``registry`` dict to avoid a duplicate HTTP round trip
    when the caller (main()) has already loaded it.

    Returns None when bootstrap is impossible (no on-disk copy and remote
    cannot be validated) so the caller can skip scheduler configuration
    instead of pinning cron at a broken script. On a SHA-pin mismatch we
    *do* return an existing on-disk copy if one is present: it was written
    through this same gate (or self_update_if_needed's gate) on a previous
    run, so even if it lags behind the current pin it is still a validated,
    parseable install.py — better than zero scheduler entry.
    """
    if __file__ and os.path.isfile(__file__):
        return os.path.abspath(__file__)

    state_dir = _state_path().parent
    state_dir.mkdir(parents=True, exist_ok=True)
    saved = state_dir / "install.py"

    if registry is None:
        try:
            registry = fetch_json(REGISTRY_URL)
        except (urllib.error.URLError, OSError, json.JSONDecodeError) as exc:
            warn(f"State-dir refresh: cannot fetch registry pin ({exc})")
            return str(saved) if saved.exists() else None

    expected_sha = registry.get("installer_sha256") if isinstance(registry, dict) else None
    if not expected_sha:
        warn("State-dir refresh: registry has no installer_sha256")
        return str(saved) if saved.exists() else None

    if saved.exists():
        try:
            local_sha = hashlib.sha256(saved.read_bytes()).hexdigest()
            if local_sha == expected_sha:
                return str(saved)
        except OSError as exc:
            warn(f"State-dir refresh: cannot read existing copy ({exc})")
            # Fall through — try to refetch and overwrite.

    try:
        remote_bytes = _https_only_fetch(INSTALL_PY_URL, SELF_UPDATE_TIMEOUT)
    except (urllib.error.URLError, OSError) as exc:
        warn(f"State-dir refresh: fetch failed ({exc})")
        return str(saved) if saved.exists() else None

    actual_sha = hashlib.sha256(remote_bytes).hexdigest()
    if actual_sha != expected_sha:
        warn(
            f"State-dir refresh: SHA pin mismatch "
            f"(registry expects {expected_sha[:8]}, got {actual_sha[:8]})"
        )
        return str(saved) if saved.exists() else None

    if not _payload_looks_like_install_py(remote_bytes):
        warn(
            f"State-dir refresh: rejected payload "
            f"(size={len(remote_bytes)} bytes, prefix={remote_bytes[:16]!r})"
        )
        return str(saved) if saved.exists() else None

    try:
        compile(remote_bytes, str(saved), "exec")
    except (SyntaxError, ValueError) as exc:
        warn(f"State-dir refresh: candidate failed to parse ({exc})")
        return str(saved) if saved.exists() else None

    fd, tmp_path = tempfile.mkstemp(
        prefix="install.py.", suffix=".new", dir=str(state_dir)
    )
    try:
        with os.fdopen(fd, "wb") as f:
            f.write(remote_bytes)
        os.replace(tmp_path, saved)
    except OSError as exc:
        warn(f"State-dir refresh: atomic write failed ({exc})")
        try:
            os.unlink(tmp_path)
        except OSError:
            pass
        return str(saved) if saved.exists() else None

    info(f"State-dir install.py refreshed (sha {actual_sha[:8]})")
    return str(saved)


# ---- Main installation flow ----


def apply_install_filters(plugins: list, cli: "argparse.Namespace") -> tuple:
    """Filter the registry plugin list by --plugin and the beta opt-in.

    Returns (filtered_plugins, summary_str). The summary describes the
    skip / allow accounting so the caller can surface it in the log.

    Rules:
      * Without --plugin: drop every entry with `beta: true` unless
        --allow-beta is also set.
      * With --plugin <name>: keep only those names. Refuse (fail) when a
        named plugin is `beta: true` and --allow-beta is missing — silently
        skipping would be a footgun.
      * Plugin names supplied via --plugin must exist in the registry; an
        unknown name is fatal (typo guard).
    """
    requested = list(cli.plugin or [])
    allow_beta = bool(cli.allow_beta)

    by_name = {p.get("name"): p for p in plugins}
    if requested:
        unknown = [n for n in requested if n not in by_name]
        if unknown:
            fail("Unknown plugin(s) requested via --plugin: " + ", ".join(unknown))
        selected = [by_name[n] for n in requested]
        blocked_beta = [p["name"] for p in selected if p.get("beta") and not allow_beta]
        if blocked_beta:
            fail(
                "Refusing to install beta plugin(s) without --allow-beta: "
                + ", ".join(blocked_beta)
            )
        return selected, (
            f"Installing {len(selected)} requested plugin(s); --allow-beta="
            + ("yes" if allow_beta else "no")
        )

    # No --plugin -> bulk mode. Strip beta unless explicitly allowed.
    if allow_beta:
        return plugins, "Bulk install (beta plugins allowed via --allow-beta)"
    kept = [p for p in plugins if not p.get("beta")]
    skipped = [p["name"] for p in plugins if p.get("beta")]
    if skipped:
        return kept, (
            f"Bulk install skipping beta plugin(s): {', '.join(skipped)} "
            "(pass --allow-beta + --plugin to install one)"
        )
    return kept, "Bulk install (no beta plugins in registry)"


def parse_cli_args(argv: List[str]) -> argparse.Namespace:
    """Parse the installer's command-line flags.

    Default behavior (no flags) is unchanged: install every non-beta plugin
    in the registry to every detected target. The flags let the operator
    narrow either dimension and opt into plugins flagged as `beta: true`
    in their `plugin.yaml`.
    """
    parser = argparse.ArgumentParser(
        prog="install.py",
        description=(
            "Install Creatio Engineering plugins into Claude Code. With no "
            "flags, installs every non-beta plugin. Use --plugin to narrow, "
            "and --allow-beta to opt into plugins marked beta in their "
            "plugin.yaml."
        ),
    )

    # --- Plugin selection -------------------------------------------------
    selection = parser.add_argument_group(
        "plugin selection", "Which plugins this run installs."
    )
    selection.add_argument(
        "--plugin",
        action="append",
        default=[],
        metavar="NAME",
        help=(
            "Install only the plugin with this name. May be passed more than "
            "once. When omitted, the full non-beta set is installed."
        ),
    )
    selection.add_argument(
        "--allow-beta",
        action="store_true",
        help=(
            "Allow plugins flagged `beta: true` in plugin.yaml to be "
            "installed. Without this flag, beta plugins are skipped from "
            "the default bulk run and refused when named via --plugin."
        ),
    )
    # --all forces bulk mode; mutually exclusive with the selective --plugin.
    # argparse generates the conflict error and shows the exclusivity in --help.
    selection.add_argument(
        "--all",
        action="store_true",
        help=(
            "Force bulk mode: install every non-beta plugin and persist "
            "bulk in the install profile, even if a prior run pinned a "
            "selective set. Mutually exclusive with --plugin."
        ),
    )

    # --- MCP provisioning -------------------------------------------------
    # The installer provisions the ${VAR} credentials that plugin-shipped MCP
    # servers reference (Creatio creds, GHE token) into settings.json `env`.
    # Prompting is gated: first install only, or on demand via --configure-mcp.
    # Turning individual servers on/off is a Claude Code concern (`/mcp`).
    mcp = parser.add_argument_group(
        "MCP provisioning",
        "Credential provisioning for plugin-shipped MCP servers. Normal "
        "updates/auto-sync never prompt — only the first install or "
        "--configure-mcp does.",
    )
    mcp_toggle = mcp.add_mutually_exclusive_group()
    mcp_toggle.add_argument(
        "--configure-mcp",
        action="store_true",
        help=(
            "(Re)configure MCP server credentials interactively (Creatio "
            "login/password/URL, GHE token) and enable MCP provisioning. "
            "Use this to set up MCP after a silent install, change a token, "
            "or re-enable MCP after --no-mcp."
        ),
    )
    mcp_toggle.add_argument(
        "--no-mcp",
        action="store_true",
        help=(
            "Disable MCP provisioning: skip prompting and do not write MCP "
            "env vars to settings.json. The preference is remembered, so "
            "subsequent runs stay silent too. Re-enable with --configure-mcp."
        ),
    )

    # parse_known_args lets us co-exist with the bootstrap shim that may
    # exec us with `[script_path, *sys.argv[1:]]`-style argv that already
    # contains unrelated flags from upstream wrappers.
    args, _unknown = parser.parse_known_args(argv)
    # --all/--plugin aren't in a mutually-exclusive group because --plugin is
    # `append` (group exclusivity on a repeatable arg is awkward); keep the
    # explicit check. --configure-mcp/--no-mcp exclusivity is enforced by the
    # argparse group above.
    if args.all and args.plugin:
        fail("--all is mutually exclusive with --plugin")
    return args


# ---- Tool install (rtk) ----
#
# The marketplace registry can ship a `tools` block alongside `plugins`. Each
# tool entry pins a version, a platform → asset map (with sha256), and the
# binary name to install. This installer downloads the right asset for the
# current host, drops the binary in `~/.local/bin` atomically, runs `rtk init
# -g` so RTK writes its own Claude Code hook into `~/.claude/settings.json`,
# and persists a tiny tools-state.json so cron ticks can detect when a prior
# init step failed and retry it without forcing a download.


def _detect_tool_platform() -> Optional[str]:
    """Map the current host to a registry asset platform key.

    Returns None for any host we do not ship a binary for — the caller emits
    a clear "unsupported platform" warning. This is what stops a Linux ARM
    host from silently installing the x86_64-musl binary and entering an
    infinite reinstall+exec-format-error loop.
    """
    sysname = platform.system()
    mach = platform.machine().lower()
    if sysname == "Darwin":
        return "darwin-arm64" if mach in ("arm64", "aarch64") else "darwin-x64"
    if sysname == "Windows":
        return "windows-x64" if mach in ("amd64", "x86_64") else None
    if sysname == "Linux":
        if mach in ("x86_64", "amd64"):
            return "linux-x64-musl"
        return None
    return None


def _tool_bin_dir() -> Path:
    return Path.home() / ".local" / "bin"


# Regex priorities for parsing `<tool> --version` output. The anchored variant
# tries to match an "rtk 0.42.0" / "rtk v0.42.0" prefix so we ignore embedded
# rustc / build-date triples that future RTK releases might emit. The generic
# fallback is the original "first semver-shaped token" heuristic.
_VERSION_RE_ANCHORED = re.compile(
    r"\brtk[\s/=]+v?(\d+\.\d+\.\d+(?:[.-][A-Za-z0-9.]+)?)",
    re.IGNORECASE,
)
_VERSION_RE_GENERIC = re.compile(
    r"(\d+\.\d+\.\d+(?:[.-][A-Za-z0-9.]+)?)"
)


def _parse_tool_version(blob: str) -> Optional[str]:
    if not blob:
        return None
    m = _VERSION_RE_ANCHORED.search(blob)
    if m:
        return m.group(1)
    m = _VERSION_RE_GENERIC.search(blob)
    return m.group(1) if m else None


def _local_tool_version(binary_path: Path) -> Optional[str]:
    if not binary_path.exists():
        return None
    import subprocess as sp

    try:
        out = sp.run(
            [str(binary_path), "--version"],
            capture_output=True,
            timeout=10,
            text=True,
        )
    except (OSError, sp.TimeoutExpired):
        return None
    blob = (out.stdout or "") + " " + (out.stderr or "")
    return _parse_tool_version(blob)


def _compare_semver(a: str, b: str) -> int:
    """Compare two semver-ish strings. Returns -1 / 0 / 1.

    Implements just enough of SemVer to keep `install_or_update_tool_rtk`
    from silently downgrading a user who manually upgraded RTK: pre-release
    tags (anything after the first ``-``) rank LOWER than the corresponding
    release, and non-numeric segments fall back to lexicographic compare. Not
    a full semver parser — we never need build metadata ("+...") or strict
    grouping rules here.
    """
    def split(v: str) -> "tuple[list, list]":
        if "-" in v:
            main, pre = v.split("-", 1)
        else:
            main, pre = v, ""
        main_parts = [int(p) if p.isdigit() else p for p in main.split(".")]
        pre_parts = [int(p) if p.isdigit() else p for p in pre.split(".")] if pre else []
        return main_parts, pre_parts

    am, ap = split(a)
    bm, bp = split(b)

    # Pad numeric prefixes equally and compare element-wise.
    for i in range(max(len(am), len(bm))):
        ai = am[i] if i < len(am) else 0
        bi = bm[i] if i < len(bm) else 0
        if type(ai) == type(bi):
            if ai != bi:
                return -1 if ai < bi else 1
        else:
            sa, sb = str(ai), str(bi)
            if sa != sb:
                return -1 if sa < sb else 1

    # Equal main parts: a missing pre-release tag outranks a present one
    # (SemVer rule 11: 1.0.0 > 1.0.0-rc1).
    if not ap and not bp:
        return 0
    if not ap:
        return 1
    if not bp:
        return -1
    for i in range(max(len(ap), len(bp))):
        ai = ap[i] if i < len(ap) else 0
        bi = bp[i] if i < len(bp) else 0
        if type(ai) == type(bi):
            if ai != bi:
                return -1 if ai < bi else 1
        else:
            sa, sb = str(ai), str(bi)
            if sa != sb:
                return -1 if sa < sb else 1
    return 0


def _extract_tool_archive(
    archive_bytes: bytes,
    archive_name: str,
    dest_dir: Path,
    binary_basename: str,
) -> Path:
    """Extract a single binary from a tar.gz or zip into ``dest_dir``.

    Writes into a per-process temp file under dest_dir and then ``os.replace``s
    into place, so a mid-stream copy failure or a second concurrent installer
    cannot leave a torn / half-written binary at the final path. Matches the
    binary by exact root-level name first and only accepts a nested entry as a
    last resort, so a tarball that lists a stray ``examples/rtk`` before the
    real one cannot win the selection.
    """
    import shutil
    import zipfile

    dest_dir.mkdir(parents=True, exist_ok=True)
    target = dest_dir / binary_basename
    tmp_target = dest_dir / f".{binary_basename}.{os.getpid()}.tmp"
    tmp_dir = Path(tempfile.mkdtemp(prefix="rtk-extract-"))

    def _pick(members_root, members_nested):
        if members_root:
            return members_root[0]
        if members_nested:
            return members_nested[0]
        return None

    try:
        archive_path = tmp_dir / archive_name
        archive_path.write_bytes(archive_bytes)
        if archive_name.lower().endswith(".zip"):
            with zipfile.ZipFile(archive_path) as zf:
                names = zf.namelist()
                root = [m for m in names if m == binary_basename]
                nested = [
                    m
                    for m in names
                    if m != binary_basename
                    and (m.endswith("/" + binary_basename) or m.endswith("\\" + binary_basename))
                ]
                pick = _pick(root, nested)
                if pick is None:
                    raise RuntimeError(
                        f"{archive_name} does not contain {binary_basename}"
                    )
                with zf.open(pick) as src, open(tmp_target, "wb") as dst:
                    shutil.copyfileobj(src, dst)
        else:
            with tarfile.open(archive_path, "r:*") as tf:
                members = [m for m in tf.getmembers() if m.isfile()]
                root = [m for m in members if m.name == binary_basename]
                nested = [
                    m
                    for m in members
                    if m.name != binary_basename
                    and m.name.endswith("/" + binary_basename)
                ]
                pick = _pick(root, nested)
                if pick is None:
                    raise RuntimeError(
                        f"{archive_name} does not contain {binary_basename}"
                    )
                src = tf.extractfile(pick)
                if src is None:
                    raise RuntimeError(
                        f"{archive_name} returned no stream for {binary_basename}"
                    )
                with open(tmp_target, "wb") as dst:
                    shutil.copyfileobj(src, dst)
        if platform.system() != "Windows":
            tmp_target.chmod(0o755)
        # os.replace is atomic on POSIX and best-effort on Windows; the
        # caller must accept that on Windows a concurrent process holding
        # the existing rtk.exe open will block the swap with PermissionError.
        os.replace(tmp_target, target)
        return target
    finally:
        try:
            if tmp_target.exists():
                tmp_target.unlink()
        except OSError:
            pass
        import shutil as _rm
        _rm.rmtree(tmp_dir, ignore_errors=True)


_RTK_PATH_MARKER = "# creatio-rtk-bin"


def _ensure_path_unix(bin_dir: Path) -> bool:
    """Append a PATH line to whichever shell startup files apply on this host.

    Touches the union of files commonly read by a developer's interactive
    shells: zsh (.zshrc, .zshenv), bash login shells (.bash_profile,
    .profile on Debian/Ubuntu, .bashrc on Linux non-login bash). The marker
    comment makes the edits idempotent across reruns. Returns True when at
    least one file was modified so the caller can tell the user to open a
    new terminal.
    """
    line = f'export PATH="$HOME/.local/bin:$PATH"  {_RTK_PATH_MARKER}\n'
    home = Path.home()
    sysname = platform.system()
    shell = os.environ.get("SHELL", "")

    candidates: List[Path] = []
    if sysname == "Darwin":
        # macOS: zsh is default; bash users source ~/.bash_profile.
        candidates += [home / ".zshrc", home / ".zshenv", home / ".bash_profile"]
    elif sysname == "Linux":
        # Linux: login bash reads ~/.profile (Debian/Ubuntu) or ~/.bash_profile
        # (others); interactive non-login reads ~/.bashrc; zsh reads ~/.zshrc /
        # ~/.zshenv.
        candidates += [home / ".profile", home / ".bash_profile", home / ".bashrc"]
        candidates += [home / ".zshrc", home / ".zshenv"]
    else:
        candidates += [home / ".profile", home / ".bashrc", home / ".zshrc"]

    # If the user has an explicit SHELL set, also nudge the matching rc file
    # in case the platform-default list missed it (e.g. exotic distros).
    if shell.endswith("/zsh") and (home / ".zshrc") not in candidates:
        candidates.append(home / ".zshrc")
    if shell.endswith("/bash") and (home / ".bashrc") not in candidates:
        candidates.append(home / ".bashrc")

    modified = False
    for rc in candidates:
        try:
            if rc.is_symlink():
                # Skip symlinks pointing outside $HOME — refuse to write to
                # an arbitrary on-disk file if a profile is templated via a
                # broken/external symlink.
                target = rc.resolve(strict=False)
                try:
                    target.relative_to(home.resolve(strict=False))
                except ValueError:
                    continue
            text = rc.read_text(encoding="utf-8") if rc.exists() else ""
            if _RTK_PATH_MARKER in text:
                continue
            with rc.open("a", encoding="utf-8") as fh:
                if text and not text.endswith("\n"):
                    fh.write("\n")
                fh.write(line)
            modified = True
        except OSError:
            continue
    return modified


def _ensure_path_windows(bin_dir: Path) -> bool:
    """Append ``bin_dir`` to the User-scope PATH via PowerShell.

    Returns True on a successful write, False when PATH already had the entry
    or PowerShell was unavailable. Either way the caller surfaces a hint so
    the user can recover manually if needed.
    """
    import subprocess as sp

    bin_dir_s = str(bin_dir).replace("'", "''")
    script = (
        "$cur = [Environment]::GetEnvironmentVariable('Path', 'User');"
        "if ($null -eq $cur) { $cur = '' };"
        "$parts = $cur -split ';' | Where-Object { $_ -ne '' };"
        f"if ($parts -contains '{bin_dir_s}') {{ exit 2 }};"
        f"$new = if ($cur) {{ $cur + ';' + '{bin_dir_s}' }} else {{ '{bin_dir_s}' }};"
        "[Environment]::SetEnvironmentVariable('Path', $new, 'User');"
    )
    try:
        result = sp.run(
            ["powershell", "-NoProfile", "-Command", script],
            capture_output=True,
            timeout=10,
        )
    except (OSError, sp.TimeoutExpired):
        return False
    return result.returncode == 0


def _tools_state_path() -> Path:
    return _state_path().parent / "tools-state.json"


def _load_tools_state() -> dict:
    return read_json(_tools_state_path()) or {"version": 1, "tools": {}}


def _save_tools_state(state: dict) -> None:
    try:
        write_json(_tools_state_path(), state)
    except OSError as exc:
        warn(f"Failed to persist tools state: {exc}")


def _run_rtk_init(binary_path: Path) -> "tuple[bool, str]":
    """Run ``rtk init -g`` and return (ok, captured-stderr).

    The previous implementation used ``check=False`` and discarded the result,
    so a non-zero exit (e.g. corrupt ~/.claude/settings.json) was invisible
    and never retried. Now we explicitly inspect ``returncode`` and propagate
    stderr so the caller can surface diagnostic output when something is
    broken.

    NOTE: a zero return code from ``rtk init -g`` is NOT sufficient evidence
    that the Claude Code PreToolUse hook actually landed in
    ``~/.claude/settings.json`` — in a non-TTY shell RTK skips the
    interactive ``Patch existing settings.json? [y/N]`` prompt, prints the
    JSON fragment under a ``MANUAL STEP`` heading, and exits 0 anyway.
    Callers must verify the post-condition with ``_ensure_rtk_hook_installed``
    before persisting the ``init_done_for_version`` marker.
    """
    import subprocess as sp

    try:
        result = sp.run(
            [str(binary_path), "init", "-g"],
            check=False,
            timeout=30,
            capture_output=True,
            text=True,
        )
    except (OSError, sp.TimeoutExpired) as exc:
        return False, f"{type(exc).__name__}: {exc}"
    if result.returncode != 0:
        stderr = (result.stderr or "").strip() or (result.stdout or "").strip()
        return False, stderr or f"exit code {result.returncode}"
    return True, ""


# ---- Claude Code PreToolUse hook helpers ----
#
# `rtk init -g` is the documented entry-point for wiring RTK's auto-rewrite
# Bash hook into ``~/.claude/settings.json``. In a non-interactive shell
# (which is exactly how the installer runs — both `curl … | python3` and
# the every-6-hours cron tick), RTK asks
#
#     Patch existing /.../settings.json? [y/N]
#     (non-interactive mode, defaulting to N)
#
# and exits with code 0 without touching the file. The previous installer
# trusted the zero exit code, persisted ``init_done_for_version``, and
# never retried — so the hook was silently absent on every host that
# installed non-interactively. We now treat ``rtk init -g`` as
# best-effort (it still does useful side-work like seeding ``RTK.md`` and
# adding the ``@RTK.md`` reference in ``CLAUDE.md``) and verify the
# settings.json post-condition ourselves. When the hook is missing we
# merge it in atomically using the same JSON shape RTK prints under
# ``MANUAL STEP``.

_RTK_HOOK_COMMAND = "rtk hook claude"
_RTK_HOOK_MATCHER = "Bash"

# Token-based match for "is this command the RTK auto-rewrite hook?".
# A plain ``"rtk hook" in command`` substring check is too permissive —
# it would also treat unrelated commands like
# ``echo "running rtk hook setup"`` or ``tail -f rtk-hook.log`` as the
# installed hook and refuse to patch the real one in. The regex pins
# the match to the start of the command (optional leading whitespace),
# followed by the literal ``rtk`` token, whitespace, the literal
# ``hook`` token, and then either end-of-string or whitespace — so
# ``rtk hook claude``, ``rtk hook codex``, ``rtk hook claude --verbose``
# all match while ``echo …rtk hook…`` does not.
_RTK_HOOK_COMMAND_RE = re.compile(r"^\s*rtk\s+hook(\s|$)")


def _claude_settings_path() -> Path:
    """Path to Claude Code's global settings.json (``~/.claude/settings.json``)."""
    return Path.home() / ".claude" / "settings.json"


def _settings_has_rtk_hook(settings_path: Path) -> bool:
    """Return True iff settings.json already wires up an rtk-hook PreToolUse entry.

    The match is anchored to the start of the command string and
    tokenized via ``_RTK_HOOK_COMMAND_RE`` (``^\\s*rtk\\s+hook(\\s|$)``),
    so a user who pinned a specific agent variant
    (``rtk hook claude --verbose``, ``rtk hook codex``, etc.) is not
    re-patched, but unrelated commands that happen to contain the
    substring ``"rtk hook"`` (``echo "running rtk hook setup"``,
    ``tail -f rtk-hook.log``) do not falsely register as present.

    Every nested field is shape-checked with ``isinstance`` before
    iterating or matching, so a hand-edited settings.json with, e.g.,
    ``"hooks": 42`` or a ``command`` set to an integer returns ``False``
    (not present) rather than raising ``TypeError`` and aborting the
    entire installer.
    """
    try:
        text = settings_path.read_text(encoding="utf-8")
    except (FileNotFoundError, OSError):
        return False
    try:
        data = json.loads(text)
    except json.JSONDecodeError:
        return False
    if not isinstance(data, dict):
        return False
    hooks = data.get("hooks")
    if not isinstance(hooks, dict):
        return False
    pre_tool = hooks.get("PreToolUse")
    if not isinstance(pre_tool, list):
        return False
    for entry in pre_tool:
        if not isinstance(entry, dict) or entry.get("matcher") != _RTK_HOOK_MATCHER:
            continue
        raw_hooks = entry.get("hooks")
        if not isinstance(raw_hooks, list):
            continue
        for hook in raw_hooks:
            if not isinstance(hook, dict):
                continue
            if hook.get("type") != "command":
                continue
            command = hook.get("command")
            if not isinstance(command, str):
                continue
            if _RTK_HOOK_COMMAND_RE.match(command):
                return True
    return False


def _patch_settings_with_rtk_hook(settings_path: Path) -> "tuple[bool, str]":
    """Add the RTK PreToolUse Bash hook to settings.json atomically.

    Returns ``(patched, reason)``. ``patched=True`` when settings.json was
    rewritten to include the hook; ``patched=False`` with a reason when the
    file was already adequate (``"already present"``) or could not be safely
    modified (corrupt JSON, wrong shape, IO error). The function refuses to
    touch a file whose existing ``hooks``, ``hooks.PreToolUse``, or
    matcher-``hooks`` are not the expected shape, so a hand-edited
    settings.json with a string where we expect a list is reported as an
    error instead of overwritten — the caller will warn and leave
    ``init_done_for_version`` unset so the next tick retries.

    Atomicity & race-window notes:

    * The final swap uses ``tempfile.mkstemp`` for a unique sibling file
      in the same directory as ``settings_path`` (so ``os.replace`` stays
      atomic — same filesystem). The unique suffix means two concurrent
      installer processes (e.g. a manual ``curl … | python3`` run racing
      the 6-hour cron tick) never clobber each other's temp file, and
      the error-path cleanup only unlinks the temp file this call
      created — never another process's.
    * Concurrent OUTSIDE writers (e.g. Claude Code persisting its own
      settings change between our read and our replace) are detected by
      re-reading the target's bytes right before ``os.replace``: if
      they no longer match the snapshot we parsed, we abort with a
      retryable error so the next installer tick gets a fresh read
      instead of clobbering the foreign writer's payload. This is NOT
      a lock — concurrent writers can still collide in the tiny window
      between the re-read and ``os.replace`` — but it shrinks the race
      from ~80 lines of Python to a single syscall.
    * File permissions are preserved when an existing ``settings.json``
      is updated: ``mkstemp`` creates the temp file at the hardcoded
      ``0600`` default, so without an explicit ``os.chmod`` the target
      would silently downgrade from, e.g., ``0644`` to ``0600`` on
      every patch tick.
    """
    try:
        original_text = settings_path.read_text(encoding="utf-8")
    except FileNotFoundError:
        # settings.json missing entirely — synthesize a minimal one. RTK
        # init normally creates ~/.claude/ on first run, so this branch
        # only hits when the installer races ahead of any other tool
        # that would have created settings.json.
        data: Any = {}
        original_text = ""
    except OSError as exc:
        return False, f"read failed: {exc}"

    if original_text.strip():
        try:
            data = json.loads(original_text)
        except json.JSONDecodeError as exc:
            return False, f"invalid JSON: {exc}"
    else:
        data = {}

    if not isinstance(data, dict):
        return False, "settings.json top-level is not an object"

    hooks = data.setdefault("hooks", {})
    if not isinstance(hooks, dict):
        return False, "settings.json `hooks` is not an object"

    pre_tool = hooks.setdefault("PreToolUse", [])
    if not isinstance(pre_tool, list):
        return False, "settings.json `hooks.PreToolUse` is not a list"

    bash_entry = None
    for entry in pre_tool:
        if isinstance(entry, dict) and entry.get("matcher") == _RTK_HOOK_MATCHER:
            bash_entry = entry
            break
    if bash_entry is None:
        bash_entry = {"matcher": _RTK_HOOK_MATCHER, "hooks": []}
        pre_tool.append(bash_entry)

    bash_hooks = bash_entry.setdefault("hooks", [])
    if not isinstance(bash_hooks, list):
        return False, "settings.json Bash matcher `hooks` is not a list"

    for hook in bash_hooks:
        if not isinstance(hook, dict):
            continue
        if hook.get("type") != "command":
            continue
        command = hook.get("command")
        if not isinstance(command, str):
            continue
        if _RTK_HOOK_COMMAND_RE.match(command):
            # Already present — nothing to do. (Reached when the in-memory
            # walk diverges from `_settings_has_rtk_hook`, e.g. a concurrent
            # writer added it between our two reads.)
            return False, "already present"

    bash_hooks.append({"type": "command", "command": _RTK_HOOK_COMMAND})

    # Reserve a unique temp file in the same directory as the target.
    # `tempfile.mkstemp` gives us a deterministically unique name (PID +
    # random suffix) so two concurrent installer runs never share a
    # temp path. `dir=settings_path.parent` keeps it on the same
    # filesystem, which is what makes `os.replace` atomic.
    try:
        settings_path.parent.mkdir(parents=True, exist_ok=True)
        fd, tmp_str = tempfile.mkstemp(
            prefix=settings_path.name + ".",
            suffix=".rtk-tmp",
            dir=str(settings_path.parent),
        )
    except OSError as exc:
        return False, f"write failed: {exc}"

    tmp = Path(tmp_str)
    try:
        # `os.fdopen` takes ownership of `fd` and closes it when the
        # `with` block exits, so we don't leak an open file descriptor
        # on the success path or on a `write` exception.
        payload = (json.dumps(data, indent=2) + "\n").encode("utf-8")
        with os.fdopen(fd, "wb") as f:
            f.write(payload)
        # Preserve the existing settings.json mode. mkstemp creates the
        # tmp at 0600; without this chmod, os.replace would inherit
        # 0600 into the target on every patch tick and silently
        # downgrade a user who had e.g. 0644. New files keep mkstemp's
        # secure default.
        try:
            original_mode = settings_path.stat().st_mode & 0o777
            os.chmod(tmp, original_mode)
        except FileNotFoundError:
            pass
        # TOCTOU narrow-window guard. Another writer (e.g. Claude Code
        # persisting its own settings change, another configuration
        # tool) may have rewritten settings.json between our parse and
        # this replace. Re-reading bytes right before the swap turns
        # the ~80-line edit window into a single-syscall race — small
        # enough that the next installer tick almost always picks up
        # the foreign change on retry instead of clobbering it.
        try:
            current_text = settings_path.read_text(encoding="utf-8")
        except FileNotFoundError:
            current_text = ""
        if current_text != original_text:
            try:
                tmp.unlink()
            except OSError:
                pass
            return False, "settings.json changed externally during patch"
        os.replace(tmp, settings_path)
    except OSError as exc:
        # Best-effort cleanup of this call's own temp file only. Two
        # try/except layers are intentional: (a) the unlink may fail
        # (race with another process, transient permission error) and
        # that must not mask the original OSError; (b) `tmp` is always
        # the path `mkstemp` just returned to us, so we can never
        # accidentally unlink a sibling installer's temp file.
        try:
            tmp.unlink()
        except OSError:
            pass
        return False, f"write failed: {exc}"
    return True, "patched"


def _ensure_rtk_hook_installed(settings_path: Path) -> bool:
    """Ensure settings.json carries the RTK hook, patching it if `rtk init -g` did not.

    Returns True when the hook is present after this call (either because
    it was already there or we just merged it in), False when settings.json
    could not be brought into a valid state. The caller must refuse to
    persist ``init_done_for_version`` on False so the next installer tick
    retries.
    """
    if _settings_has_rtk_hook(settings_path):
        return True
    patched, reason = _patch_settings_with_rtk_hook(settings_path)
    if patched:
        info("RTK: added auto-rewrite hook to ~/.claude/settings.json")
        return True
    if reason == "already present":
        # Race: another process added the hook between our two reads.
        # Treat as success — the post-condition holds.
        return True
    warn(
        f"RTK: could not add auto-rewrite hook to ~/.claude/settings.json "
        f"({reason}); Bash auto-rewrite stays off until this is resolved"
    )
    return False


def install_or_update_tool_rtk(registry: dict) -> None:
    """Install or update the rtk CLI according to the registry's tools.rtk pin.

    Idempotent: a cron tick re-enters and returns early ONLY when both the
    binary version matches the registry pin AND the Claude Code PreToolUse
    hook is actually present in ``~/.claude/settings.json`` for that
    version. CREATIO_SKIP_RTK opts a host out entirely.

    The version compare uses semver semantics so a manually-upgraded host
    (e.g. ``brew upgrade rtk``) is not silently downgraded back to the
    registry pin — we install only when desired > installed.

    Marker semantics: ``tools_state.tools.rtk.init_done_for_version`` is
    asserted to mean "the Claude Code Bash auto-rewrite hook is wired up
    for this RTK version" — NOT "rtk init -g exited 0". A non-TTY
    ``rtk init -g`` can exit 0 without patching settings.json (see
    ``_run_rtk_init`` docstring), so we persist the marker only after
    ``_ensure_rtk_hook_installed`` confirms the post-condition holds.
    """
    if os.environ.get("CREATIO_SKIP_RTK", "").lower() in ("1", "true", "yes"):
        info("RTK: skipped (CREATIO_SKIP_RTK is set)")
        return

    tool = (registry.get("tools") or {}).get("rtk")
    if not tool:
        info("RTK: registry has no tools.rtk block; skipping")
        return

    desired_version = tool.get("version")
    if not desired_version or not isinstance(desired_version, str):
        warn("RTK: registry tools.rtk.version is missing or invalid; skipping")
        return

    assets = tool.get("assets") or {}
    binary_names = tool.get("binary_name") or {"unix": "rtk", "windows": "rtk.exe"}
    platform_key = _detect_tool_platform()
    if not platform_key:
        warn(
            f"RTK: unsupported platform {platform.system()} {platform.machine()}; skipping"
        )
        return
    asset = assets.get(platform_key)
    if not asset:
        warn(f"RTK: no asset for platform {platform_key} in registry; skipping")
        return

    expected_sha = asset.get("sha256")
    if not expected_sha or not isinstance(expected_sha, str):
        warn(
            f"RTK: registry asset for {platform_key} has no sha256 — refusing to install unverifiable bytes"
        )
        return

    bin_dir = _tool_bin_dir()
    binary_basename = (
        binary_names["windows"] if platform.system() == "Windows" else binary_names["unix"]
    )
    binary_path = bin_dir / binary_basename

    installed_version = _local_tool_version(binary_path)
    tools_state = _load_tools_state()
    init_done_for = (
        tools_state.get("tools", {}).get("rtk", {}).get("init_done_for_version")
    )
    settings_path = _claude_settings_path()

    # Self-heal stale marker: a host that ran a pre-fix installer (which
    # trusted ``rtk init -g``'s zero exit and persisted ``init_done_for_version``
    # even when the hook was never patched into settings.json) would
    # otherwise skip every retry path below. Clearing the marker when
    # settings.json disagrees lets the rest of this function re-run the
    # init/verify/patch cycle for the same version it claims to be done with.
    if init_done_for and not _settings_has_rtk_hook(settings_path):
        info(
            f"RTK: marker reports init done for v{init_done_for} but the Claude Code "
            "hook is missing from ~/.claude/settings.json — clearing marker and retrying"
        )
        tools_state.get("tools", {}).get("rtk", {}).pop("init_done_for_version", None)
        _save_tools_state(tools_state)
        init_done_for = None

    needs_install = False
    if installed_version is None:
        needs_install = True
    else:
        try:
            cmp = _compare_semver(installed_version, desired_version)
        except Exception:
            cmp = -1 if installed_version != desired_version else 0
        if cmp < 0:
            needs_install = True
        elif cmp > 0:
            info(
                f"RTK: keeping locally installed v{installed_version} (registry pins v{desired_version})"
            )
            # Still ensure init has happened for whatever version is on disk.
            if init_done_for != installed_version:
                ok, err = _run_rtk_init(binary_path)
                if not ok:
                    warn(
                        f"RTK: `rtk init -g` failed ({err}); will continue and verify "
                        "settings.json directly"
                    )
                if _ensure_rtk_hook_installed(settings_path):
                    tools_state.setdefault("tools", {}).setdefault("rtk", {})[
                        "init_done_for_version"
                    ] = installed_version
                    _save_tools_state(tools_state)
                else:
                    warn(
                        "RTK: hook still missing in ~/.claude/settings.json; "
                        "will retry on the next tick"
                    )
            return

    if not needs_install:
        # Version matches; ensure init ran successfully at least once for it.
        if init_done_for == desired_version:
            info(f"RTK: already at v{desired_version}")
            return
        info(
            f"RTK: binary at v{desired_version} present but init not confirmed — running `rtk init -g`"
        )
        ok, err = _run_rtk_init(binary_path)
        if not ok:
            warn(
                f"RTK: `rtk init -g` failed ({err}); will continue and verify "
                "settings.json directly"
            )
        if _ensure_rtk_hook_installed(settings_path):
            tools_state.setdefault("tools", {}).setdefault("rtk", {})[
                "init_done_for_version"
            ] = desired_version
            _save_tools_state(tools_state)
            # Report what actually happened — not "init confirmed", since
            # the hook may have landed via our fallback patch after
            # `rtk init -g` itself failed.
            if ok:
                info(f"RTK: hook confirmed for v{desired_version}")
            else:
                info(
                    f"RTK: hook installed for v{desired_version} via fallback "
                    "merge (rtk init -g reported failure)"
                )
        else:
            warn(
                "RTK: hook still missing in ~/.claude/settings.json after init "
                "attempt; will retry on the next tick"
            )
        return

    info(f"RTK: installing v{desired_version} ({platform_key})...")
    try:
        archive_bytes = fetch(asset["url"], timeout=120)
    except (urllib.error.URLError, OSError) as exc:
        warn(f"RTK: download failed ({exc}); skipping")
        return

    actual_sha = hashlib.sha256(archive_bytes).hexdigest()
    if actual_sha != expected_sha.lower():
        warn(
            f"RTK: SHA256 mismatch (got {actual_sha}, expected {expected_sha}); skipping install"
        )
        return

    try:
        _extract_tool_archive(archive_bytes, asset["filename"], bin_dir, binary_basename)
    except Exception as exc:
        warn(f"RTK: extraction failed ({exc}); skipping")
        return

    path_modified = False
    try:
        if platform.system() == "Windows":
            path_modified = _ensure_path_windows(bin_dir)
        else:
            path_modified = _ensure_path_unix(bin_dir)
    except Exception as exc:
        warn(f"RTK: PATH update best-effort failed ({exc})")

    # Run `rtk init -g` via the absolute binary path, so a fresh install does
    # not depend on the shell rc edit having been sourced yet. RTK is
    # supposed to write the auto-rewrite hook into ~/.claude/settings.json
    # itself, but in a non-TTY shell it skips the interactive patch step
    # and exits 0 — so we always follow up with `_ensure_rtk_hook_installed`
    # to verify the post-condition (and merge the hook ourselves if RTK
    # did not). The per-version marker is persisted only when the hook is
    # actually present so a later cron tick retries instead of silently
    # leaving Bash auto-rewrite off.
    ok, err = _run_rtk_init(binary_path)
    if not ok:
        warn(
            f"RTK: `rtk init -g` failed ({err}); will continue and verify "
            "settings.json directly"
        )
    if _ensure_rtk_hook_installed(settings_path):
        tools_state.setdefault("tools", {}).setdefault("rtk", {})[
            "init_done_for_version"
        ] = desired_version
        _save_tools_state(tools_state)
    else:
        warn(
            "RTK: hook still missing in ~/.claude/settings.json after init "
            "attempt; will retry on the next tick"
        )

    info(f"RTK: installed v{desired_version} -> {binary_path}")
    if path_modified:
        info("RTK: PATH updated — open a new terminal so the change takes effect")
    if platform.system() == "Windows":
        info(
            "RTK: native Windows shells run without auto-rewrite — use WSL for full hook support"
        )


def main() -> None:
    cli = parse_cli_args(sys.argv[1:])

    print()
    print("  Creatio Engineering — AI Plugin Marketplace Installer")
    print("  =====================================================")
    print()

    # Python version is checked before self-update so a future MIN_PYTHON bump
    # cannot atomically overwrite the installer with bytes the running
    # interpreter cannot execute.
    if sys.version_info < MIN_PYTHON:
        fail(
            f"Python {MIN_PYTHON[0]}.{MIN_PYTHON[1]}+ required, found {sys.version_info.major}.{sys.version_info.minor}"
        )

    self_update_if_needed()
    _cleanup_stale_self_update_artifacts()

    # Checks
    system = platform.system()
    info(f"Platform: {system} {platform.machine()}")
    info(f"Python: {sys.version.split()[0]}")

    check_network()

    # Detect targets (Claude Code only). Done BEFORE the legacy cleanup so a
    # host without Claude Code fails here with a clear message instead of
    # having `~/.claude` created as a side effect of writing the cleanup
    # marker (the marker lives under `~/.claude/.creatio-marketplace/`, and
    # `detect_targets()` keys off whether `~/.claude` exists).
    targets = detect_targets()
    if not targets:
        fail(
            "Claude Code not detected.\n"
            "  Install Claude Code first: https://claude.ai/code"
        )

    target_names = ", ".join(t["name"] for t in targets.values())
    info(f"Detected: {target_names}")

    # One-shot migration: users upgrading from the legacy npm
    # `@creatio/dev-skills` distribution need their cron scheduler,
    # flat skills under `~/.agents/skills`, and npm-global package torn
    # down before the new Claude-plugin install can take effect. Marker
    # file in `~/.claude/.creatio-marketplace/` keeps the cleanup
    # idempotent across cron ticks.
    cleanup_legacy_npm_install()

    # Fetch registry
    info(f"Fetching marketplace registry...")
    try:
        registry = fetch_json(REGISTRY_URL)
    except (urllib.error.URLError, OSError, json.JSONDecodeError) as e:
        fail(f"Failed to fetch registry: {e}")

    plugins = registry.get("plugins", [])
    info(f"Available plugins: {len(plugins)}")

    # Hydrate cli from the saved profile when invoked without flags (e.g. by
    # the 6-hour cron tick). This is what keeps a selective install from
    # being silently broadened to the full bulk set on every auto-update.
    cli, profile_msg = resolve_effective_cli(cli)
    if profile_msg:
        info(profile_msg)

    # Apply --plugin / --allow-beta filters declared on the CLI.
    # A missing --plugin entry is a hard error; we will not silently install
    # nothing when the operator named a plugin that does not exist in the
    # registry.
    plugins, registry_filter_summary = apply_install_filters(plugins, cli)
    if registry_filter_summary:
        info(registry_filter_summary)

    # Counters survive an empty filtered plugin list so the post-loop
    # MCP / tool / scheduler setup still runs. A stale selective profile
    # that filters to zero plugins must not block RTK updates or scheduler
    # refresh — that was a regression introduced by the previous early
    # return at this point.
    state = read_state()
    installed_count = 0
    claude_target = targets.get("claude")

    if not plugins:
        info("No plugins to install after filters; continuing with MCP / tool / scheduler setup.")
    elif claude_target is None:
        # detect_targets() returned no Claude entry but earlier check
        # only fails when targets is empty — defensive belt for any
        # future code path that mutates `targets` between then and here.
        info("Claude Code target unavailable; skipping plugin install loop.")
    else:
        print()
        for plugin in plugins:
            name = plugin["name"]
            version = plugin["version"]
            download_url = plugin.get("download_url")

            if not download_url:
                warn(f"  {name}: no download URL, skipping")
                continue

            # Respect the plugin's `targets` field so a plugin that does
            # not opt into Claude is skipped silently. Legacy default
            # (`["claude", "codex", "opencode"]`) still includes Claude,
            # so older registry entries without the field continue to
            # install on this Claude-only build.
            plugin_targets = set(
                plugin.get("targets") or ["claude", "codex", "opencode"]
            )
            if "claude" not in plugin_targets:
                continue

            print(f"  Installing {name} v{version}...")

            try:
                tarball = fetch(download_url, timeout=60)
            except (urllib.error.URLError, OSError) as e:
                warn(f"  {name}: download failed ({e}), skipping")
                continue

            if not verify_sha256(tarball, plugin.get("sha256")):
                warn(f"  {name}: SHA256 mismatch, skipping")
                continue

            try:
                install_plugin_claude(plugin, tarball, claude_target)
            except Exception as e:
                warn(f"  {name} -> {claude_target['name']}: {e}")
                continue

            state["plugins"][name] = {
                "version": version,
                "installedAt": state.get("plugins", {})
                .get(name, {})
                .get("installedAt", _now()),
                "updatedAt": _now(),
                "targets": list(targets.keys()),
            }
            installed_count += 1

    # Bookkeeping always runs so the cron tick records lastPoll even on a
    # zero-plugin run; without this, a fleet of selective profiles that
    # filter to empty would never advance their poll cursor.
    state["lastPoll"] = _now()
    state["registryUrl"] = REGISTRY_URL
    write_state(state)

    # Now that plugins are installed, drop the duplicate ~/.claude/skills
    # symlinks left by the retired npm `@creatio/dev-skills` flow — but ONLY for
    # skills now verifiably present in the plugin cache, so a skill is never left
    # without a copy (see remove_duplicate_agent_skill_symlinks). This is what
    # the legacy cleanup missed; running it here (not marker-gated) self-heals
    # the already-migrated fleet on the next install / auto-update tick.
    remove_duplicate_agent_skill_symlinks(claude_target)

    # Provision MCP env vars for servers the installed plugins need. Prompts
    # only on first install or with --configure-mcp; --no-mcp disables it.
    # Normal updates / auto-sync ticks run silently (see configure_mcp).
    configure_mcp(
        targets,
        registry,
        state,
        configure=bool(getattr(cli, "configure_mcp", False)),
        disable=bool(getattr(cli, "no_mcp", False)),
    )

    # Install or update bundled developer tools (rtk). Best-effort: any
    # failure surfaces a warning but does not abort the rest of the run.
    try:
        install_or_update_tool_rtk(registry)
    except Exception as exc:
        warn(f"RTK install step failed: {exc}")

    # Configure auto-update scheduler
    script_path = _get_script_path(registry)
    if script_path:
        configure_scheduler(script_path)

    # Persist the install profile so the next cron tick honors this run's
    # intent (selective vs bulk). Done after the install loop succeeded so
    # we never lock in a profile that did not actually take effect.
    #
    # `cli.persisted_mode_override` is set by `resolve_effective_cli`
    # when the user passed `--plugin X` on top of an existing bulk
    # profile. In that case we install X this run but keep the bulk
    # profile so the next cron tick continues to refresh the full pack.
    persisted_override = getattr(cli, "persisted_mode_override", None)
    if persisted_override == "bulk":
        save_profile(
            mode="bulk",
            plugins=[],
            allow_beta=bool(cli.allow_beta),
        )
    else:
        save_profile(
            mode="selective" if cli.plugin else "bulk",
            plugins=list(cli.plugin),
            allow_beta=bool(cli.allow_beta),
        )

    # Update marketplace repo if cloned
    _pull_marketplace_repo(targets)

    # Summary
    print()
    if installed_count > 0:
        info(f"Installed {installed_count} plugin(s) to: {target_names}")
    else:
        info("All plugins are up to date.")

    print()
    print("  Installation complete!")
    print()
    if "claude" in targets:
        print("  Claude Code: plugins are ready. Restart Claude Code to activate.")
    print("  Auto-update: scheduled every 6 hours.")
    print()


if __name__ == "__main__":
    main()
