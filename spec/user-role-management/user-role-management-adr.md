# Native administration adapters

Status: accepted for implementation; access and license operations remain gated by
their individual runtime evidence. Coordination: clio#968.

## Decision

Use the existing Command<TOptions>, DI, IApplicationClient, ServiceUrlBuilder and
environment-aware BaseTool patterns. Group user, role, membership, license and access
operations into small command families with typed fields and explicit actions. Do
not accept an arbitrary schema, service URL, SQL statement or unconstrained column
dictionary as the administration mutation contract.

Use DataService SelectQuery with explicit columns for inspection. The lab can read
SysAdminUnit with OData, but OData rejects membership collections that DataService
successfully reads. A missing column is an error, not an empty result. Bound results
and expose pagination rather than silently truncating identities.

Use native administration service mutations. All fixed routes live in KnownRoute.
Use explicit timeouts and one transport attempt for mutations; after an uncertain
result, inspect the requested state before a caller retries. Do not introduce a
retry state store or transaction simulator.

## User lifecycle

Separate creation from membership. UpdateOrCreateUser's roleId only applies to new
rows and cannot repair a failed membership step on retry. Pre-read explicit user IDs
before updates or resets so a typo cannot create a partial new user. Use an explicit
contact ID and user kind on creation. Preserve absent update fields.

Expose password reset separately from account unlock and activation. Use a secret
input source rather than repurposing the existing authentication --password option.
Never return or log the secret, request body, password hash or raw remote fault.
Do not combine login rename with password mutation. Respect native password policy,
2FA and externally managed identity boundaries. Document reset/expiry behavior.

## Roles and membership

Create/update through SaveRole. Validate lookup IDs and role/parent types first.
Inspect Manager children by ParentRole and type=2 before SaveChiefsRole. Return the
existing unique child; fail on duplicates. Organization/division insertion on the
verified runtime auto-creates that child. Managers are membership-bearing roles
whose inherited scope differs from ordinary organizational membership.

Use AddUserRoles for grants requiring the platform's second-factor check. Native
AddUsersInRole lacks that check on the inspected version; never use it to bypass a
challenge for a System administrators grant. Membership removal uses native removal
checks, including the last-administrator safeguard. Verify direct relationships and
actualize effective membership explicitly; actualization failure is not success.

## Responses

Administration responses wrap MethodNameResult. The result can be an encoded JSON
object with lower/upper-case success, an empty string meaning success, a nonempty
error string, a boolean, or encoded read data. Parse the expected contract for each
operation and fail closed on absent/malformed envelopes. Treat partial writes as
possible when the native method reports failure. Keep remote prose out of default
diagnostics; use existing untrusted-text handling for necessary validation detail.

## Access and licenses

IP access ranges, delegated rights, operation permissions and record rights are
different concepts. Reuse existing record-right tooling. Add only verified native
paths for the others. License reads must retain source/capacity information; license
updates affect only explicitly selected assignments. No license purchasing,
unlicensed entitlement fabrication or blanket redistribution.

## Rejected alternatives

Three narrow ClioGate methods fill verified gaps: removing a functional association with administration
authorization and conditional native license scheduling; explicitly scheduling native redistribution;
and invalidating native backend/browser rights caches after priority changes. The first two require
ClioGate 2.0.0.50. Priority requires 2.0.0.52 and Creatio 10.1.585.0; its cache bridge is called as an
authorization/capability preflight and again after native ordering. Native ordering alone left stale
backend decisions in live tests. Public platform APIs own the invalidation; no grant is rewritten.

- Generic SysAdminUnit OData writes: reproduce the original protected-schema failure.
- Direct SQL or a generic privileged gateway: bypasses platform event and policy paths.
- Automatically creating a manager after every role: live test produced duplicates.
- Generic HTTP-success classification: live ordinary-user unblock failed inside a
  nested result while generic call-service exited zero.
- Automatic retries of user+role create: a failed membership can leave the user row
  committed and an identical retry does not add membership.
- Reasserting grant values to invalidate priority caches: can overwrite a concurrent administrator's change.

## KISS check

The intended flow remains inspect -> validate -> one native mutation -> inspect
effects. Existing DI, transport and MCP dispatch own their existing responsibilities.
No new orchestration layer, permissions model, global locks or recovery database.
