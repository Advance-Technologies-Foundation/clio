---
description: on a TeamCity agent the Creatio sandbox is routed as http://<agent>:88/<pool> while the agent-local IIS application may be a root app under different bindings, so the destructive-E2E application pool must come from the ApplicationPoolName build parameter, not from the registered URL
applies-to:
  - clio.mcp.e2e/Support/Configuration/IisApplicationPoolResolver.cs
  - clio.mcp.e2e/Support/Configuration/TeamCityBuildParameterResolver.cs
  - clio.mcp.e2e/UninstallCreatioWarningE2ETests.cs
date: 2026-08-19
---

**What is true** — the public URL a TeamCity build records for its Creatio sandbox
(`http://<agent>:88/<pool>` style) is a routed address whose path segment is not an IIS application
path. The same instance can be served locally as a root application under entirely different
bindings, so matching the URL path against `appcmd list app` output identifies the wrong
application or none. The build's own `ApplicationPoolName` parameter is the authority;
`IisApplicationPoolResolver` accepts it, then requires that it match either the routed URI target
or the direct IIS topology AND have exactly one live application assignment
(`SharedIisApplicationPoolException` otherwise).

**Why it is this way** — TeamCity reports build parameters through two properties files:
`TEAMCITY_BUILD_PROPERTIES_FILE` and, for configuration parameters, the
`teamcity.configuration.properties.file` it points at. `TeamCityBuildParameterResolver` follows
both hops because a configuration parameter is absent from the first file.

**What breaks if you ignore it** — the destructive uninstall-warning E2E stops and deletes an
unrelated IIS application, or removes a pool shared with another site on the same agent. This is
irreversible on a build agent and does not surface as a test failure — the test passes while
something else on the agent is gone. Evidence: TeamCity build 15736567, where URL-only inference
resolved the wrong target.
