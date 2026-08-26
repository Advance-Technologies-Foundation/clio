---
description: cliogate hosts four [ServiceContract] classes, so /rest/PackagesGateway/, /rest/ATFLogService/ and /rest/FeatureStateService/ are cliogate endpoints too, not platform ones
applies-to:
  - cliogate/Files/cs/CreatioApiGateway.cs
  - cliogate/Files/cs/PackagesGateway.cs
  - cliogate/Files/cs/ATFLogService.cs
  - cliogate/Files/cs/Feature/FeatureStateService.cs
  - clio/Common/ServiceUrlBuilder.cs
date: 2026-08-19
---

**What is true** — the cliogate package exposes **four** `[ServiceContract]` classes, each served at
`/rest/<ClassName>/<MethodName>`: `CreatioApiGateway`, `PackagesGateway`, `ATFLogService` and
`Feature/FeatureStateService`. Any clio call to one of those four route prefixes therefore requires
cliogate to be installed in the target environment. Live examples in clio:
`ServiceUrlBuilder.KnownRoutes` maps `StartLogBroadcast`/`StopLogBroadcast` to `/rest/ATFLogService/…`,
`FeatureCommand.ServicePath` is `/rest/FeatureStateService/SetFeatureState`, and `Program.cs` builds
three `/rest/PackagesGateway/…` URLs.

**Why it is this way** — `AGENTS.md` documents only the `CreatioApiGateway` prefix ("All ClioGate
methods are served at `/rest/CreatioApiGateway/<MethodName>`"). That is the rule for *adding* an
endpoint, not an inventory of what cliogate already serves, and the other three prefixes read like
platform services because their names carry no gateway marker.

**What breaks if you ignore it** — you audit "which commands need cliogate" by grepping for
`CreatioApiGateway`, conclude that `listen`, `pull-pkg --async` and `set-feature
--use-feature-web-service` are package-free, and ship them ungated. On an environment without
cliogate they fail with a raw 404 or an HTML error page instead of the install-gate hint, and the user
has no way to tell a missing package from a broken command.
