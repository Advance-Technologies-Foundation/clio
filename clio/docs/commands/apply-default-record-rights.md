# apply-default-record-rights

## Command Type

Object rights

## Name

apply-default-record-rights - apply an object's default record rules to its existing records

## Description

Starts the platform's "Update record permissions" — the process `ObjectRecordRightsActualizationProcess` — once for
ONE object. The record rights that came from default rules are deleted and the current default rules (see
`set-default-record-rights`) are applied to every existing record. Rights granted by hand (`set-record-rights`) stay.

Run it only when the **user decides** to: the run is heavy on large tables (Creatio Academy: minutes or more, run it
at low load). `get-object-rights` and `set-default-record-rights` report the number of existing records for that
decision. The Freedom UI designer also asks instead of running it.

- Refused when the object's record permissions are OFF.
- The launch is sent once and never retried: a retry would start a second run.
- With `--wait` (default `true`) the command follows the run in its `SysProcessLog` row until it completes, fails or
  `--timeout-seconds` pass. A run still going then is reported as **still running** with its process id, exit 0 — do
  NOT start it again; check `SysProcessLog` (`Id` = the process id) later.

**Destructive and not idempotent**: every call starts a run. A non-interactive run needs `--confirm`; on MCP the host's
approval of the call is the confirmation.

## Synopsis

```bash
clio apply-default-record-rights --entity-schema-name <Name> [--wait true|false] [--timeout-seconds N] [--confirm] -e <environment>
```

## Options

```bash
--entity-schema-name NAME   The object whose existing records get the current rules. Required.
--wait BOOL                 true (default): wait for the run to end; false: return the process id at once.
--timeout-seconds N         How long to wait (default 300; 60 on MCP, where the whole call ends after about 100 s and a longer run is reported as still running).
--confirm                   Start without a prompt.
-e, --environment NAME      Registered environment.
```

## Exit codes

- `0` — completed, still running (with its process id), started but its status could not be read (said so, with the process id), queued without an id, or cancelled at the prompt.
- `1` — invalid input, no confirmation in a non-interactive run, the object could not be read, record permissions
  are OFF, the run could not be started, or it ended in error or was cancelled.

## Examples

```bash
clio apply-default-record-rights --entity-schema-name UsrOrder --timeout-seconds 600 --confirm -e production
clio apply-default-record-rights --entity-schema-name UsrOrder --wait false --confirm -e production
```
