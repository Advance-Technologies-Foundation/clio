# ADR: use persisted references for section deletion

Status: accepted for issue #1634.

Choose an allow-list of persisted page identities plus a separately opted-in, uniquely resolved entity. Read SysModule because the ApplicationSection virtual projection omits persisted card and entity-binding identifiers on the validated runtime. Preserve independent SysModuleEdit references and unknown auxiliary ownership. Reject failed or capped reference discovery.

A naming convention cannot prove ownership; replacing StartsWith with guessed suffixes still risks unrelated data. A full dependency graph or rollback protocol is unnecessary for the confirmed defect. Preservation is the safe default when ownership cannot be proven. Native deletion failures remain visible and may be partial; this change does not claim transactionality or universal dependency discovery.
