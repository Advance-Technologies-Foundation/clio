# autoupdate

## Command Type

    Application management

## Name

autoupdate - Enable or disable automatic clio updates on startup

## Synopsis

```bash
autoupdate [--enable | --disable]
```

## Description

Controls the `clio` policy in the automatic-update settings. Running the
command without arguments displays whether automatic clio updates are enabled.

Clio also has independent knowledge and toolkit policies. On an eligible
command startup, each due enabled policy advances its `next-run` timestamp and
calls the existing updater on a best-effort basis.

```json
"autoupdate": {
  "clio":      { "enabled": true, "frequency-minutes": 480, "next-run": "2026-09-04T08:00:00Z" },
  "knowledge": { "enabled": true, "frequency-minutes": 60,  "next-run": "2026-09-04T01:00:00Z" },
  "toolkit":   { "enabled": true, "frequency-minutes": 60,  "next-run": "2026-09-04T01:00:00Z" }
}
```

The timestamps are maintained by clio. An existing scalar `Autoupdate` value
is accepted and applied only to the clio policy.

## Options

```bash
--enable    Enable automatic clio updates (default behavior)

--disable   Disable automatic clio updates
```

## Examples

```bash
# Show current autoupdate setting
autoupdate

# Disable automatic updates
autoupdate --disable

# Re-enable automatic updates
autoupdate --enable
```

## Behavior

- With no flags: prints whether auto-update is currently enabled or disabled
- --enable and --disable control only `autoupdate.clio.enabled`
- Default frequencies are 480 minutes for clio and 60 minutes for knowledge and toolkit
- Due policies reuse the existing clio, knowledge, and toolkit update services
- Manual update commands remain available and bypass the schedule

## Exit Codes

    0   Setting applied successfully (or status displayed)

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#autoupdate)
