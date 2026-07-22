using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// Canonical AI-facing guidance for wiring Freedom UI page actions to platform requests
/// (<c>crt.*Request</c>): when to reach for a request, and the mandatory
/// <c>get-request-info</c> catalog discipline that replaces authoring request names and
/// parameters from memory. OOTB button-action requests initiative (ENG-93187).
/// </summary>
[McpServerResourceType]
public sealed class WhenToUseRequestsGuidanceResource {
	private const string DocsScheme = "docs";
	private const string ResourcePath = "mcp/guides/when-to-use-requests";
	private const string ResourceUri = DocsScheme + "://" + ResourcePath;

	/// <summary>
	/// Returns the canonical guidance article for choosing and wiring Freedom UI requests.
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "when-to-use-requests-guidance")]
	[Description("Returns canonical MCP guidance for wiring Freedom UI page actions to platform requests: "
		+ "when to reuse a built-in crt.*Request, the mandatory get-request-info catalog discipline "
		+ "(parameters, baseParameters, per-request documentation), and the anti-patterns.")]
	public ResourceContents GetGuide() => Guide;

	internal static readonly TextResourceContents Guide = new() {
		Uri = ResourceUri,
		MimeType = "text/plain",
		Text = """
		       clio MCP when-to-use-requests guide

		       Goal: wire a Freedom UI page action (a button's `clicked`, a menu item, any request-binding
		       output) to a platform request the right way — pick a cataloged `crt.*Request`, fetch its
		       authoritative contract with `get-request-info`, and author `params` strictly from that
		       contract. Never author request names or parameter lists from memory.

		       When to reach for a request
		       - One-step built-in page action (close / save / cancel / delete / create / open / refresh /
		         export / import / copy / print): wire the built-in `crt.*Request` directly from the page
		         config. No custom handler — the platform ships one. The decision tree, the
		         declarative-vs-imperative shape rules, and the broad request-name table live in
		         `get-guidance name=page-schema-handlers`; this guide owns HOW to pick the request and get
		         its parameter contract.
		       - Custom logic BEFORE a built-in action: keep the SAME `crt.*` request wiring and add a
		         `handlers` entry for it that ends with `return next?.handle(request);` — do not clone the
		         behavior under a `usr.*` name. Returning without calling `next` swallows the request and
		         the built-in action never runs.
		       - Genuinely custom domain workflow no built-in request covers: a `usr.*Request` plus a
		         custom handler (authoring rules in `page-schema-handlers`).
		       - Run a business process: resolve the process with `get-process-signature` FIRST, then
		         `get-request-info request-type=crt.RunBusinessProcessRequest` - its `parameters` and
		         `documentation` carry the full contract (parameter keys are CODES, not captions).

		       MANDATORY catalog discipline (get-request-info)
		       - Before wiring, call `get-request-info` in list mode (omit `request-type`, or pass 'list')
		         to discover the cataloged requests; use `search` for keyword narrowing.
		       - For the chosen request call `get-request-info request-type=<crt.X>` and read:
		         * `parameters` — the ONLY keys allowed in the binding's `params` block. An EMPTY
		           `parameters` map means the request accepts NO parameters: emit no `params` block at all.
		           The runtime silently ignores unknown keys — a wrong key never errors, it just does
		           nothing, so an invented parameter is a silent no-op bug.
		         * `baseParameters` (`$context`, `scopes`, `type`) — fields every request inherits from
		           BaseRequest, injected by the platform at dispatch time. NEVER put them into `params`.
		         * `references.typeDefinitions` — the schema for every non-primitive parameter type,
		           including `RequestBindingConfig` (the binding contract itself).
		         * `documentation` — the per-request authoring recipe (canonical wiring, pitfalls,
		           checklist) when the producer published one. Follow it over memory; its recipes are
		           verified against platform sources and production schemas.
		       - HARD RULE for environment-dependent values: a parameter carrying a `valueSource`
		         annotation (kind `environment`) is filled ONLY from the result of the probe tool the
		         annotation names - e.g. `templateId` -> `list-printables`, `processName` ->
		         `get-process-signature`. NEVER invent, guess, or copy such a value from memory or
		         examples; when the probe returns nothing or several candidates, ask the user. A made-up
		         value passes save and fails silently at runtime.
		       - Catalog coverage is incremental: a `crt.*` request missing from the catalog is NOT proof
		         it does not exist. When the catalog has no entry, fall back to the request-name table in
		         `page-schema-handlers`; when both surface a request, the catalog detail is authoritative
		         for parameters.

		       Version scoping
		       - Pass `environment-name` so the catalog matches the target environment's real platform
		         version. When the response carries `resolvedFrom: "latest-fallback"` it also sets
		         `requiresVersionConfirmation: true` — tell the user the version is unknown and get
		         explicit confirmation before authoring against the `latest` superset.

		       Web vs mobile
		       - Default catalog is the WEB request registry. When wiring a request on a MOBILE page
		         (a page whose `get-page` metadata reports `schema-type: "mobile"`, i.e. schemaType 10),
		         pass `schema-type: "mobile"` — the mobile request catalog is a SEPARATE registry, scoped
		         to the requests available on Freedom UI mobile, and a request's parameters can differ
		         from desktop. Never author a mobile request's params from the web catalog.

		       Wiring shape (declarative page config)
		       - `"clicked": { "request": "crt.XxxRequest" }` — add `"params": { ... }` ONLY when the
		         catalog lists parameters for the request.
		       - The config shape is `request` + `params`; the imperative handler-dispatch shape is
		         `type` + flat payload fields (+ `$context` / `scopes`). Never mix the two shapes — the
		         full rule and examples live in `page-schema-handlers`.

		       Anti-patterns
		       - Inventing parameters. The runtime ignores unknown `params` keys silently; the button
		         looks wired but the value goes nowhere.
		       - Passing `$context`, `scopes`, or `type` through `params` — platform-injected, never
		         authored.
		       - Re-implementing a built-in request's behavior in a custom `usr.*` handler instead of
		         wiring the `crt.*` request (or chaining in front of it).
		       - Authoring a request name from memory when the catalog is one call away — names are
		         case-sensitive contracts, and a mistyped request silently does nothing at runtime.
		       - A pre-action handler that does not return `next?.handle(request)` — the chain stops and
		         the built-in action is lost.

		       Quick checklist (before update-page)
		       - [ ] `get-request-info` list consulted; the request comes from the catalog (or from the
		             `page-schema-handlers` table when the catalog has no entry yet)
		       - [ ] detail fetched; `params` authored strictly from `parameters` (empty map -> no
		             `params` block at all)
		       - [ ] no platform-injected fields (`$context` / `scopes` / `type`) inside `params`
		       - [ ] `environment-name` passed, or the `latest-fallback` superset explicitly confirmed
		             with the user
		       - [ ] per-request `documentation` followed when present
		       """
	};
}
