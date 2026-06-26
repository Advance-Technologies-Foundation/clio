using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// Canonical guidance for the <c>describe-environment</c> MCP tool: what the single, source-independent
/// environment report contains, which source supplies each field, and the auth/cliogate prerequisites.
/// </summary>
[McpServerResourceType]
public sealed class DescribeEnvironmentGuidanceResource {
	private const string DocsScheme = "docs";
	private const string ResourcePath = "mcp/guides/describe-environment";
	private const string ResourceUri = DocsScheme + "://" + ResourcePath;

	/// <summary>
	/// Canonical guidance article accessible by name through <c>get-guidance</c>.
	/// </summary>
	internal static readonly TextResourceContents Guide = new() {
		Uri = ResourceUri,
		MimeType = "text/plain",
		Text = """
		       clio MCP describe-environment guide

		       PURPOSE
		       - describe-environment returns ONE source-independent JSON report for a Creatio instance.
		         The field SET is the same with or without cliogate; only the cliogate-only fields drop out
		         when cliogate is absent. Reason about every environment the same way.
		       - Read-only. It never mutates the environment.
		       - Target the environment with environment-name (PREFERRED). uri/login/password is an emergency
		         fallback only when no environment is registered.

		       BEST-EFFORT CONTRACT (exit 0 even when an optional source is missing)
		       The report is assembled from up to three sources, in order. A field's ABSENCE means the source
		       that supplies it was unavailable (older Creatio, cliogate not installed, or the caller lacks the
		       admin permission) — it is NOT an error. Only productName and licenseInfo strictly require cliogate.

		       1) ALWAYS — ApplicationInfoService.GetApplicationInfo (authenticated session, no permission gate,
		          no cliogate):
		          - coreVersion           Creatio platform version (e.g. "8.2.1.xxxx"). Use this to decide whether
		                                  a component/feature exists before assuming availability.
		          - environmentType       Configured environment label (SysSetting "EnvironmentType"), may be "".
		          - maintainer            Package maintainer code (e.g. "Customer", "Creatio").
		          - user / userContact / userAccount   Logged-in user, contact and account (id + display name).
		          - userCulture / primaryCulture / primaryLanguage   Locale of the user and of the system.
		          - userTimezoneOffset / userTimezoneCode            User timezone (minutes offset + code).
		          - workspace             Current workspace (id + display name).
		          - moneyDisplayPrecision / maxEntitySchemaNameLength / freedomUiSchemaVersion   Platform limits.

		       2) WITHOUT cliogate — ApplicationInfoService.GetSystemEnvironmentInfo (admin-gated POST; requires
		          the CanManageSolution system operation; exists only on newer Creatio):
		          - dbEngineType          Database engine: "MSSql" | "PostgreSql" | "Oracle".
		          - frameworkKind         Executing framework family: "Net" (.NET / .NET Core) | "NetFramework".
		          - frameworkDescription  Detailed runtime string (e.g. ".NET 8.0.11", ".NET Framework 4.8").
		          If CanManageSolution is not granted or the operation is absent, these are skipped silently
		          (and may instead be backfilled from cliogate in step 3).

		       3) cliogate ONLY — GET /rest/CreatioApiGateway/GetSysInfo (cliogate >= 2.0.0.32 installed):
		          - productName           Creatio product/edition name (e.g. "studio"). NO core web service
		                                  exposes this — it is the one field that always needs cliogate.
		          - licenseInfo           License metadata object: CustomerId, IsDemoMode (and related fields).
		                                  NOTE: CustomerId is the customer's licensing identifier — treat it as
		                                  sensitive; do not echo or paste it outside this environment context.
		          cliogate also BACKFILLS dbEngineType / frameworkKind / frameworkDescription when step 2 did
		          not provide them (older Creatio without GetSystemEnvironmentInfo), keeping the shape consistent.

		       WHEN TO USE
		       - Verify the platform version (coreVersion) before planning page/component work — pair with the
		         get-component-info "latest-fallback" warning.
		       - Read dbEngineType + frameworkKind/frameworkDescription for deploy and troubleshooting decisions
		         (now available WITHOUT cliogate when CanManageSolution is granted).
		       - Confirm productName / license status before applying edition-specific features.

		       INTERPRETING A SPARSE REPORT
		       - Missing dbEngineType/framework AND missing productName/licenseInfo  -> cliogate absent and the
		         caller lacks CanManageSolution (or the env is old): grant CanManageSolution, or install cliogate
		         (install-gate) for the full report.
		       - Present db/framework but missing productName/licenseInfo            -> cliogate not installed;
		         everything except product and license is still reported.
		       """
	};

	/// <summary>
	/// Returns the canonical describe-environment guidance article.
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "describe-environment-guidance")]
	[Description("Returns the canonical clio MCP guidance for describe-environment: the single source-independent environment report, which source supplies each field (ApplicationInfoService, the admin-gated GetSystemEnvironmentInfo, cliogate GetSysInfo), and the cliogate / CanManageSolution prerequisites.")]
	public ResourceContents GetGuide() => Guide;
}
