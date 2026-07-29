# create-related-page-addon

## Command Type

Development commands

## Name

create-related-page-addon - Bind Freedom UI pages to an object: which page opens by default for a record and which page is used to add a record (the RelatedPage add-on)

## Description

Configures the `RelatedPage` add-on attached to an object (entity schema), controlling which Freedom UI
page opens when a record is opened (the default page) and when a record is added (the add page).
Optionally binds a separate page set per audience - internal `All employees` versus portal
`All external users` - and per record type.

The command performs the same round-trip the Interface Designer does when you save related pages: it
reads the (server auto-provisioned) `RelatedPage` add-on for the object, replaces its page list, saves
it through `AddonSchemaDesignerService`, resets the client script cache, and rebuilds static content so
the change is reflected in the UI. Existing page and object schemas are not modified - only the object's
related-page add-on metadata is written.

Page schema names are resolved to their `PageSchemaUId` automatically. An unknown object, package, or
page name fails the command with a clear message and writes nothing.

## Synopsis

```
clio create-related-page-addon -e ENVIRONMENT --entity-schema-name NAME --package-name NAME [OPTIONS]
```

## Options

| Option | Required | Default | Description |
|---|---|---|---|
| `-e, --environment` | No | | Registered Creatio environment to target. |
| `--entity-schema-name` | Yes | | Object (entity schema) the related pages belong to, e.g. `UsrDeliveryItem`. |
| `--package-name` | Yes | | Package that owns the add-on configuration, e.g. `Custom`. |
| `--default-page` | No | | Page schema name shown by default when opening a record. Always provide this so the configuration carries a base default page (the page opened for a record and the fallback when a record's type has no dedicated set). |
| `--add-page` | No | `--default-page` | Page schema name used when adding a new record. |
| `--portal-default-page` | No | | Page shown by default to portal (self-service / `All external users`) users. When any portal page is set, the `--default-page` / `--add-page` set is auto-scoped to `All employees` so the internal and portal sets stay separate. |
| `--portal-add-page` | No | `--portal-default-page` | Page portal users use when adding a record. |
| `--type-column-uid` | No | | UId of the type column that drives type-specific page sets. Omit for a single page set. |
| `--schema-type` | No | `web` | Target UI surface: `web` (the `RelatedPage` add-on) or `mobile` (the `MobileRelatedPage` add-on — the page the Creatio Mobile app opens for a record). The two add-ons are independent; writing one never affects the other. |

## Examples

```
# Bind a default record page and a separate add page.
clio create-related-page-addon -e dev --entity-schema-name UsrDeliveryItem --package-name Custom \
    --default-page UsrDeliveryItemFormPage --add-page UsrDeliveryItemAddPage

# Bind internal and portal page sets (internal pages are auto-scoped to "All employees").
clio create-related-page-addon -e dev --entity-schema-name UsrRequest --package-name Custom \
    --default-page UsrRequestFormPage --portal-default-page UsrRequestPortalFormPage
```

## Notes

- Aliases: `related-page-addon`, `set-related-pages`.
- The page list **fully replaces** the object's current related-page configuration; it is not a merge.
- The richer per-role and per-type page matrix is available through the `create-related-page-addon` MCP
  tool's `pages` array. For the full binding flow (audiences, typed page sets, name discovery) read the
  `related-page-binding` guidance article (`get-guidance name=related-page-binding`).
- Only the two audiences the Interface Designer produces are supported: internal `All employees` and portal
  `All external users`. A custom role (via the MCP `pages` array's `role` / `role-name`) is rejected — the
  platform's runtime resolution of an arbitrary role in a related-page set is unverified.
- Sending an **empty** `pages` array through the MCP tool clears all bindings (reset to inline) — the
  effective delete, since the platform has no add-on delete. The scalar CLI cannot express this: a
  no-option invocation is rejected rather than silently wiping the configuration.
- For a safe read-modify-write, `get-related-page-addon` output can be replayed into the MCP `pages` array
  verbatim: an entry may carry both `role` and `role-name` (reconciled when they agree), and each entry's
  `page-schema-uid` is accepted and used as-is — so a page whose name no longer reverse-resolves still
  round-trips instead of being silently dropped. `page-schema-uid` wins over `page-schema-name` when both
  are present. (`page-schema-uid` is an MCP-only field; the scalar CLI resolves by page name.)
- This tool manages the **web** related-page add-on (`RelatedPage`) by default. Pass `--schema-type mobile` to
  manage the **mobile** related-page add-on (`MobileRelatedPage`) instead — the page the Creatio Mobile app
  opens for a record. The two add-ons are independent: a write to one (including an empty-clear reset) is
  neither read nor written against the other, so it never affects the other surface's page configuration.
