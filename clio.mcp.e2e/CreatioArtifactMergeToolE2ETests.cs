using Allure.NUnit;
using Allure.NUnit.Attributes;
using System.Text.Json;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the preview-only Creatio semantic merge MCP tool.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[Category("McpE2E.Manual")]
[Explicit("Developer-local merge tests: excluded from GitHub Actions and TeamCity; run explicitly after local review.")]
[AllureNUnit]
[AllureFeature(CreatioArtifactMergeTool.ToolName)]
[NonParallelizable]
public sealed class CreatioArtifactMergeToolE2ETests : McpContractFixtureBase {
	private string _isolatedClioHome = null!;

	private protected override void ConfigureMcpServerSettings(McpE2ESettings settings) {
		TeamCityRunGuard.IgnoreIfRunningUnderTeamCityOrGitHubActions(
			"Creatio artifact merge E2E tests are developer-local and must not run in GitHub Actions or TeamCity.");
		_isolatedClioHome = CreateIsolatedClioHome("{}", "creatio-merge-home");
		settings.ProcessEnvironmentVariables["CLIO_HOME"] = _isolatedClioHome;
	}

	[Test]
	[Description("Runs the real CLI process against preserved Creatio-authored Git stage files.")]
	[AllureTag(CreatioArtifactMergeTool.ToolName)]
	[AllureDescription("Runs the real CLI against preserved EntitySchema Git stages and verifies independent columns survive the semantic merge.")]
	[AllureName("Creatio artifact merge CLI combines EntitySchema columns")]
	public async Task MergeCreatioArtifactCli_ShouldMergeEntitySchemaStages_WhenFilesAreExplicit() {
		// Arrange
		string repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
		string fixtureRoot = Path.Combine(repositoryRoot, "lab", "creatio-three-way-merge", "fixtures", "entity-schema");
		McpE2ESettings settings = ResolveSettings();
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));

		// Act
		ClioCliCommandResult commandResult = await ClioCliCommandRunner.RunAsync(
			settings,
			[
				CreatioArtifactMergeTool.ToolName,
				"--artifact-path", "packages/UsrMergeProof/Schemas/UsrMergeProofEntity/metadata.json",
				"--base-file", Path.Combine(fixtureRoot, "base-metadata.json"),
				"--ours-file", Path.Combine(fixtureRoot, "ours-metadata.json"),
				"--theirs-file", Path.Combine(fixtureRoot, "theirs-metadata.json"),
				"--descriptor-file", Path.Combine(fixtureRoot, "descriptor.json")
			],
			cancellationToken: cancellationTokenSource.Token);
		CreatioArtifactMergeResult? result = JsonSerializer.Deserialize<CreatioArtifactMergeResult>(
			commandResult.StandardOutput);

		// Assert
		commandResult.ExitCode.Should().Be(0,
			because: $"the installed CLI should resolve the preserved independent additions. stderr: {commandResult.StandardError}");
		result.Should().NotBeNull(because: "the CLI contract returns the shared structured merge result as JSON");
		result!.Status.Should().Be("resolved", because: "the two Creatio-authored branches add different columns");
		result.Content.Should().Contain("UsrDeveloperAText", because: "developer A's column must survive the merge");
		result.Content.Should().Contain("UsrDeveloperBNumber", because: "developer B's column must survive the merge");
		result.Report.VerificationPassed.Should().BeTrue(because: "CLI output is safe only after resolver verification");
	}

	[Test]
	[Description("Returns a failing CLI exit code and explicit not-implemented JSON for BusinessProcess metadata.")]
	[AllureTag(CreatioArtifactMergeTool.ToolName)]
	[AllureDescription("Runs the real CLI against recognized BusinessProcess metadata and verifies the explicit not-implemented contract.")]
	[AllureName("Creatio artifact merge CLI rejects BusinessProcess metadata clearly")]
	public async Task MergeCreatioArtifactCli_ShouldReturnNotImplemented_WhenProcessSchemaIsRecognized() {
		// Arrange
		const string metadata = """
		= MetaData.Schema.UId "22222222-2222-2222-2222-222222222222"
		= MetaData.Schema.A2 "UsrProofProcess"
		= MetaData.Schema.ManagerName "ProcessSchemaManager"
		""";
		const string descriptor = """
		{"Descriptor":{"UId":"22222222-2222-2222-2222-222222222222","Name":"UsrProofProcess","ManagerName":"ProcessSchemaManager"}}
		""";
		string fixtureRoot = CreateFixtureDirectory("process-merge-cli");
		string basePath = Path.Combine(fixtureRoot, "base.json");
		string oursPath = Path.Combine(fixtureRoot, "ours.json");
		string theirsPath = Path.Combine(fixtureRoot, "theirs.json");
		string descriptorPath = Path.Combine(fixtureRoot, "descriptor.json");
		await File.WriteAllTextAsync(basePath, metadata);
		await File.WriteAllTextAsync(oursPath, metadata);
		await File.WriteAllTextAsync(theirsPath, metadata);
		await File.WriteAllTextAsync(descriptorPath, descriptor);
		McpE2ESettings settings = ResolveSettings();
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));

		// Act
		ClioCliCommandResult commandResult = await ClioCliCommandRunner.RunAsync(
			settings,
			[
				CreatioArtifactMergeTool.ToolName,
				"--artifact-path", "packages/Test/Schemas/UsrProofProcess/metadata.json",
				"--base-file", basePath,
				"--ours-file", oursPath,
				"--theirs-file", theirsPath,
				"--descriptor-file", descriptorPath
			],
			cancellationToken: cancellationTokenSource.Token);
		CreatioArtifactMergeResult? result = JsonSerializer.Deserialize<CreatioArtifactMergeResult>(
			commandResult.StandardOutput);

		// Assert
		commandResult.ExitCode.Should().Be(1,
			because: "a recognized but unimplemented artifact must fail closed for shell automation");
		result.Should().NotBeNull(because: "the refusal must remain a structured result rather than an opaque error");
		result!.Status.Should().Be("not-implemented", because: "BusinessProcess semantic merge is outside the supported slice");
		result.Diagnostics.Should().Equal(["Merge for process-schema-metadata is not implemented yet."],
			because: "the CLI must state the explicit supported-type boundary");
		result.Content.Should().BeNull(because: "not-implemented results must not expose content as safe to apply");
	}

	[Test]
	[Description("Advertises merge-creatio-artifact directly on the resident MCP surface.")]
	[AllureTag(CreatioArtifactMergeTool.ToolName)]
	[AllureDescription("Lists tools from the real MCP server and verifies the merge tool is resident with its explicit supported-type contract.")]
	[AllureName("Creatio artifact merge tool is resident")]
	public async Task MergeCreatioArtifact_Should_Be_Listed() {
		// Arrange
		await using var context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		IList<McpClientTool> tools = await context.Session.ListToolsAsync(
			context.CancellationTokenSource.Token);
		McpClientTool mergeTool = tools.Single(tool => tool.Name == CreatioArtifactMergeTool.ToolName);

		// Assert
		mergeTool.Description.Should().Contain("EntitySchema",
			because: "tool discovery must state the primary supported Creatio artifact family");
		mergeTool.Description.Should().Contain("ProcessSchema", because: "discovery must expose the explicit roadmap boundary");
		mergeTool.Description.Should().Contain("not implemented", because: "agents must not guess textual fallback for recognized process artifacts");
		mergeTool.JsonSchema.ToString().Should().Contain("artifact-path",
			because: "the live tools/list schema must advertise the repository-relative classification evidence");
	}

	[Test]
	[Description("Starts the real MCP server and merges the preserved two-developer EntitySchema stages, retaining both independently added columns.")]
	[AllureTag(CreatioArtifactMergeTool.ToolName)]
	[AllureDescription("Invokes the resident tool through stdio MCP and verifies independent EntitySchema columns survive.")]
	[AllureName("Creatio artifact merge combines EntitySchema columns")]
	public async Task MergeCreatioArtifact_Should_Merge_EntitySchema_Stages() {
		// Arrange
		await using var context = Arrange(TimeSpan.FromMinutes(3));
		string repositoryRoot = Path.GetFullPath(Path.Combine(
			AppContext.BaseDirectory,
			"..",
			"..",
			"..",
			".."));
		string fixtureRoot = Path.Combine(
			repositoryRoot,
			"lab",
			"creatio-three-way-merge",
			"fixtures",
			"entity-schema");
		Dictionary<string, object?> args = new() {
			["artifact-path"] = "packages/UsrMergeProof/Schemas/UsrMergeProofEntity/metadata.json",
			["base-content"] = await File.ReadAllTextAsync(Path.Combine(fixtureRoot, "base-metadata.json")),
			["ours-content"] = await File.ReadAllTextAsync(Path.Combine(fixtureRoot, "ours-metadata.json")),
			["theirs-content"] = await File.ReadAllTextAsync(Path.Combine(fixtureRoot, "theirs-metadata.json")),
			["descriptor-content"] = await File.ReadAllTextAsync(Path.Combine(fixtureRoot, "descriptor.json"))
		};

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			CreatioArtifactMergeTool.ToolName,
			new Dictionary<string, object?> { ["args"] = args },
			context.CancellationTokenSource.Token);
		CreatioArtifactMergeResult result = EntitySchemaStructuredResultParser
			.Extract<CreatioArtifactMergeResult>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a valid semantic merge is a domain result, not an MCP transport failure");
		result.Status.Should().Be("resolved",
			because: "the two branches add different EntitySchema columns from the same base");
		result.ArtifactKind.Should().Be("entity-schema-metadata",
			because: "descriptor identity should explicitly classify the supported schema type");
		result.Content.Should().Contain("UsrDeveloperAText",
			because: "the merged metadata must retain the first developer's column");
		result.Content.Should().Contain("UsrDeveloperBNumber",
			because: "the merged metadata must retain the second developer's column");
		result.Report.VerificationPassed.Should().BeTrue(
			because: "clean content is exposed only after semantic verification");
	}

	[Test]
	[Description("Returns the explicit not-implemented status for BusinessProcess metadata over the real MCP transport.")]
	[AllureTag(CreatioArtifactMergeTool.ToolName)]
	[AllureDescription("Invokes recognized BusinessProcess metadata through MCP and verifies the explicit not-implemented response.")]
	[AllureName("Creatio artifact merge rejects BusinessProcess metadata clearly")]
	public async Task MergeCreatioArtifact_Should_Return_NotImplemented_For_ProcessSchema() {
		// Arrange
		await using var context = Arrange(TimeSpan.FromMinutes(3));
		const string metadata = """
		= MetaData.Schema.UId "22222222-2222-2222-2222-222222222222"
		= MetaData.Schema.A2 "UsrProofProcess"
		= MetaData.Schema.ManagerName "ProcessSchemaManager"
		""";
		const string descriptor = """
		{"Descriptor":{"UId":"22222222-2222-2222-2222-222222222222","Name":"UsrProofProcess","ManagerName":"ProcessSchemaManager"}}
		""";

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			CreatioArtifactMergeTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["artifact-path"] = "packages/Test/Schemas/UsrProofProcess/metadata.json",
					["base-content"] = metadata,
					["ours-content"] = metadata,
					["theirs-content"] = metadata,
					["descriptor-content"] = descriptor
				}
			},
			context.CancellationTokenSource.Token);
		CreatioArtifactMergeResult result = EntitySchemaStructuredResultParser
			.Extract<CreatioArtifactMergeResult>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "recognized unsupported roadmap types return a readable domain result");
		result.Status.Should().Be("not-implemented",
			because: "BusinessProcess semantic merge is outside the implemented slice");
		result.ArtifactKind.Should().Be("process-schema-metadata",
			because: "the response must name the recognized Creatio schema type");
		result.Diagnostics.Should().Equal(["Merge for process-schema-metadata is not implemented yet."],
			because: "agents need the promised clear refusal instead of a generic resolver error");
		result.Content.Should().BeNull(
			because: "not-implemented outcomes must never expose content as safe to write");
	}

	[Test]
	[Description("Rejects unknown merge arguments through the real MCP JSON binder instead of silently ignoring them.")]
	[AllureTag(CreatioArtifactMergeTool.ToolName)]
	[AllureDescription("Sends an unknown merge argument through the real MCP server and verifies strict contract rejection.")]
	[AllureName("Creatio artifact merge rejects unknown arguments")]
	public async Task MergeCreatioArtifact_Should_Reject_Unknown_Argument() {
		// Arrange
		await using var context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			CreatioArtifactMergeTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["artifact-path"] = "packages/Test/descriptor.json",
					["base-content"] = "base",
					["ours-content"] = "ours",
					["theirs-content"] = "theirs",
					["repository-path"] = "C:/must-not-be-read"
				}
			},
			context.CancellationTokenSource.Token);
		string responseText = string.Join(
			Environment.NewLine,
			callResult.Content?.Select(content => content?.ToString() ?? string.Empty) ?? []);

		// Assert
		callResult.IsError.Should().BeTrue(
			because: "unadvertised repository authority must fail at the MCP binding boundary");
		responseText.Should().Contain("repository-path",
			because: "the binding failure should name the rejected argument so the caller can correct it");
	}

	[Test]
	[Description("Returns byte-identical EntitySchema merge content over the real stdio and Streamable HTTP MCP transports.")]
	[AllureTag(CreatioArtifactMergeTool.ToolName)]
	[AllureDescription("Invokes the same merge over stdio and HTTP and verifies byte-equivalent structured content.")]
	[AllureName("Creatio artifact merge is transport invariant")]
	public async Task MergeCreatioArtifact_Should_Return_Same_Content_Over_Stdio_And_Http() {
		// Arrange
		await using var context = Arrange(TimeSpan.FromMinutes(3));
		string repositoryRoot = Path.GetFullPath(Path.Combine(
			AppContext.BaseDirectory,
			"..",
			"..",
			"..",
			".."));
		string fixtureRoot = Path.Combine(
			repositoryRoot,
			"lab",
			"creatio-three-way-merge",
			"fixtures",
			"entity-schema");
		Dictionary<string, object?> request = new() {
			["args"] = new Dictionary<string, object?> {
				["artifact-path"] = "packages/UsrMergeProof/Schemas/UsrMergeProofEntity/metadata.json",
				["base-content"] = await File.ReadAllTextAsync(Path.Combine(fixtureRoot, "base-metadata.json")),
				["ours-content"] = await File.ReadAllTextAsync(Path.Combine(fixtureRoot, "ours-metadata.json")),
				["theirs-content"] = await File.ReadAllTextAsync(Path.Combine(fixtureRoot, "theirs-metadata.json")),
				["descriptor-content"] = await File.ReadAllTextAsync(Path.Combine(fixtureRoot, "descriptor.json"))
			}
		};
		McpE2ESettings settings = ResolveSettings();
		await using McpHttpServerSession httpServer = await McpHttpServerSession.StartAsync(
			settings,
			platformApiKey: null,
			context.CancellationTokenSource.Token);
		await using McpClient httpClient = await httpServer.ConnectAsync(
			platformApiKey: null,
			integrationCredentialsBase64: null,
			context.CancellationTokenSource.Token);
		ClioCliCommandResult cliCall = await ClioCliCommandRunner.RunAsync(
			settings,
			[
				CreatioArtifactMergeTool.ToolName,
				"--artifact-path", "packages/UsrMergeProof/Schemas/UsrMergeProofEntity/metadata.json",
				"--base-file", Path.Combine(fixtureRoot, "base-metadata.json"),
				"--ours-file", Path.Combine(fixtureRoot, "ours-metadata.json"),
				"--theirs-file", Path.Combine(fixtureRoot, "theirs-metadata.json"),
				"--descriptor-file", Path.Combine(fixtureRoot, "descriptor.json")
			],
			cancellationToken: context.CancellationTokenSource.Token);
		CreatioArtifactMergeResult? cliResult = JsonSerializer.Deserialize<CreatioArtifactMergeResult>(
			cliCall.StandardOutput);

		// Act
		CallToolResult stdioCall = await context.Session.CallToolAsync(
			CreatioArtifactMergeTool.ToolName,
			request,
			context.CancellationTokenSource.Token);
		CallToolResult httpCall = await httpClient.CallToolAsync(
			CreatioArtifactMergeTool.ToolName,
			request,
			cancellationToken: context.CancellationTokenSource.Token);
		CreatioArtifactMergeResult stdioResult = EntitySchemaStructuredResultParser
			.Extract<CreatioArtifactMergeResult>(stdioCall);
		CreatioArtifactMergeResult httpResult = EntitySchemaStructuredResultParser
			.Extract<CreatioArtifactMergeResult>(httpCall);

		// Assert
		cliCall.ExitCode.Should().Be(0,
			because: $"the CLI must resolve the same fixture before transport parity is compared. stderr: {cliCall.StandardError}");
		cliResult.Should().NotBeNull(because: "the CLI returns the shared structured result as JSON");
		stdioResult.Status.Should().Be("resolved",
			because: "the preserved EntitySchema stages have independent developer additions");
		httpResult.Status.Should().Be("resolved",
			because: "the HTTP transport must execute the same pure semantic operation");
		httpResult.Content.Should().Be(stdioResult.Content,
			because: "transport selection must not change a single byte of merge content");
		httpResult.Report.Should().BeEquivalentTo(stdioResult.Report,
			because: "transport selection must not change semantic verification or change reporting");
		cliResult!.Content.Should().Be(stdioResult.Content,
			because: "CLI-first and MCP adapters must expose the same merge service result byte-for-byte");
		cliResult.Report.Should().BeEquivalentTo(stdioResult.Report,
			because: "CLI-first and MCP adapters must expose the same semantic report");
	}

	[Test]
	[Description("Preserves legitimate sensitive-looking property names and values byte-for-byte across CLI, stdio, and HTTP.")]
	[AllureTag(CreatioArtifactMergeTool.ToolName)]
	[AllureDescription("Verifies business metadata whose names resemble secrets survives CLI, stdio, and HTTP response boundaries unchanged.")]
	[AllureName("Creatio artifact merge survives response redaction boundaries")]
	public async Task MergeCreatioArtifact_ShouldPreserveSensitiveLookingProperties_AcrossAllSurfaces() {
		// Arrange
		await using var context = Arrange(TimeSpan.FromMinutes(3));
		const string baseContent = """
		{"Database":"postgres://db/base","Server":"server-base","Token":"token-base","Auth":"auth-base","Uri":"https://example.test/base","Path":"packages/base"}
		""";
		const string oursContent = """
		{"Database":"postgres://db/ours","Server":"server-base","Token":"token-ours","Auth":"auth-base","Uri":"https://example.test/base","Path":"packages/base"}
		""";
		const string theirsContent = """
		{"Database":"postgres://db/base","Server":"server-theirs","Token":"token-base","Auth":"auth-theirs","Uri":"https://example.test/base","Path":"packages/base"}
		""";
		string fixtureRoot = CreateFixtureDirectory("properties-redaction-survival");
		string basePath = Path.Combine(fixtureRoot, "base.json");
		string oursPath = Path.Combine(fixtureRoot, "ours.json");
		string theirsPath = Path.Combine(fixtureRoot, "theirs.json");
		await File.WriteAllTextAsync(basePath, baseContent);
		await File.WriteAllTextAsync(oursPath, oursContent);
		await File.WriteAllTextAsync(theirsPath, theirsContent);
		Dictionary<string, object?> request = new() {
			["args"] = new Dictionary<string, object?> {
				["artifact-path"] = "packages/Test/Schemas/UsrProof/properties.json",
				["base-content"] = baseContent,
				["ours-content"] = oursContent,
				["theirs-content"] = theirsContent
			}
		};
		McpE2ESettings settings = ResolveSettings();
		await using McpHttpServerSession httpServer = await McpHttpServerSession.StartAsync(
			settings,
			platformApiKey: null,
			context.CancellationTokenSource.Token);
		await using McpClient httpClient = await httpServer.ConnectAsync(
			platformApiKey: null,
			integrationCredentialsBase64: null,
			context.CancellationTokenSource.Token);

		// Act
		ClioCliCommandResult cliCall = await ClioCliCommandRunner.RunAsync(
			settings,
			[
				CreatioArtifactMergeTool.ToolName,
				"--artifact-path", "packages/Test/Schemas/UsrProof/properties.json",
				"--base-file", basePath,
				"--ours-file", oursPath,
				"--theirs-file", theirsPath
			],
			cancellationToken: context.CancellationTokenSource.Token);
		CallToolResult stdioCall = await context.Session.CallToolAsync(
			CreatioArtifactMergeTool.ToolName,
			request,
			context.CancellationTokenSource.Token);
		CallToolResult httpCall = await httpClient.CallToolAsync(
			CreatioArtifactMergeTool.ToolName,
			request,
			cancellationToken: context.CancellationTokenSource.Token);
		CreatioArtifactMergeResult cliResult = JsonSerializer.Deserialize<CreatioArtifactMergeResult>(cliCall.StandardOutput)!;
		CreatioArtifactMergeResult stdioResult = EntitySchemaStructuredResultParser.Extract<CreatioArtifactMergeResult>(stdioCall);
		CreatioArtifactMergeResult httpResult = EntitySchemaStructuredResultParser.Extract<CreatioArtifactMergeResult>(httpCall);

		// Assert
		cliCall.ExitCode.Should().Be(0, because: $"independent properties changes should resolve. stderr: {cliCall.StandardError}");
		stdioResult.Status.Should().Be(CreatioArtifactMergeResult.ResolvedStatus,
			because: "the two branches changed different properties");
		httpResult.Content.Should().Be(stdioResult.Content,
			because: "HTTP serialization must not redact or rewrite legitimate artifact content");
		cliResult.Content.Should().Be(stdioResult.Content,
			because: "CLI and MCP must expose the same unmodified merge result");
		stdioResult.Content.Should().ContainAll([
			"postgres://db/ours",
			"server-theirs",
			"token-ours",
			"auth-theirs",
			"https://example.test/base",
			"packages/base"
		],
			because: "field names that resemble redaction targets are legitimate package metadata and must survive intact");
	}

	[Test]
	[Description("Preserves a real XML namespace byte-for-byte across native stdio and HTTP merge responses.")]
	[AllureTag(CreatioArtifactMergeTool.ToolName)]
	[AllureDescription("Merges namespaced resource XML through stdio and HTTP and verifies the namespace and independent additions survive.")]
	[AllureName("Creatio resource merge preserves XML namespace")]
	public async Task MergeCreatioArtifact_ShouldPreserveXmlNamespace_AcrossMcpTransports() {
		// Arrange
		await using var context = Arrange(TimeSpan.FromMinutes(3));
		const string baseContent = """
		<Resources xmlns="urn:creatio:test"><Items><Item Name="Shared" Value="Base" /></Items></Resources>
		""";
		const string oursContent = """
		<Resources xmlns="urn:creatio:test"><Items><Item Name="Shared" Value="Base" /><Item Name="LocalOnly" Value="L" /></Items></Resources>
		""";
		const string theirsContent = """
		<Resources xmlns="urn:creatio:test"><Items><Item Name="Shared" Value="Base" /><Item Name="RemoteOnly" Value="R" /></Items></Resources>
		""";
		Dictionary<string, object?> request = new() {
			["args"] = new Dictionary<string, object?> {
				["artifact-path"] = "packages/Test/Resources/UsrProof.Entity/resource.en-US.xml",
				["base-content"] = baseContent,
				["ours-content"] = oursContent,
				["theirs-content"] = theirsContent
			}
		};
		McpE2ESettings settings = ResolveSettings();
		await using McpHttpServerSession httpServer = await McpHttpServerSession.StartAsync(
			settings,
			platformApiKey: null,
			context.CancellationTokenSource.Token);
		await using McpClient httpClient = await httpServer.ConnectAsync(
			platformApiKey: null,
			integrationCredentialsBase64: null,
			context.CancellationTokenSource.Token);

		// Act
		CallToolResult stdioCall = await context.Session.CallToolAsync(
			CreatioArtifactMergeTool.ToolName,
			request,
			context.CancellationTokenSource.Token);
		CallToolResult httpCall = await httpClient.CallToolAsync(
			CreatioArtifactMergeTool.ToolName,
			request,
			cancellationToken: context.CancellationTokenSource.Token);
		CreatioArtifactMergeResult stdioResult = EntitySchemaStructuredResultParser.Extract<CreatioArtifactMergeResult>(stdioCall);
		CreatioArtifactMergeResult httpResult = EntitySchemaStructuredResultParser.Extract<CreatioArtifactMergeResult>(httpCall);

		// Assert
		stdioResult.Status.Should().Be(CreatioArtifactMergeResult.ResolvedStatus,
			because: "independent resource additions should merge semantically");
		httpResult.Content.Should().Be(stdioResult.Content,
			because: "transport selection must not rewrite namespace-qualified XML");
		stdioResult.Content.Should().Contain("xmlns=\"urn:creatio:test\"",
			because: "the namespace is part of the valid Creatio artifact and must survive the merge");
		stdioResult.Content.Should().ContainAll(["Name=\"LocalOnly\"", "Name=\"RemoteOnly\""],
			because: "both independent resource additions must survive alongside the namespace");
	}

	private McpE2ESettings ResolveSettings() {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = string.IsNullOrWhiteSpace(settings.ClioProcessPath)
			? TestConfiguration.ResolveFreshClioProcessPath()
			: Path.GetFullPath(settings.ClioProcessPath);
		settings.ProcessEnvironmentVariables["CLIO_HOME"] = _isolatedClioHome;
		return settings;
	}
}
