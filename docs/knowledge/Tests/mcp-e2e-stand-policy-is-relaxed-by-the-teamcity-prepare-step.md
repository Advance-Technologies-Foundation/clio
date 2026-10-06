---
description: the per-build Team_Atf_ClioMcpE2eTests stand is deployed with DenyCustomQueryApiUsage=true and xml in FileExtensionsDenyList; the TeamCity step "Prepare site" relaxes both, so SQL-read and xml-upload e2e cases pass only because of a TeamCity-side script that lives outside this repository
applies-to:
  - clio.mcp.e2e/ExecuteSqlScriptToolE2ETests.cs
  - clio.mcp.e2e/DownloadSysSettingFileE2ETests.cs
ticket: GH-1509
date: 2026-09-25
---

**What is true** — the shared build-engine target `ApplyDeployedTestApplicationTransformations`
(`engineering/build-engine`, `BPMonline.Build.Publish.targets`, called for every deployment with
`IsDeployTestApplication=true`) unconditionally runs `DenyCustomQueryApiUsage`, which writes
`<add key="DenyCustomQueryApiUsage" value="true" />` into `Terrasoft.WebApp\Web.config`. The stand's default
`FileExtensionsDenyList` system setting also contains `xml`. Build configuration `Team_Atf_ClioMcpE2eTests`
undoes both for its own disposable stand in step "Prepare site: reg-web-app + install-gate": it sets the Web.config
key to `false` before the login-readiness gate, and after `install-gate` it removes only `xml` from
`FileExtensionsDenyList` with `clio set-syssetting`. The step logs every change with a `[stand-policy]` prefix.

**Why it is this way** — the build-engine target is shared by every acceptance lane, and the stand owners chose in
clio#1509 to relax the policy for this lane only, not for all test deployments. There is no `.teamcity/` DSL in this
repository, so the script lives only in the TeamCity build configuration.

**What breaks if you ignore it** — a new e2e case that needs another denied operation fails only on TeamCity, while a
local stand with different defaults passes. If the prepare-step block is removed or the build configuration is
copied without it, the nine `ExecuteSqlScriptToolE2ETests` success cases fail with
`Usage of CustomQuery.ExecuteReader is denied by application security settings.` and the `xml` case of
`DownloadSysSettingFileE2ETests` fails with the `DenyList file-security policy` refusal. The assertion messages carry
those texts, so read them before blaming clio.
