using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// Provides canonical AI-facing guidance for editing Freedom UI page validators through clio MCP.
/// </summary>
[McpServerResourceType]
public sealed class PageSchemaValidatorsGuidanceResource {
	private const string DocsScheme = "docs";
	private const string ResourcePath = "mcp/guides/page-schema-validators";
	private const string ResourceUri = DocsScheme + "://" + ResourcePath;

	internal static readonly TextResourceContents Guide = new() {
			Uri = ResourceUri,
			MimeType = "text/plain",
			Text = """
			       clio MCP page-schema validators guide

			       Scope
			       - Use this guide when the task changes the `validators` section of a Freedom UI page body returned by `get-page`.
			       - Resolve exact MCP tool contracts through `get-tool-contract` before any write workflow.
			       - Keep validator work inside clio-owned page-body guidance instead of depending on an external repository.

			       Canonical runtime flow
			       - Prefer `list-pages -> get-page -> sync-pages -> get-page` for deployed page-schema validator edits.
			       - Use the `raw.body` field from the `get-page` response as the editable source of truth and preserve the outer AMD module structure.
			       - `SCHEMA_VALIDATORS` must contain an object section, not an array section.
			       - Treat `SCHEMA_VALIDATORS` as a JavaScript object section, not a strict JSON-only payload. Function-based validator entries are valid.

			       Decision tree
			       - If the requirement is field-value validation, continue here.
			       - If the requirement is dynamic `required`, `visible`, or `readonly` state, use business rules or `setAttributePropertyValue(...)`.
			       - First inspect the live page body: if it uses `viewModelConfig`, edit that object directly; if it uses `viewModelConfigDiff`, edit the merge on `path: ["attributes"]`.
			       - If the validator body actually `await`s I/O, use `"async": true`; otherwise use `"async": false`.

			       Standard validator decision table
			       - Prefer a standard Creatio validator when it already matches the requirement. Do NOT create a custom validator when a standard validator is sufficient.
			       - Use this table as the first-match decision rule before creating any `usr.*Validator`.
			         | Requirement pattern | Prefer | Parameters | Custom validator needed |
			         | --- | --- | --- | --- |
			         | field must be filled | `crt.Required` | none | no |
			         | field must not be whitespace-only | `crt.EmptyOrWhiteSpace` | none | no |
			         | minimum string length | `crt.MinLength` | `minLength` | no |
			         | maximum string length | `crt.MaxLength` | `maxLength` | no |
			         | minimum numeric value | `crt.Min` | `min` | no |
			         | maximum numeric value | `crt.Max` | `max` | no |
			         | requirement is not covered by rows above | custom `usr.*Validator` | custom | yes |
			       - Create a custom `usr.*Validator` only when the requirement is not covered by `crt.Required`, `crt.EmptyOrWhiteSpace`, `crt.MinLength`, `crt.MaxLength`, `crt.Min`, or `crt.Max`, or when the validation logic is genuinely domain-specific.
			       - All `crt.*` validators accept an optional `message` param to override the built-in localized error text (e.g., `"message": "#ResourceString(SomeKey)#"`). Omit `message` to use the automatic built-in error text. Only add `message` when you specifically need to override the default error.

			       NON-NEGOTIABLES
			       - Keep validators focused on field-value validation.
			       - Implement field validation as a validator entry, not as logic inside a handler.
			       - Name validator types with the `usr.` prefix, PascalCase body, and `Validator` suffix, for example `usr.UpperCaseValidator`. The local alias in `validators` may omit the `usr.` prefix.
			       - The `validators` property is an OBJECT, not an array.
			       - `validators.<Alias>.type` MUST equal the `SCHEMA_VALIDATORS` key.
			       - The validation error key MUST equal the validator type name.
			       - `"params"` MUST contain at minimum `{ "name": "message" }` — every custom validator requires a user-visible message. `"params": []` is NEVER valid.
			       - The error return MUST be `{ "<ValidatorType>": { message: config.message } }`. Never return `true`, `false`, `{}`, or a hardcoded string.
			       - Treat the binding-location, control-binding, resource-string, in-place-fix, and async CRITICAL sections below as hard requirements.

			       CRITICAL — Validator binding location
			       - Validators MUST be bound to the model attribute in `viewModelConfig` or `viewModelConfigDiff`, NOT to the UI element in `viewConfigDiff`.
			       - Binding to the `crt.Input` (or any UI element) in `viewConfigDiff` with a `validators` array does NOT invoke the validator at runtime — Creatio ignores it.
			       - For `viewModelConfigDiff`, use a merge operation on `path: ["attributes"]`.
			       - For `viewModelConfig`, add the `validators` property directly to the attribute object.
			       - Do NOT add a `validators` property inside the UI element in `viewConfigDiff`. Remove it if present.

			       CRITICAL — UI control binding when validators are used
			       - When a validator is registered on attribute `UsrName`, the `crt.Input` control MUST bind to `"$UsrName"`, NOT `"$PDS_UsrName"`.
			       - `"$PDS_AttrName"` is a direct data-source binding — Creatio does NOT run attribute validators on it.
			       - `"$AttrName"` is the view-model attribute binding — validators only fire on view-model attribute bindings, not on `$PDS_` data-source bindings.
			       - Rule: `control` in `viewConfigDiff` MUST reference the same attribute name as the one holding the `validators` object.
			       - Correct: `"control": "$UsrName"` (view-model attribute — validators fire)
			       - Wrong: `"control": "$PDS_UsrName"` (raw data-source field — validators are ignored)

			       CRITICAL — Fix control binding in the original operation, never add a patch merge
			       - When `"control": "$PDS_AttrName"` is wrong, fix the EXISTING insert or merge operation directly.
			       - NEVER add a second `merge` operation with the same `name` to override individual properties. This creates duplicate entries and is rejected by clio validation.
			       - Anti-pattern (REJECTED by clio):
			         viewConfigDiff: [
			           { "operation": "insert", "name": "UsrName", "values": { "control": "$PDS_UsrName" } },
			           { "operation": "merge", "name": "UsrName", "values": { "control": "$UsrName" } }
			         ]
			       - Correct — fix the original insert:
			         viewConfigDiff: [
			           { "operation": "insert", "name": "UsrName", "values": { "control": "$UsrName" } }
			         ]

			       CRITICAL — Resource string format in validator params
			       - Validator `params.message` MUST use `"#ResourceString(KeyName)#"` format.
			       - `"$Resources.Strings.KeyName"` is a reactive binding syntax for `viewConfigDiff` values and is NOT evaluated in validator params.
			       - Correct: `"params": { "message": "#ResourceString(UsrUpperCaseValidator_Message)#" }`
			       - Wrong: `"params": { "message": "$Resources.Strings.UsrUpperCaseValidator_Message" }`

			       CRITICAL — async vs sync validator
			       - Set `"async": false` when the validator body is a plain synchronous function: no `await`, no HTTP calls, no SDK lookups.
			       - Set `"async": true` ONLY when the inner function actually `await`s something.
			       - `async function` keyword alone does NOT require `"async": true` — if the function never awaits anything it is effectively synchronous and MUST use `"async": false`.

			       Name mapping
			       - Local alias example: `validators["UpperCaseValidator"]`.
			       - Validator type example: `"type": "usr.UpperCaseValidator"`.
			       - `SCHEMA_VALIDATORS` entry example: `"usr.UpperCaseValidator": { ... }`.
			       - Error return example: `return { "usr.UpperCaseValidator": { message: config.message } };`.
			       - Anti-example: do NOT return `{ "UpperCaseValidator": { message: config.message } }` because the error key must equal the validator type, not the local alias.

			       CRITICAL — Error return and params shape
			       - The validator MUST declare `{ "name": "message" }` in `params` so the runtime can pass the localized message.
			       - The inner function MUST return `{ "<ValidatorType>": { message: config.message } }` when invalid, and `null` when valid.
			       - Wrong returns that clio validation WILL REJECT:
			         - `return { "usr.OnlyDigits": true }` — primitive value causes a Creatio runtime error "Property message is not defined in validator params config"
			         - `return { "usr.OnlyDigits": {} }` — empty object: no message is shown to the user, params must not be empty
			         - `return { "usr.OnlyDigits": { message: "Only digits allowed" } }` with `params: []` — undeclared property rejected by runtime
			         - `"params": []` — ALWAYS wrong for a custom validator; use `[{ "name": "message" }]` at minimum
			       - Correct pattern (always use this shape):
			           "params": [{ "name": "message" }],
			           // in the inner function:
			           return { "usr.MyValidator": { message: config.message } };

			       Minimal canonical template
			       - Replace all placeholder identifiers consistently: `<AttrName>`, `<PdsPath>`, `<Alias>`, `<ValidatorType>`, `<MessageResourceKey>`.
			         viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[
			           {
			             "operation": "insert",
			             "name": "<AttrName>",
			             "values": {
			               "type": "crt.Input",
			               "control": "$<AttrName>"
			             }
			           }
			         ]/**SCHEMA_VIEW_CONFIG_DIFF*/,
			         viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[
			           {
			             "operation": "merge",
			             "path": ["attributes"],
			             "values": {
			               "<AttrName>": {
			                 "modelConfig": { "path": "<PdsPath>" },
			                 "validators": {
			                   "<Alias>": {
			                     "type": "<ValidatorType>",
			                     "params": { "message": "#ResourceString(<MessageResourceKey>)#" }
			                   }
			                 }
			               }
			             }
			           }
			         ]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/,
			         validators: /**SCHEMA_VALIDATORS*/{
			           "<ValidatorType>": {
			             "validator": function (config) {
			               return function (control) {
			                 const value = control.value;
			                 if (!value) return null;
			                 const isValid = <isValidCondition>;
			                 return isValid ? null : { "<ValidatorType>": { message: config.message } };
			               };
			             },
			             "params": [{ "name": "message" }],
			             "async": false
			           }
			         }/**SCHEMA_VALIDATORS*/

			       Complete page-body example (uppercase validator)
			       - Replace all placeholder identifiers consistently with live schema names.
			         viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[
			           {
			             "operation": "insert",
			             "name": "UsrName",
			             "values": {
			               "type": "crt.Input",
			               "label": "$Resources.Strings.UsrName",
			               "control": "$UsrName"
			             }
			           }
			         ]/**SCHEMA_VIEW_CONFIG_DIFF*/,
			         viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[
			           {
			             "operation": "merge",
			             "path": ["attributes"],
			             "values": {
			               "UsrName": {
			                 "modelConfig": { "path": "PDS.UsrName" },
			                 "validators": {
			                   "UpperCaseValidator": {
			                     "type": "usr.UpperCaseValidator",
			                     "params": { "message": "#ResourceString(UsrUpperCaseValidator_Message)#" }
			                   }
			                 }
			               }
			             }
			           }
			         ]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/,
			         validators: /**SCHEMA_VALIDATORS*/{
			           "usr.UpperCaseValidator": {
			             "validator": function (config) {
			               return function (control) {
			                 const value = control.value;
			                 if (!value || value === value.toUpperCase()) return null;
			                 return { "usr.UpperCaseValidator": { message: config.message } };
			               };
			             },
			             "params": [{ "name": "message" }],
			             "async": false
			           }
			         }/**SCHEMA_VALIDATORS*/

			       Static `viewModelConfig` variant
			       - If the live page body already uses `viewModelConfig`, add `validators` directly under `attributes.<AttrName>`.
			       - The control MUST bind to that attribute, not to the PDS field directly — use the view-model attribute binding when validators are present.
			         viewModelConfig: /**SCHEMA_VIEW_MODEL_CONFIG*/{
			           "attributes": {
			             "UsrName": {
			               "modelConfig": { "path": "PDS.UsrName" },
			               "validators": {
			                 "UpperCaseValidator": {
			                   "type": "usr.UpperCaseValidator",
			                   "params": { "message": "#ResourceString(UsrUpperCaseValidator_Message)#" }
			                 }
			               }
			             }
			           }
			         }/**SCHEMA_VIEW_MODEL_CONFIG*/
			       - Correct: `"control": "$UsrName"`
			       - Wrong: `"control": "$PDS_UsrName"`

			       Regex/pattern validator example
			       - Use `Minimal canonical template` for the binding structure, then change only the validator body for numeric or regex validation. Example:
			         "usr.SsnValidator": {
			           "validator": function (config) {
			             return function (control) {
			               const value = control.value;
			               if (!value) return null;
			               return /^\d{9}$/.test(value) ? null : {
			                 "usr.SsnValidator": { message: config.message }
			               };
			             };
			           },
			           "params": [{ "name": "message" }],
			           "async": false
			         }

			       Async validator template (SysSettingsService example)
			       - Use `Minimal canonical template` for the base binding structure, then apply this async variant only when the validator must call an external service or SDK method with `await`.
			       - `devkit` MUST be declared as an AMD dependency: add `"@creatio-devkit/common"` to `SCHEMA_DEPS` and `devkit` to the function args.
			       - The `viewConfigDiff` control binding still MUST be `"$UsrName"`, NOT `"$PDS_UsrName"`.
			         SCHEMA_DEPS / function args:
			           define("UsrPage", /**SCHEMA_DEPS*/["@creatio-devkit/common"]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/(devkit)/**SCHEMA_ARGS*/ {
			         validators: /**SCHEMA_VALIDATORS*/{
			           "usr.MaxLengthFromSysSettingValidator": {
			             "validator": function (config) {
			               return async function (control) {
			                 const value = control.value;
			                 if (!value) return null;
			                 const sysSettingsService = new devkit.SysSettingsService();
			                 const maxLength = await sysSettingsService.getByCode(config.settingCode);
			                 if (maxLength && value.length > Number(maxLength)) {
			                   return { "usr.MaxLengthFromSysSettingValidator": { message: config.message } };
			                 }
			                 return null;
			               };
			             },
			             "params": [{ "name": "settingCode" }, { "name": "message" }],
			             "async": true
			           }
			         }/**SCHEMA_VALIDATORS*/

			       BEFORE SAVE CHECKLIST
			       - The live body format was detected first: `viewModelConfig` vs `viewModelConfigDiff`.
			       - The validator lives under model attributes, not under a UI element in `viewConfigDiff`.
			       - The attribute `validators` property is an object and `type` matches the `SCHEMA_VALIDATORS` key.
			       - The control binds to the same attribute with `"$AttrName"`, not to `"$PDS_AttrName"`.
			       - No duplicate patch `merge` was added to override an existing control binding.
			       - `params.message` uses `#ResourceString(...)#`.
			       - `"params"` contains `{ "name": "message" }` — NOT an empty array `[]`.
			       - The inner function returns `{ "<ValidatorType>": { message: config.message } }` — NOT `true`, NOT `{}`, NOT a hardcoded string.
			       - The returned error key equals the validator type name.
			       - `"async"` matches the actual implementation.

			       Frontend-source pattern
			       - Ignore this section unless the task explicitly targets frontend source modules instead of runtime page bodies.
			       - For frontend-source modules, the public SDK registration pattern is `@CrtValidator({ type: 'usr.MyValidator' })` from `@creatio-devkit/common`.
			       - The public validator helpers include `BaseValidator`, `CrtControlState`, `CrtInject`, `CrtValidationErrors`, `CrtValidator`, `ValidatorConfig`, and `ValidatorParametersValues`.

			       Safe editing rules
			       - Edit only the minimal coupled sections required for validator correctness: `SCHEMA_VALIDATORS`, the attribute binding section (`viewModelConfig` or `viewModelConfigDiff`), the matching `viewConfigDiff` control binding, and `SCHEMA_DEPS` / `SCHEMA_ARGS` only when imports are required.
			       - For page-body work, reuse the live SDK alias already present in the schema body when imports are required.
			       - Verify the edited body is syntactically valid JavaScript before calling `sync-pages`.
			       - Keep validators free of navigation, data loading, save orchestration, or HTTP side effects.
			       """
		};

	/// <summary>
	/// Returns the canonical guidance article for editing validator sections in Freedom UI page bodies.
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "page-schema-validators-guidance")]
	[Description("Returns canonical MCP guidance for creating and editing Freedom UI page validators inside raw page schema bodies.")]
	public ResourceContents GetGuide() => Guide;
}
