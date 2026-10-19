# create-page

## Command Type

    Development commands

## Name

create-page - Create a new Freedom UI page from a supported template

**Aliases:** `page-create`

## Description

The create-page command provisions a new Freedom UI page schema by
saving a new ClientUnit schema whose parent is one of the templates
advertised by the target Creatio environment through
`/rest/schema.template.api/templates`. Use `list-page-templates` to
discover valid template values (web and mobile).

create-page validates the input before calling the designer service:
schema-name format, template existence, package existence, schema-name
uniqueness, and optional entity schema resolution. The command emits
step-by-step progress for every validation and remote call, and on
success returns the assigned SchemaUId and the resolved package /
template metadata.

create-page performs no client-side body validation of its own; it relies on
the platform template body. After a successful call, use `get-page` to read
the new schema back through the canonical page workflow.

## Synopsis

```bash
clio create-page [options]
```

## Options

```bash
--schema-name                      New page schema name, e.g. UsrMyApp_BlankPage
                                   Must start with a letter and contain only
                                   letters, digits, or underscores

--template                         Template name or UId returned by
                                   list-page-templates, e.g. BlankPageTemplate.
                                   For a desktop page use CentralAreaDesktopTemplate

--package-name                     Target package name that will own the
                                   new page schema

--caption                          Optional display caption; defaults to
                                   schema-name

--description                      Optional schema description

--entity-schema-name               Optional entity schema the page is bound
                                   to. A web page gets a data source for it
                                   (see Notes); the schema is also recorded
                                   in the page dependencies

--optional-properties              Optional JSON array of {key, value} objects
                                   seeded into the new schema optionalProperties.
                                   Used to create a dashboard (see Notes).

--caption-culture                  Override the culture for the generated page
                                   caption (e.g. en-US, uk-UA). Precedence:
                                   override > the connected user's profile
                                   culture (see get-user-culture) > en-US.

--uri                    -u       Application uri

--Password               -p       User password

--Login                  -l       User login (administrator permission required)

--Environment            -e       Environment name

--Maintainer             -m       Maintainer name
```

## Example

```bash
clio list-page-templates -e dev
discover valid templates in the target environment

clio create-page --schema-name UsrDemo_BlankPage --template BlankPageTemplate --package-name Custom --caption "Demo blank page" -e dev
create the Freedom UI page from the BlankPageTemplate parent

clio create-page --schema-name UsrDemo_Mobile --template BlankMobilePageTemplate --package-name Custom -e dev
create a mobile Freedom UI page from the BlankMobilePageTemplate parent

clio create-page --schema-name UsrDemo_Dashboard --template BaseDashboardTemplate --package-name Custom --optional-properties "[{\"key\":\"DashboardsEntitySchemaName\",\"value\":\"Contact\"},{\"key\":\"DashboardsElementName\",\"value\":\"Dashboards\"},{\"key\":\"DashboardsClientUnitSchemaUId\",\"value\":\"<root-schema-uid>\"}]" -e dev
create a dashboard from BaseDashboardTemplate and seed its link-back optional properties

clio create-page --schema-name UsrSalesDesktop --template CentralAreaDesktopTemplate --package-name Custom --caption "Sales desktop" -e dev
create a desktop (a desktop-selector workspace) from the CentralAreaDesktopTemplate parent
```

## Notes

- The page caption is stored under the resolved culture (`--caption-culture` override > the connected user's profile culture > `en-US`). A caption whose script does not match a Latin-script culture (for example Cyrillic under `en-US`) is rejected with an actionable error; pass `--caption-culture` to author the caption in a specific language.
- `--optional-properties` accepts a JSON array of `{key, value}` objects that are written verbatim into the new schema's `optionalProperties`. A malformed payload fails before any remote call.
- To create a **dashboard**, use `--template BaseDashboardTemplate` and pass its link-back properties (`DashboardsEntitySchemaName`, `DashboardsElementName`, `DashboardsClientUnitSchemaUId`) through `--optional-properties`. When the host page has replacements, `DashboardsClientUnitSchemaUId` must be the root schema's UId. MCP callers should read the `dashboard-creation` guidance (`get-guidance name=dashboard-creation`) for how to retrieve each value.
- To create a **desktop** (a workspace listed in the desktop selector), use `--template CentralAreaDesktopTemplate`. The created schema gets group `Desktop`, and the schema group is what makes the platform (`DesktopAppEventListener`) auto-register the desktop in the `Desktop` entity; clio never writes that record itself. Note that inheriting the template as a *parent* is not enough on its own — a page that keeps a non-`Desktop` group (e.g. the template's own `DesktopTemplate` group, which the generic page designer copies) will not appear in the selector; `create-page` stamps group `Desktop` for you. Deleting the desktop schema (`delete-schema --remote`) auto-removes its selector record. MCP callers should read the `desktop-page` guidance (`get-guidance name=desktop-page`) first.

- With `--entity-schema-name`, a **web** page is saved with a body that binds a data source to the entity, the way the Freedom UI designer does when a data source is set. The template decides the shape: a template with no primary data source (record, mini and blank page templates) gets a page-scoped `PDS` that becomes `primaryDataSourceName`, the shape of stock record pages such as `Accounts_FormPage`; a template that already names a primary data source gets that one bound; the list page template's `PDS` (a `viewElement` list source, not primary) is bound without being made primary. The response reports `dataSourceName` and, when it is primary, `primaryDataSourceName`. Without a primary data source, fields, `crt.Connections`, `crt.Feed` and the templates' `#DataSourceEntityName()#` / `#PrimaryDataSourceName()#` macros show nothing. A **mobile** page is not seeded: the entity is only recorded in its dependencies, and `note` says so.

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#create-page)
