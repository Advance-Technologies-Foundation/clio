# process-add-element

Drive the live Creatio **Process Designer** (over the Chrome DevTools Protocol) to append and configure
an element and **save** the process — without hand-drawing BPMN. This slice supports the **Read data**
element; the result is a runnable `Start → Read data → End` process.

**Alias:** `pae`

## Usage

```bash
clio process-add-element --element-type read-data --read-object Contact -e <environment>
clio pae --element-type read-data --read-object Account --process-caption "My read" -e <environment>
```

## Options

| Option | Required | Description |
|---|---|---|
| `--element-type` | yes | Element type to add. Supported: `read-data`. |
| `--read-object` | yes | Object the Read data element reads, e.g. `Contact`. |
| `--process-id` | no | Existing process id to open; omit to create a **new** process. |
| `--process-caption` | no | Process caption (readback handle); auto-generated when omitted. |
| `--headed` | no | Run the browser headed (default `true`; headless unverified). |
| `-e, --environment` | yes | Registered clio environment name (forms-auth). |

## How it works

1. **Validate** the planned `Start → Read data → End` graph; abort **before** opening a browser on any
   error finding.
2. Obtain an authenticated browser session (`get-browser-session`) and launch a local Chromium with
   remote debugging (CDP).
3. Append a Read data element onto the `Start→End` flow, set the **source object**, assert the connection
   is valid (no `.djs-validate-outline`), and **SAVE**.
4. Print the saved identity as JSON:

```json
{ "success": true, "code": "UsrProcess_493d4c9", "uId": "dd3a473e-…", "caption": "clio-pae-…" }
```

## Requirements

- A **forms-auth** Creatio environment (login + password). OAuth-only environments are not supported.
- A local **Chromium**-based browser (Chrome/Edge/Brave) on the machine running clio.

## Errors

Each failure class prints a specific `Error:` and exits non-zero, **never** reporting a false-positive
save: planned-graph invalid; no forms-auth session; Chromium not found; designer never rendered; object
lookup not found; connection flagged `.djs-validate-outline`; SAVE not confirmed.

## Read it back

- [`describe-process`](describe-process.md) `--process-caption "<caption>"` — structured graph.
- [`generate-process-model`](generate-process-model.md) `--code <code>` — C# model.

## Related

- `validate-process-graph` (MCP) — pre-check a planned graph against the connection rules.
- `get-guidance` (MCP) name `process-modeling` — the element catalog + connection rules.
