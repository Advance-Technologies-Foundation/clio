# Studio handoff validation

- TC-U-1: malformed schema, unsafe names/URLs/paths, duplicate paths and unresolved
  required inputs fail before mutation; diagnostics never include secret values.
- TC-U-2: checkout uses full recorded commit and has no deployment dependency;
  deploy/status have no source dependency.
- TC-I-1: real Git remote, pinned worktree, rerun, dirty preservation, wrong origin,
  mismatched HEAD and symlink escape.
- TC-U-3: input precedence, generated-secret retry, exact placeholder substitution,
  ownership refusal and bounded process errors.
- TC-U-4: operator validates every phase before writes; namespace and object
  ownership enforced; missing prerequisites block rather than mutate them.
- TC-U-5: completed Jobs remain completed, failed Jobs fail, pending workloads
  remain pending, observed generation prevents stale readiness.
- TC-I-2: host MCP contract guards and malformed profile behavior through a real
  MCP process, with no cluster required for checkout validation.
- TC-L-1: real Rancher install from package with prebuilt images and no repository
  checkout; fresh login, Twin reply, Builder build/deploy/reply.
- TC-L-2: repeat deployment preserves credentials/data and unrelated CRM; source
  checkout separately proves recorded SHAs.
