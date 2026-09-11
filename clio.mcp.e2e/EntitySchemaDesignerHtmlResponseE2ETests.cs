using System.Text.Json;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Creatio;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end coverage for issue #722: when <c>GetSchemaDesignItem</c> answers with an HTML page, the error
/// an MCP agent receives must name what was observed and nothing else - no raw response body, and no
/// "stale database table left by a previously deleted package" claim, which the previous text asserted on
/// every HTML body with no check anywhere producing evidence for it.
/// <para>
/// Three server shapes are exercised, because clio must tell them apart: an ASP.NET server-error page (the
/// missing-dependency case, where the candidate packages must be named), a rendered sign-in page (an expired
/// session, which must NOT be reported as a package problem), and a bare markup fragment (the IIS/WAF shape
/// the previous doctype-prefixed test classified as generic garbage and previewed back to the caller).
/// </para>
/// </summary>
[TestFixture]
[Category("McpE2E.Sandbox")]
[AllureNUnit]
[AllureFeature(GetEntitySchemaPropertiesTool.GetEntitySchemaPropertiesToolName)]
[NonParallelizable]
public sealed class EntitySchemaDesignerHtmlResponseE2ETests {
	private const string RegisterToolName = "reg-web-app";
	private const string TargetPackageName = "UsrStubTarget";
	private const string TargetSchemaName = "Opportunity";
	private const string StaleTableClaim = "stale database table";
	private const string DeletedPackageClaim = "previously deleted package";

	[Test]
	[AllureTag(GetEntitySchemaPropertiesTool.GetEntitySchemaPropertiesToolName)]
	[AllureName("a designer HTML error page names the candidate packages and claims no stale table")]
	[AllureDescription("Registers an environment against a stub whose GetSchemaDesignItem answers with an ASP.NET error page, then verifies the surfaced error names the addable packages and the add-package-dependency fix, leaks no response body, and asserts no stale-table or deleted-package cause.")]
	[Description("A designer server-error page surfaces the ranked candidate packages and the one-call fix, with no raw HTML body and no stale-table claim.")]
	public async Task GetEntitySchemaProperties_Should_Name_Candidate_Packages_When_Designer_Returns_Error_Page() {
		await RunDesignerHtmlScenarioAsync(
			RuntimeDetectionStubServer.DesignerHtmlServerError,
			surfacedText => {
				surfacedText.Should().Contain("StubOwnerApp",
					because: "the packages that contribute the schema and are not already dependencies are the actionable part of the diagnosis");
				surfacedText.Should().Contain("add-package-dependency",
					because: "the agent must reach the one-call fix from the error itself, not only from a log warning");
				surfacedText.Should().NotContain(StaleTableClaim,
					because: "no check anywhere produces evidence for a stale table, so it must not be asserted (issue #722)");
				surfacedText.Should().NotContain(DeletedPackageClaim,
					because: "a deleted package is a cause clio never observed");
			});
	}

	[Test]
	[AllureTag(GetEntitySchemaPropertiesTool.GetEntitySchemaPropertiesToolName)]
	[AllureName("a designer sign-in page is reported as an authentication failure, not a package problem")]
	[AllureDescription("Registers an environment against a stub whose GetSchemaDesignItem answers with the rendered sign-in page, then verifies the surfaced error points at the credentials rather than at a package dependency, and that no package was modified.")]
	[Description("A designer sign-in page surfaces a credential error, never a missing-dependency diagnosis, and never triggers a dependency write.")]
	public async Task GetEntitySchemaProperties_Should_Report_Credentials_When_Designer_Returns_Login_Page() {
		await RunDesignerHtmlScenarioAsync(
			RuntimeDetectionStubServer.DesignerHtmlLoginPage,
			surfacedText => {
				surfacedText.Should().Contain("credentials",
					because: "the recovery for an expired session is a credential check, not a package change");
				surfacedText.Should().NotContain("add-package-dependency",
					because: "a sign-in response says nothing about packages, so it must not steer the agent into changing one");
				surfacedText.Should().NotContain(StaleTableClaim,
					because: "the removed stale-table claim must not survive on any branch");
			},
			assertNoDependencyWrite: true);
	}

	[Test]
	[AllureTag(GetEntitySchemaPropertiesTool.GetEntitySchemaPropertiesToolName)]
	[AllureName("a bare designer markup fragment is classified as markup, not previewed as garbage")]
	[AllureDescription("Registers an environment against a stub whose GetSchemaDesignItem answers with a bare div fragment, then verifies it takes the markup branch so the body is withheld instead of previewed.")]
	[Description("A bare markup fragment from the designer is classified as markup and its body is never surfaced.")]
	public async Task GetEntitySchemaProperties_Should_Withhold_Body_When_Designer_Returns_Markup_Fragment() {
		await RunDesignerHtmlScenarioAsync(
			RuntimeDetectionStubServer.DesignerHtmlFragment,
			surfacedText => {
				surfacedText.Should().Contain("HTML/XML page instead of JSON",
					because: "the fragment must take the markup branch; the previous doctype-prefixed test sent it to the branch that previews the body back to the caller");
				surfacedText.Should().NotContain(StaleTableClaim,
					because: "the removed stale-table claim must not survive on any branch");
			});
	}

