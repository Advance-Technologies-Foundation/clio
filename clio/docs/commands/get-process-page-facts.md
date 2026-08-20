# get-process-page-facts

Reads the facts a **Pre-configured page** process element needs about a Freedom UI page.

Aliases: `page-facts`

## Why this command exists

A Pre-configured page process element stores two things it cannot work out for itself:

- **which buttons complete the page** — the element resumes the process when the user presses one of them;
- **which page-scoped entity data sources the page has** — each becomes an element parameter carrying the id of
  the record the page added or modified.

Both are only knowable from the **merged** page, because a page inherits its buttons from its template chain. The
platform performs that merge client-side in the process designer and exposes no server-side merged-view API, so
the `CrtProcessBuilder` package cannot read them. clio already merges the chain to build a page bundle, so it
supplies the facts and the package writes the element.

## Usage

```bash
clio get-process-page-facts --schema-name UsrRequestReview_FormPage -e dev
```

| Option | Description |
| --- | --- |
| `--schema-name` | Freedom UI page schema name (required) |
| `--culture` | Culture used to resolve resource-backed button captions (default `en-US`) |
| `-e, --environment` | Registered environment name |

## Output

```json
{
  "success": true,
  "schema-name": "UsrRequestReview_FormPage",
  "completingButtonCandidates": [
    { "name": "SaveButton", "caption": "Save | SaveButton", "event": "clicked",
      "requests": ["crt.SaveRecordRequest"] },
    { "name": "CancelButton", "caption": "Cancel | CancelButton", "event": "clicked",
      "requests": ["crt.CancelRecordChangesRequest"] }
  ],
  "dataSources": [ { "name": "PDS", "entitySchemaName": "UsrRequest" } ]
}
```

Pass `completingButtonCandidates` and `dataSources` verbatim into the process descriptor's
`preconfiguredPage.buttons` / `preconfiguredPage.dataSources`. Choosing **which** candidates complete the page is
still a decision the caller makes — the element requires at least one.

## Selection rules

These mirror the process designer's own rules, transcribed from the shipped designer bundle and verified against a
designer-built element:

- A **button** is a candidate when its click handler issues `crt.SaveRecordRequest`, `crt.ClosePageRequest` or
  `crt.CancelRecordChangesRequest` — or when it declares no requests at all (a custom button that only runs code
  can still be chosen).
- A **menu** button contributes one entry per leaf menu item, not one for itself, with the caption path joined as
  `"Actions | Approve | ApproveItem"`.
- The **caption** is the page button's resolved caption and its element name joined with `" | "`, which is exactly
  what the designer stores on the element.
- A **data source** is reported only when its scope is `page` and its type is `crt.EntityDataSource`. The
  view-element-scoped sources behind lists and detail grids are excluded — including them would generate element
  parameters the page never fills.

## Notes

- Fails for a Classic UI page, which completes through the buttons its own page designer marks as completing the
  process step. The error says so rather than reporting an empty candidate list.
- Read-only: it never modifies the page.
