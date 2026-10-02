# reset-navigation-cache

## Name

reset-navigation-cache - Clear the menu cache (module structure, workplaces, sections) of clio's own Creatio session

## Description

Calls `ConfigurationDataService/GetData` with `forceGet = true` in clio's own Creatio session. The server drops that session's cached module structure, reloads its workplace and section caches, and bumps the client cache hash. The command changes no data and logs nobody out.

Creatio caches the left menu (`WorkplaceNavigationPanelService/GetGroups`) and the module structure per server session in Redis. Writes to `SysModule` or `SysModuleInWorkplace` (for example `odata-update` or data-binding upserts) clear none of it, so run this command after such writes when later clio calls must see the change. `create-app`, `create-app-section` and `update-app-section` already do it.

The command does **not** refresh other sessions or open browser tabs. A tab refreshes its menu only when it receives the `ConfigurationStructureChanged` websocket message, so a tab that was not connected at that moment keeps the old menu across reloads. The command prints the call that fixes such a tab; run it in the developer console of that tab, then reload the tab:

```js
fetch('<site>/0/rest/ConfigurationDataService/GetData', {method:'POST', headers:{'Content-Type':'application/json', BPMCSRF:document.cookie.match(/BPMCSRF=([^;]+)/)[1]}, body:'true'})
```

The printed URL is the environment's own: `.NET Framework` sites have the `0/` prefix, `.NET` (Core) sites do not.

Do not clear Redis (`clear-redis-db`) to fix a stale menu: that logs out every user of the environment.

## Synopsis

```bash
clio reset-navigation-cache -e <ENVIRONMENT_NAME>
```

## Options

```bash
-e, --environment <ENVIRONMENT_NAME>
Target environment name

-u, --uri <URI>
Application URI (instead of -e)

-l, --Login <LOGIN>
User login (administrator permission required)

-p, --Password <PASSWORD>
User password
```

## Examples

```bash
clio reset-navigation-cache -e dev
Clear the menu cache of clio's session for the dev environment
```

## MCP

The `reset-navigation-cache` MCP tool takes `environment-name` (optional only under credential passthrough, which supplies the environment itself). It returns `success`, `error` when the server did not confirm the reset, and `next-step` with the in-tab call above, on success and on failure. It is not destructive and is idempotent.

## Notes

- Exit code 0 means the server answered `success: true`. Any other answer, or a transport failure, is printed as an error and exits with 1.
- The in-tab call is printed in both cases, because the command never reaches a browser session.

## See Also

create-app - Create a new application in Creatio
update-app-section - Update metadata of a section inside an existing installed application
clear-redis-db - Clear redis database

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#reset-navigation-cache)