	/// <summary>
	/// Registers a stub environment whose designer answers with <paramref name="designerHtmlMode"/>, calls
	/// <c>get-entity-schema-properties</c> scoped to the stub package, and applies the caller's assertions to
	/// everything the tool result carries. The body-leak assertion runs for every mode.
	/// </summary>
	/// <param name="designerHtmlMode">Which HTML shape the stub designer answers with.</param>
	/// <param name="assertSurfacedText">Mode-specific assertions on the surfaced text.</param>
	/// <param name="assertNoDependencyWrite">
	/// Whether to also assert that no package-properties write was issued. On this read path the resolver is
	/// never allowed to write anyway, so this is a belt-and-braces check on the whole pipeline rather than the
	/// proof of the session gate - that proof is
	/// <c>ModifyColumn_ShouldNotReachTheDependencyResolver_WhenTheSessionExpired</c>, which drives the write
	/// path where the resolver IS allowed to write.
	/// </param>
	private static async Task RunDesignerHtmlScenarioAsync(
		string designerHtmlMode,
		Action<string> assertSurfacedText,
		bool assertNoDependencyWrite = false) {
		// Arrange
		string tempHome = Path.Combine(Path.GetTempPath(), $"clio-designer-html-e2e-{Guid.NewGuid():N}");
		Directory.CreateDirectory(tempHome);
		try {
			string envVarName = OperatingSystem.IsWindows() ? "LOCALAPPDATA" : "HOME";
			McpE2ESettings settings = TestConfiguration.Load();
			settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
			settings.ProcessEnvironmentVariables[envVarName] = tempHome;
			using TemporaryClioSettingsOverride settingsOverride = TemporaryClioSettingsOverride.ReplaceContent(
				"""
				{
				  "ActiveEnvironmentKey": null,
				  "Environments": {}
				}
				""",
				settings.ClioProcessPath,
				settings.ProcessEnvironmentVariables);
			await using RuntimeDetectionStubServer stubServer = RuntimeDetectionStubServer.Start(
				new RuntimeDetectionStubServerConfiguration(
					NetCoreHealthEnabled: true,
					NetFrameworkHealthEnabled: true,
					NetCoreServiceEnabled: false,
					NetFrameworkServiceEnabled: true,
					NetCoreUiMarkerEnabled: false,
					NetFrameworkUiMarkerEnabled: true,
					DesignerHtmlMode: designerHtmlMode,
					DesignerPackageName: TargetPackageName,
					DesignerSchemaName: TargetSchemaName));
			using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
			await using McpServerSession session = await McpServerSession.StartAsync(
				settings, cancellationTokenSource.Token);
			string environmentName = $"designer-html-{Guid.NewGuid():N}";
			await RegisterEnvironmentAsync(
				session, environmentName, stubServer.BaseUrl, cancellationTokenSource.Token);

			// Act
			CallToolResult callResult = await session.CallToolAsync(
				GetEntitySchemaPropertiesTool.GetEntitySchemaPropertiesToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["environment-name"] = environmentName,
						["schema-name"] = TargetSchemaName,
						["package-name"] = TargetPackageName
					}
				},
				cancellationTokenSource.Token);

			// Assert
			string surfacedText = SerializeSurfacedText(callResult);
			surfacedText.Should().NotContain(RuntimeDetectionStubServer.DesignerHtmlBodyMarker,
				because: "an error or sign-in page can carry session tokens, so no layer may copy its body into an agent transcript");
			assertSurfacedText(surfacedText);
			if (assertNoDependencyWrite) {
				IReadOnlyList<RecordedStubRequest> recorded =
					await stubServer.GetRecordedRequestsAsync(cancellationTokenSource.Token);
				recorded.Should().NotContain(
					request => request.Url.Contains("SavePackageProperties", StringComparison.OrdinalIgnoreCase),
					because: "nothing on the sign-in-response path may reach a package-properties write");
				recorded.Should().NotContain(
					request => request.Url.Contains("PackageService.svc", StringComparison.OrdinalIgnoreCase),
					because: "the sign-in response must be classified before the dependency lookup runs, so the resolver is never reached at all");
			}
		} finally {
			TryDeleteDirectory(tempHome);
		}
	}

	/// <summary>
	/// Flattens everything the tool result carries (content blocks plus structured content) into one string,
	/// so the assertions cover whatever channel the failure was surfaced on.
	/// </summary>
	/// <param name="callResult">Tool result returned by the MCP server.</param>
	/// <returns>Serialized text of the whole result payload.</returns>
	private static string SerializeSurfacedText(CallToolResult callResult) =>
		JsonSerializer.Serialize(callResult.Content) + JsonSerializer.Serialize(callResult.StructuredContent);

	private static void TryDeleteDirectory(string path) {
		try {
			if (Directory.Exists(path)) {
				Directory.Delete(path, recursive: true);
			}
		} catch {
			// Best-effort cleanup of the isolated home directory; a leaked temp dir must not fail the test.
		}
	}

	private static async Task RegisterEnvironmentAsync(
		McpServerSession session,
		string environmentName,
		string baseUrl,
		CancellationToken cancellationToken) {
		IReadOnlyCollection<string> toolNames = await session.ListReachableToolNamesAsync(cancellationToken);
		toolNames.Should().Contain(RegisterToolName,
			because: $"the {RegisterToolName} MCP tool must be discoverable before the test can register the stub environment");

		CallToolResult registerResult = await session.CallToolAsync(
			RegisterToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["uri"] = baseUrl,
					["login"] = "Supervisor",
					["password"] = "Supervisor"
				}
			},
			cancellationToken);
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(registerResult);
		execution.ExitCode.Should().Be(0,
			because: "the stub environment must register successfully before the designer path can be exercised against it");
	}
}
