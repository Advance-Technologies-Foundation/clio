---
description: right after an OData rebuild Creatio reloads its route table and any request in that window is answered HTTP 200 success:false "Collection was modified", often with an empty errorInfo.message; reads must go through SelectQueryHelper's transient retry, never a private copy
applies-to:
  - clio/Package/SelectQueryHelper.cs
  - clio/Command/ApplicationInfoService.cs
ticket: ENG-102683
date: 2026-10-08
---

**What is true** — saving a section (`create-app-section`, `update-app-section`) starts a background
OData rebuild (`WorkspaceCompiler.BuildOData`). When it finishes, the web app reloads its route table,
and a request that arrives in that moment fails inside `System.Web.Routing.RouteCollection.GetRouteData`
with `Collection was modified; enumeration operation may not execute.`. The platform answers HTTP 200
with `success: false`, so `ExecutePostRequest`'s transport retry never sees it, and the stand logs of
three failed CI builds showed `errorInfo.message` empty: the text is only somewhere else in the body.
Any endpoint is affected, `SelectQuery` and `RuntimeEntitySchemaRequest` included.

`SelectQueryHelper.ExecuteSelectQuery` re-sends such reads (issue #1119), and
`SelectQueryHelper.SendWithTransientRetry` gives any other idempotent read the same budget and markers.
`SelectQueryHelper.DescribeServerFailure` treats a blank `errorInfo.message` as absent and falls back to
the raw body — otherwise both the transient classifier and the final error see an empty string.

**Why it is this way** — the window is milliseconds long and clears on its own, so a bounded re-send of
a read is the only fix on clio's side; a write must not be re-sent blindly.

**What breaks if you ignore it** — `ApplicationInfoService` used to declare its own private
`ExecuteSelectQuery`. Because the file also has `using static Clio.Package.SelectQueryHelper;`, the
private method silently won overload resolution over the shared one, so the #1119 retry never applied
to `get-app-info`, and `update-app-section` failed intermittently with only `Select query failed.`
(the `ApplicationSectionUpdateToolE2ETests` caption-culture flake). The call sites now qualify
`SelectQueryHelper.ExecuteSelectQuery` explicitly; a new private helper with the same name in a file
that imports the class statically reintroduces the defect with no compiler warning.
