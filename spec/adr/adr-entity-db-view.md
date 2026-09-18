# Entity database-view flag

Expose the platform's existing `IsDBView` property through entity creation, sync creation,
and schema-property updates. Reuse the existing designer DTO and save/publish pipeline:
Creatio's database installer already excludes DB views from table generation.

Omitted values preserve inherited/current metadata. Setting the flag does not create SQL,
convert an existing table, or change `IsVirtual`. Package SQL remains responsible for the view.
Sync rejects an existing schema whose explicitly requested DB-view kind differs; callers use
the explicit property-update command to change it. This avoids silently changing storage kind.

Validation: argument mapping, omitted/true/false values, sync kind collisions, readback failures,
and real MCP creation/property updates on a disposable PostgreSQL Creatio instance.
