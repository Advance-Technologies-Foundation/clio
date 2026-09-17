# Bounded DataService batch writes

Issue: clio#1577, parent clio#1574.

Agents need to write a bounded set of explicit records in one native DataService request. Existing single-record commands remain compatible. Accept 1–100 insert/update/delete operations, each naming a schema and record UUID, with typed scalar values. Updates and deletes always target that exact UUID. No arbitrary filters, job system, automatic retries, or generic sequence activation workflow.

Return correlated per-item outcomes and counts. Missing, malformed or uncorrelated results are unknown, never inferred successful or not attempted. Native continuation is enabled so independent errors do not hide subsequent outcomes. No atomicity guarantee. Enrolled participants and activities remain owned by native sequence services.
