#!/usr/bin/env bash
# Prints every PARAMETER-caption resource row of the schemas whose name starts with $1, plus each schema's
# ModifiedOn - the evidence that tells "describe shows it stale" from "it is SAVED stale".
# $1 is REQUIRED: the name prefix of the fixtures to read (the research used UsrCapD and UsrCapE).
# Uses `clio sql` (cliogate) against the clio environment $CLIO_ENV (default: Creatio).
# CLIO_DLL: a clio build whose bundled CrtProcessBuilder matches the stand (default: this repo's Release build).
# MSSQL ONLY: the queries use [Key] and CONVERT(varchar(23), ..., 121). Rewrite both for PostgreSQL.
set -euo pipefail
PREFIX="${1:?usage: capsql.sh <schema-name-prefix>}"
if [[ ! "$PREFIX" =~ ^[A-Za-z0-9_]+$ ]]; then
	echo "capsql.sh: the prefix must be letters, digits and underscores only: it is spliced into SQL" >&2
	exit 2
fi
CLIO_DLL="${CLIO_DLL:-clio/bin/Release/net10.0/clio.dll}"
ENV="${CLIO_ENV:-Creatio}"
# The filter drops clio's log and table-frame lines. It runs AFTER the query's own exit status is captured,
# because a grep at the end of a pipeline would otherwise hide a failed query.
run_sql() {
	local output
	output="$(dotnet "$CLIO_DLL" sql "$1" -e "$ENV" 2>&1)" || { printf '%s\n' "$output" >&2; return 1; }
	printf '%s\n' "$output" | grep -v -E '^\[INF\]|^ -+ $|^$' || true
}
run_sql "SELECT s.Name AS SchemaName, v.[Key] AS ResKey, c.Name AS Culture, v.Value AS ResValue, CONVERT(varchar(23), v.ModifiedOn, 121) AS ResModified FROM SysLocalizableValue v JOIN SysSchema s ON s.Id = v.SysSchemaId JOIN SysCulture c ON c.Id = v.SysCultureId WHERE s.Name LIKE '${PREFIX}%' AND v.[Key] LIKE '%Parameters.%' ORDER BY s.Name, v.[Key], c.Name"
run_sql "SELECT Name, CONVERT(varchar(23), ModifiedOn, 121) AS SchemaModified FROM SysSchema WHERE Name LIKE '${PREFIX}%' ORDER BY Name"
