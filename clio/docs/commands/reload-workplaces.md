# reload-workplaces

## Command Type

    CI/CD commands

## Name

reload-workplaces - publish navigation changes to signed-in users without a re-login

## Description

Reloads the platform navigation caches on a Creatio environment so a workplace
change becomes visible to users who are already signed in. After this command
succeeds, a plain page refresh is enough and no log out / log in cycle is
required.

Workplace, section, and edit-page lists are cached **per session**, which is why
a browser refresh alone shows nothing after you create a workplace, move a
section between workplaces, grant a role, or point a workplace's home page at a
new page. Creatio invalidates those caches from an entity event listener on
`SysUserInRole` / `SysAdminUnitInWorkplace` insert and delete only, so a section
move or a home-page change invalidates nothing — and rows written straight
through the database engine (the `*-data-binding-*-db` commands) raise no entity
events at all. This command calls the platform's own reload contract
(`IWorkplaceManager.ReloadWorkplaces()`) directly, so any navigation change can
be published.

Run it as the **last** step of a navigation change. Running it earlier leaves
the writes that follow unpublished.

## Synopsis

```bash
reload-workplaces [options]
```

## Aliases

```bash
reload-navigation, rlwp
```

## Options

```bash
--uri               -u          Application uri

--Password          -p          User password

--Login             -l          User login (administrator permission required)

--Environment       -e          Environment name

--Maintainer        -m          Maintainer name
```

## Requirements

Requires `cliogate` on the target environment. Install or update it with:

```bash
clio install-gate -e dev
```

## Examples

```bash
clio reload-workplaces -e dev
```

```bash
clio rlwp -e dev
```

## Exit codes

| Code | Meaning |
|---|---|
| 0 | The navigation caches were reloaded. Tell users to refresh the page; no re-login is needed. |
| 1 | The reload did not happen. The error message names the reason. The navigation change itself is still applied — but users must log out and back in to see it. |

## Notes

- This is not a cache flush. Unlike [`clear-redis-db`](clear-redis-db.md), which
  empties the whole application cache, this command asks the platform to reload
  only its navigation state.
- A restart is never required for a navigation change; do not use
  [`restart-web-app`](restart-web-app.md) for this.
- For the write recipes this command completes — creating a workplace, moving a
  section, granting a role, binding a home page, and shipping each change as
  package data — read the `workplaces` guidance article
  (`clio mcp-server` → `get-guidance name=workplaces`).
