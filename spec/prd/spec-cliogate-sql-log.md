# ClioGate SQL request log

Issue: #397. Persist the complete SQL submitted to the executor before execution, then update the same record with elapsed milliseconds and returned/affected row count when known. Preserve existing SQL responses and platform authorization. Validate the bundled package on a new disposable Creatio instance, including long Unicode SQL, pending requests, failures, empty results and DML.

SQL logging is the scope; result streaming, SQL parsing, retention jobs and a log UI are excluded.
