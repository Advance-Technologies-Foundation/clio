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
  list. That is why the sign-in response is now classified before the markup test.
- **The failing body carries no evidence of its own cause.** Captured through a proxy on the 10.1.725 stand,
  `GetSchemaDesignItem` answers a genuine `SchemaIsNotAvailableException` with WCF's stock page — 1647 bytes
  reading `<title>Request Error</title>` and "The server encountered an error processing the request. See
  server logs for more details." It names no exception type, no schema and no package, and is byte-shaped
  exactly like a WAF block, a 502 proxy page or any other unhandled server fault. **No content-based gate
  can tell them apart**, which is why clio reports ranked candidates and never adds a dependency by itself:
  a dependency added on a transient fault that then clears looks like a success and leaves the package
  permanently changed. Do not reintroduce an auto-add here without a signal that does not exist today.
- That same captured body **begins with a U+FEFF byte-order mark**. Any predicate that decides what a body
  is by looking at its first character must skip a BOM as well as whitespace; `char.IsWhiteSpace` does not
  report one.

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

The same rule bites on the post-save verification reload. The write, the DB-structure save and the publish
have all succeeded by then, so a reload that cannot see the schema is the refresh window described above —
never a missing dependency. A missing-dependency message there tells the caller a succeeded write failed,
and an agent responds by repeating the mutation or adding a dependency it does not need.
