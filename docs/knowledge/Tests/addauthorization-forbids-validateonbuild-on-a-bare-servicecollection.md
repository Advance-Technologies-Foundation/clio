---
description: a bare ServiceCollection that went through AddAuthorization cannot be built with ValidateOnBuild=true - it fails with "Unable to resolve service for type 'Microsoft.AspNetCore.Routing.EndpointDataSource'" while activating AuthorizationPolicyCache
applies-to:
  - clio.tests/Command/McpServer/McpHttpAuthenticationTests.cs
  - clio/Command/McpServer/McpHttpAuthentication.cs
ticket: ENG-93386
date: 2026-08-19
---

**What is true** — `McpHttpAuthentication.ConfigureServices` calls `AddAuthorization`, which registers the
singleton `Microsoft.AspNetCore.Authorization.Policy.AuthorizationPolicyCache`. That type's constructor takes
`Microsoft.AspNetCore.Routing.EndpointDataSource`, which only a routing-configured web host supplies. So a unit
test that composes a plain `ServiceCollection` and calls `BuildServiceProvider` must NOT pass
`ValidateOnBuild = true`; `ValidateScopes = true` alone is fine. Verified directly: a four-line program with
`AddLogging` + `AddAuthorization` and `ValidateOnBuild = true` throws
`Unable to resolve service for type 'Microsoft.AspNetCore.Routing.EndpointDataSource' while attempting to activate
'Microsoft.AspNetCore.Authorization.Policy.AuthorizationPolicyCache'`.

**Why it is this way** — `ValidateOnBuild` eagerly validates every registered descriptor, including framework
descriptors that are only ever resolved inside a real host. The authentication tests therefore assert the
registration by RESOLVING what they care about (`IAuthenticationSchemeProvider.GetSchemeAsync`,
`IAuthorizationPolicyProvider.GetPolicyAsync`) instead of leaning on build-time validation.

**What breaks if you ignore it** — adding `ValidateOnBuild = true` to those tests (a natural move, since clio's own
container is validated on build and other fixtures use the flag) turns them red for a reason that has nothing to do
with clio. The message names a ROUTING type, so it reads as a missing package reference or a bad TFM pin rather
than "this registration needs a web host", and the usual reaction is to add a package or change the JwtBearer pin.
