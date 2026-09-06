---
description: an HTML page from EntitySchemaDesignerService.svc/GetSchemaDesignItem is the platform's SchemaIsNotAvailableException rendered by WCF - a missing package dependency, not a Creatio defect and not a stale database table
applies-to:
  - clio/Command/EntitySchemaDesigner/RemoteEntitySchemaDesignerClient.cs
  - clio/Command/EntitySchemaDesigner/RemoteEntitySchemaColumnManager.cs
  - clio/Command/EntitySchemaDesigner/EntitySchemaDependencyResolver.cs
ticket: "#722"
date: 2026-09-06
---

**What is true** — when the requesting package's dependency chain does not reach the package that owns the
upper layer of the object being extended, the platform raises `SchemaIsNotAvailableException` inside
`GetSchemaDesignItem`, and the WCF layer renders it as an HTML error page under HTTP 200. Reproduced on an
on-prem 10.1.725 stand: a fresh app package depending only on `CrtCore` failed on `Opportunity` and `Lead`,
succeeded on `Account` and `Contact`, and both failures cleared immediately after
`add-package-dependency --dependencies CrtLeadOppMgmtApp`. "Heavily layered schema" is correlation, not the
mechanism — `Account` has as many layer rows as `Opportunity` and works.

Two facts that follow from it and are easy to get wrong:

- The candidate set is never a single package. Measured on that stand: `Opportunity` 9, `Account` 9,
  `Contact` 16, `Lead` 20 — every standard schema is contributed by several packages, including every
  custom package that ever extended it. Intersecting with the installed applications ranks them but does
  not resolve them: it narrows `Opportunity` to one but leaves `Lead` at three.
- A rendered sign-in page is markup too. Classifying it as "the designer could not open the schema" makes
  an authentication failure look like a missing dependency, and — because the null that a markup body used
  to produce is what drove the auto-resolver — could make an expired session rewrite a package's dependency
  list.

**Why it is this way** — `GetSchemaDesignItem` has no equivalent of the `PackageElementDependencyApplier`
that `SaveSchema` runs, so the designer read simply fails where the write would have fixed itself. The
failure surfaces as markup rather than a fault envelope because it escapes the WCF operation rather than
being mapped to one.

**What breaks if you ignore it** — the error text is the only thing an agent sees, so a cause asserted
there is acted on. Before issue #722 the message named "a stale database table left by a previously deleted
package" as the second cause on the strength of a purely syntactic "the body starts with `<`" test, with no
check anywhere producing evidence for it; agents chased deleted packages and fell back to raw SQL. Assert a
cause only when the lookup that supports it has actually run, and name the packages it found — an
unevidenced cause in this message costs an agent a whole session.
