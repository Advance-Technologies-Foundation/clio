---
description: a fixture on McpContractFixtureBase runs every test on one clio mcp-server process, and that process keeps one DI container and one authenticated Creatio session per environment key (SessionContainerCache, 5-minute idle eviction) - later tests inherit the login the first test performed and rely on ReauthExecutor after a platform recycle, which is production behaviour, not test isolation
applies-to:
  - clio.mcp.e2e/Support/Mcp/McpContractFixtureBase.cs
  - clio.mcp.e2e/WorkspaceSyncToolE2ETests.cs
  - clio.mcp.e2e/ApplicationToolE2ETests.cs
  - clio/Command/McpServer/SessionContainerCache.cs
  - clio/Common/ReauthExecutor.cs
ticket: ENG-92558
date: 2026-09-09
---

**What is true** — `ToolCommandResolver` resolves each environment-bound call through
`SessionContainerCache.Acquire(cacheKey, …)`, which returns the container built by the first call for
that environment key until it has been idle for five minutes. The container owns the authenticated
`IApplicationClient`, so on a shared-server fixture tests 2..N run on the cookie test 1 obtained. When
Creatio recycles in between — `push-workspace`, `install-application` and package installs trigger the
delayed recycle the harness waits out in `ClioCliCommandRunner.WaitForEnvironmentRecoveryAsync` — the
next call carries a stale cookie and `ReauthExecutor` re-logs in once on the shapes it recognises (the
HTML `/Login/` page, the JSON `"Authentication failed."` envelope). Before the shared-server conversion
each test started its own process and therefore its own login, so this path was never exercised by the
suite; it is exercised now, in exactly the way a real agent's long-lived server exercises it.

**Why it is this way** — the cache is deliberate product behaviour: an MCP client keeps one server
alive for a whole working session, and re-authenticating per call would multiply login traffic and
trip the platform's login throttling. The e2e suite sharing the server per fixture is therefore a
truer model of production than one process per test was.

**What breaks if you ignore it** — two opposite mistakes. Treating the shared server as stateless
leads to a puzzling red: a test right after a recycle-triggering test fails with a login page or a
`Could not verify package requirements` gate refusal that a fresh process would not have seen; the
cause is a re-auth shape `ReauthExecutor` does not recognise, and the fix belongs in the product's
re-auth path or in the harness recovery wait — never in a per-test `McpServerSession.StartAsync`
that hides it again. Conversely, "fixing" a flaky Sandbox fixture by giving each test its own server
removes the only automated coverage of the cookie-refresh path and restores ~10 s of start-up per test.
If a fixture genuinely needs a fresh login for one test, start a private session in that test and dispose
it there (the DataForge proxy-poisoning test is the worked example), and say why in the test.
