using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources.ProcessDesigner;

/// <summary>
/// Canonical AI-facing guidance for adding a Freedom UI button that runs a business process
/// (<c>crt.RunBusinessProcessRequest</c>) into a page schema via <c>update-page</c>.
/// </summary>
[McpServerResourceType]
public sealed class RunProcessButtonGuidanceResource {
	private const string DocsScheme = "docs";
	private const string ResourcePath = "mcp/guides/run-process-button";
	private const string ResourceUri = DocsScheme + "://" + ResourcePath;

	/// <summary>
	/// Returns the canonical guidance article for authoring a run-process button config.
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "run-process-button-guidance")]
	[Description("Returns canonical MCP guidance for adding a Freedom UI button that runs a business "
		+ "process (crt.RunBusinessProcessRequest) via update-page, covering static constants, "
		+ "view-model attribute binding, and the current-record variant.")]
	public ResourceContents GetGuide() => Guide;

	internal static readonly TextResourceContents Guide = new() {
			Uri = ResourceUri,
			MimeType = "text/plain",
			Text = """
			       clio MCP run-process-button guide

			       Goal: add a Freedom UI button that starts a business process. Reuse the built-in
			       request `crt.RunBusinessProcessRequest` (handled by the platform RunBusinessProcessHandler).
			       Do NOT write a custom handler — the `handlers` block stays empty for this button.

			       MANDATORY pre-write step
			       - Call `get-process-signature process-name=<code-or-caption> environment-name=<env>` FIRST.
			       - You may pass the name the user gave (the display caption, e.g. "Business process 1") OR the
			         schema code (e.g. UsrProcess_e629820). The tool resolves both and echoes the resolved
			         `processCode` — put THAT code into the button's `processName` (never the caption).
			       - If the caption matches more than one process the tool returns a failure listing candidate
			         codes; pick one and re-run with the exact code.
			       - The button must reference the process CODE (schema name, e.g. `UsrProcess_e629820`) as
			         `processName`, and every parameter key must be the parameter CODE (the `name` field from
			         the signature) — NOT the display caption.
			       - CRITICAL: if a parameter key does not match a real parameter code, the Creatio core
			         SILENTLY drops the value (it logs a warning and still returns success=true). There is no
			         runtime error. So validate parameter codes against the signature BEFORE calling update-page.
			       - Match the value type to the parameter `clrType` from the signature. For a Lookup parameter
			         (`isLookup=true`) pass a Guid value, not display text.

			       Button skeleton (insert into a container via update-page, mode append)
			       - Pick `parentName` from `get-page` -> `bundle.containers` (NEVER guess a literal name).
			         Choose by the page's action-button region, not by copying the example below:
			         * Record form / edit page (FormPage): use `ActionButtonsContainer` — the record action bar
			           next to Save/Close. This is the default target for a run-process button on a form page.
			           Do NOT use `MainHeaderTop`: that is the page title header, not the action area.
			         * List / grid page, or a form with no `ActionButtonsContainer`: pick the relevant action bar
			           from `bundle.containers` (a `crt.FlexContainer` with `childCount` > 0 in the header region,
			           commonly `MainHeaderTop` on list pages).
			         Always confirm the chosen name actually exists in `bundle.containers` before writing.
			       - The `clicked` config is `{ request: 'crt.RunBusinessProcessRequest', params: {...} }`.
			       - Caption must be a localizable binding — pass the key via the `resources` parameter.

			       Build the button from `get-component-info crt.Button`; wire the run-process request into its
			       `clicked` binding using one of the variants below. Insert it with `update-page` (mode `append`)
			       into the chosen `parentName`,
			       leaving `handlers: []` — no custom handler. See `page-modification` for the body envelope and
			       the insert op (`operation` / `name` / `parentName` / `propertyName` / `index`).

			       Behavior flags (defaults to emit, change only when the user asks)
			       By default include both, set to true:
			         "saveAtProcessStart": true,   // save the current record before the process starts
			         "showNotification": true      // show a "process started" toast on click
			       Set either to false only when the user explicitly does not want that behavior.
			       - notificationText: do NOT include by default — the platform shows a default "process started"
			         message. Add it ONLY when the user wants to OVERRIDE that text. It is user-visible, so author
			         it as a localizable binding "#ResourceString(<ButtonName>_clicked_params_notificationText)#"
			         and register the text via the resources parameter.

			       Variant V3 — pass the current record Id (form/edit page)
			       Use when the process has a Guid/Lookup input parameter that should receive the open record.
			       The handler reads the primary data source record Id and injects it into the named parameter.
			       {
			           "request": "crt.RunBusinessProcessRequest",
			           "params": {
			               "processName": "UsrProcess_e629820",
			               "processRunType": "ForTheSelectedPage",
			               "saveAtProcessStart": true,
			               "recordIdProcessParameterName": "ProcessSchemaParameter1"
			           }
			       }
			       - `recordIdProcessParameterName` must be a parameter CODE from the signature.
			       - For a Lookup parameter, prefer one whose reference schema matches the page's primary entity.

			       Variant V2 — bind a parameter to a view-model attribute
			       Binding expressions ($Attr) inside params (including nested processParameters) are resolved
			       against the view model at runtime. The OOTB designer does NOT produce this — it is agent-only.
			       {
			           "request": "crt.RunBusinessProcessRequest",
			           "params": {
			               "processName": "UsrProcess_e629820",
			               "processRunType": "RegardlessOfThePage",
			               "processParameters": { "ProcessSchemaParameter2": "$SomeViewModelAttribute" }
			           }
			       }
			       The attribute is declared in viewModelConfigDiff (the nesting under values.attributes is required):
			       viewModelConfigDiff: [
			           {
			               "operation": "merge",
			               "path": [],
			               "values": {
			                   "attributes": {
			                       "SomeViewModelAttribute": { "value": "Hello!" }      // constant; or:
			                       // "SomeViewModelAttribute": { "modelConfig": { "path": "PDS.UsrSomeColumn" } }  // DS-bound
			                   }
			               }
			           }
			       ]
			       - `$SomeViewModelAttribute` references the attribute KEY (same spelling/case).
			       - The attribute value type must be compatible with the parameter clrType (Guid for Lookup).

			       Variant V1 — static constants (shows the default behavior flags)
			       {
			           "request": "crt.RunBusinessProcessRequest",
			           "params": {
			               "processName": "UsrProcess_e629820",
			               "processRunType": "RegardlessOfThePage",
			               "saveAtProcessStart": true,
			               "showNotification": true,
			               "processParameters": { "ProcessSchemaParameter2": "Hello!" }
			           }
			       }

			       VALUE ENCODING (verified end-to-end against a live process)
			       Both quoted strings and bare JSON literals work — the RunProcess contract coerces every value
			       to a string and the engine parses it per the parameter type. All of these were confirmed by
			       reading the parameters inside the started process:
			         - string:  "Some text"
			         - number:  99.99 or "99.99" / 42 or "42"   (both forms work)
			         - boolean: true or "true"                  (both forms work)
			         - guid / lookup: "11111111-1111-1111-1111-111111111111"  (a real, parseable Guid string)
			         - DateTime: ISO-8601 "2026-12-31T23:59:59Z" (Z is converted to local) — works
			         - Date:     "2026-12-31" — works;  Time: "18:45:00" — works


			       FULL PARAMETER CONTRACT (this guide is the single source of truth — the page-schema-handlers
			       and mobile-page guides only point here; keep this list in sync with the platform contract)
			       - processName                  (string, REQUIRED) — the process CODE from get-process-signature.
			       - processRunType               (string, REQUIRED) — see the reference below.
			       - processParameters            (object) — { "<ParameterCODE>": value }; keys are CODES, not captions.
			       - parameterMappings            (object) — { "<ParameterCODE>": "<sourceColumn>" }; keys are CODES.
			       - recordIdProcessParameterName (string) — parameter CODE that receives the current/selected record Id.
			       - resultParameterNames         (string[]) — process OUTPUT parameter CODES to read back.
			       - dataSourceName               (string) — datasource used by ForTheSelectedRecords.
			       - filters / sorting            (object) — record selection for ForTheSelectedRecords.
			       - selectionStateAttributeName  (string) — attribute holding the grid selection state.
			       - showNotification / notificationText / saveAtProcessStart — see "Behavior flags" above.
			       - Mobile-only: activeRow / activeRowAttributeName — current row context on a mobile list.

			       processRunType reference
			       - `RegardlessOfThePage` — run globally, no record context (V1/V2).
			       - `ForTheSelectedPage` — run for the current form record (V3).
			       - `ForTheSelectedRecords` — run for grid-selected records; pair with dataSourceName /
			         filters / sorting / selectionStateAttributeName. NOTE: accepted by the web and mobile
			         runtime, but the mobile designer does not yet emit it (ENG-87164) — author it for web for now.

			       resources parameter
			       - Register the caption key you used, e.g. resources = {"RunBusinessProcessButton_caption":"Run process"}.

			       update-page notes
			       - Use mode `append` to add the button without overwriting existing customizations
			         (it dedupes viewConfigDiff by `name`).
			       - See `page-modification-components` for the body envelope and container selection, and
			         `page-modification-overview` for the "do not resend the full raw.body" rule.
			       """
		};
}
