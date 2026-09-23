#!/usr/bin/env bash
# Prints every PARAMETER-caption resource row of the schemas whose name starts with $1 (default UsrCapM),
# plus each schema's ModifiedOn - the evidence that tells "describe shows it stale" from "it is SAVED stale".
# Uses `clio sql` (cliogate) against the clio environment $CLIO_ENV (default: Creatio).
# CLIO_DLL: a clio build whose bundled CrtProcessBuilder matches the stand (default: this repo's Release build).
CLIO_DLL="${CLIO_DLL:-clio/bin/Release/net10.0/clio.dll}"
ENV="${CLIO_ENV:-Creatio}"; PREFIX="${1:-UsrCapM}"
dotnet "$CLIO_DLL" sql "SELECT s.Name AS SchemaName, v.[Key] AS ResKey, c.Name AS Culture, v.Value AS ResValue, CONVERT(varchar(23), v.ModifiedOn, 121) AS ResModified FROM SysLocalizableValue v JOIN SysSchema s ON s.Id = v.SysSchemaId JOIN SysCulture c ON c.Id = v.SysCultureId WHERE s.Name LIKE '${PREFIX}%' AND v.[Key] LIKE '%Parameters.%' ORDER BY s.Name, v.[Key], c.Name" -e "$ENV" 2>&1 | grep -v -E '^\[INF\]|^ -+ $|^$'
dotnet "$CLIO_DLL" sql "SELECT Name, CONVERT(varchar(23), ModifiedOn, 121) AS SchemaModified FROM SysSchema WHERE Name LIKE '${PREFIX}%' ORDER BY Name" -e "$ENV" 2>&1 | grep -v -E '^\[INF\]|^ -+ $|^$'
