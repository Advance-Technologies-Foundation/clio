# ClioGate SQL log design

Use one native entity, `ClioSqlRequestLog`, shipped in both ClioGate archives. Store full normalized executor SQL in a maximum-length text column, creator/time, completion flag, elapsed milliseconds, row count (-1 when unknown) and error text. Use platform Insert/Update with parameters. Persist the initial row before invoking CustomQuery; if this fails, do not execute unlogged SQL. Update the row after SQL completes or fails. A completion-log failure must not turn a successful SQL write into a reported SQL failure; report it through the server logger with the request ID instead.

Keep execution classification and response formats unchanged. Count SELECT results from the already materialized table and DML rows from Execute; unknown counts are stored as -1. Measure execution and result materialization, excluding persistence and JSON serialization. Dispose the reader and DataTable. Reuse SQLFunctions without introducing another service layer or endpoint.

The log is an administrative diagnostic containing the full SQL by explicit request. Configure entity access for administrators and preserve the existing CanManageSolution endpoint check. This is not a tamper-proof audit trail for administrators who can execute arbitrary SQL.
