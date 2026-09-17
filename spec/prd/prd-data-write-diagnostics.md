# Safe write diagnostics

Issue: clio#1578; parent clio#1574.

Agents must distinguish local rejection, an acknowledged write, and uncertain side effects without losing safe platform validation details. Add optional structured diagnostics to native enrollment, DataService batch and retained OData write results. Preserve existing fields and single-record clients. Include operation, entity, input index where available, write-attempt and side-effect certainty. Never infer an offending field or HTTP status absent from the transport response.

Reuse current redaction, untrusted-text fencing and failure models. No automatic retries or new error framework. Existing CLI redaction issue #1505 remains separately owned.
