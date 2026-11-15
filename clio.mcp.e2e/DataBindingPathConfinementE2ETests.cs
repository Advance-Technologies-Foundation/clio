using System.Text.Json;
using Allure.Net.Commons;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// Path confinement of the data-binding MCP tools through the real stdio server, with no Creatio: the offline
/// SysSettings and SysModule templates reach every write path. clio runs these tools outside the caller's sandbox,
/// so a name, a culture or an image path must never carry a read or a write out of the workspace.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature("data-binding")]
[NonParallelizable]
public sealed class DataBindingPathConfinementE2ETests : McpContractFixtureBase {
	private const string CreateToolName = CreateDataBindingTool.CreateDataBindingToolName;
	private const string AddRowToolName = AddDataBindingRowTool.AddDataBindingRowToolName;
	private const string RemoveRowToolName = RemoveDataBindingRowTool.RemoveDataBindingRowToolName;
	private const string RowId = "5b0d0a5e-4a8f-4b47-9a11-2f0c9a1d7e01";
	private const string OutsideFolderName = "outside-target";

	// From packages/<package>: '..' twice reaches the folder that holds the workspace.
	private static readonly string EscapingPackageName = $"../../{OutsideFolderName}";

	// From packages/<package>/Data: '..' four times reaches the folder that holds the workspace.
	private static readonly string EscapingBindingName = $"../../../../{OutsideFolderName}";

	// From packages/<package>/Data/<binding>/Localization/data.x: '..' seven times reaches the same folder.
	private static readonly string EscapingCulture = "x" + string.Concat(Enumerable.Repeat("/..", 7)) + "/victim";

	[TestCase(CreateToolName, "package-name")]
	[TestCase(CreateToolName, "binding-name")]
	[TestCase(AddRowToolName, "package-name")]
	[TestCase(AddRowToolName, "binding-name")]
	[TestCase(RemoveRowToolName, "package-name")]
	[TestCase(RemoveRowToolName, "binding-name")]
	[AllureTag(CreateToolName, AddRowToolName, RemoveRowToolName)]
	[AllureName("Data-binding tools refuse a package or binding name that leaves the workspace")]
	[AllureDescription("Passes a '..' package or binding name that points at a folder beside the workspace holding binding files, and verifies the refusal and that no file there changed.")]
	[Description("Refuses a package or binding name with '..' segments before any read or write, so no binding file beside the workspace is created, rewritten or deleted.")]
	public async Task DataBinding_ShouldRefuseName_WhenItLeavesTheWorkspace(string toolName, string argumentName) {
		// Arrange
		await using ConfinementContext context = await ArrangeWorkspaceWithBindingAsync();
		PlantBindingCopies(context);
		Dictionary<string, string> before = SnapshotFolder(context.OutsideFolder);
		Dictionary<string, object?> args = BuildArgs(context, toolName);
		args[argumentName] = argumentName == "package-name" ? EscapingPackageName : EscapingBindingName;

		// Act
		CommandExecutionEnvelope execution = await ActAsync(context, toolName, args);

		// Assert
		AssertRefused(execution, "is not a plain folder name");
		AllureApi.Step("Verify nothing beside the workspace changed", () =>
			SnapshotFolder(context.OutsideFolder).Should().BeEquivalentTo(before,
				because: "the refusal comes before any binding file is read, written or deleted"));
	}

	[TestCase(CreateToolName)]
	[TestCase(AddRowToolName)]
	[AllureTag(CreateToolName, AddRowToolName)]
	[AllureName("Data-binding tools refuse a localization key that is not a culture name")]
	[AllureDescription("Passes a localization key with '..' segments that would name a JSON file beside the workspace, and verifies the refusal and that neither that file nor the binding changed.")]
	[Description("Refuses a localization key that is not a culture name before any write, so the key cannot name a file outside the binding's Localization folder.")]
	public async Task DataBinding_ShouldRefuseCulture_WhenItIsNotACultureName(string toolName) {
		// Arrange
		await using ConfinementContext context = await ArrangeWorkspaceWithBindingAsync();
		string victimPath = Path.Combine(context.RootDirectory, "victim.json");
		await File.WriteAllTextAsync(victimPath, """{"PackageData":[]}""");
		Dictionary<string, string> bindingBefore = SnapshotFolder(context.BindingDirectory);
		Dictionary<string, object?> args = BuildArgs(context, toolName);
		args["localizations"] = JsonSerializer.Serialize(new Dictionary<string, Dictionary<string, string>> {
			[EscapingCulture] = new() { ["Name"] = "Localized" }
		});

		// Act
		CommandExecutionEnvelope execution = await ActAsync(context, toolName, args);

		// Assert
		AssertRefused(execution, "is not a culture name");
		AllureApi.Step("Verify the file the key pointed at and the binding are unchanged", () => {
			File.ReadAllText(victimPath).Should().Be("""{"PackageData":[]}""",
				because: "the culture key must never become a path outside the Localization folder");
			SnapshotFolder(context.BindingDirectory).Should().BeEquivalentTo(bindingBefore,
				because: "the refusal comes before data.json or any culture file is written");
		});
	}

	[TestCase("outside-image.png", true)]
	[TestCase("missing-image.png", false)]
	[AllureTag(CreateToolName)]
	[AllureName("create-data-binding refuses an image path outside the workspace the same way whether or not it exists")]
	[AllureDescription("Passes an image-content value that is a path beside the workspace, once for an existing file and once for a missing one, and verifies the same refusal.")]
	[Description("Refuses an image-content path outside the workspace with the same message whether or not the file exists, so the tool does not reveal which files exist outside the workspace.")]
	public async Task CreateDataBinding_ShouldRefuseImagePath_WhenItIsOutsideTheWorkspace(string fileName, bool fileExists) {
		// Arrange
		await using ConfinementContext context = await ArrangeWorkspaceWithBindingAsync();
		string imagePath = Path.Combine(context.RootDirectory, fileName);
		if (fileExists) {
			await File.WriteAllBytesAsync(imagePath, [1, 2, 3]);
		}
		Dictionary<string, object?> args = new() {
			["package-name"] = context.PackageName,
			["schema-name"] = "SysModule",
			["workspace-path"] = context.WorkspacePath,
			["values"] = JsonSerializer.Serialize(new Dictionary<string, string> { ["Image16"] = imagePath })
		};

		// Act
		CommandExecutionEnvelope execution = await ActAsync(context, CreateToolName, args);

		// Assert
		AssertRefused(execution, $"Image file path must stay inside the workspace: {imagePath}");
		AllureApi.Step("Verify no SysModule binding was written", () =>
			Directory.Exists(Path.Combine(context.WorkspacePath, "packages", context.PackageName, "Data", "SysModule"))
				.Should().BeFalse(because: "the refusal comes before any binding file is written"));
	}

	private async Task<ConfinementContext> ArrangeWorkspaceWithBindingAsync() {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string rootDirectory = CreateFixtureDirectory("data-binding-confinement");
		CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(5));
		(string workspacePath, string packageName) = await AllureApi.Step("Arrange a clio workspace with one package",
			async () => await Workspace.CreateAsync(settings, rootDirectory, cancellationTokenSource.Token));
		ConfinementContext context = new(rootDirectory, workspacePath, packageName, Session, cancellationTokenSource);
		CommandExecutionEnvelope created = await ActAsync(context, CreateToolName, new Dictionary<string, object?> {
			["package-name"] = packageName,
			["schema-name"] = "SysSettings",
			["workspace-path"] = workspacePath,
			["values"] = JsonSerializer.Serialize(new { Id = RowId, Code = "UsrConfinement", Name = "Confinement" })
		});
		created.ExitCode.Should().Be(0, because: "a valid SysSettings binding is the starting point of every case");
		Directory.CreateDirectory(context.OutsideFolder);
		return context;
	}

	// Copies of the real binding files where each escaping name would land, so a command that followed the name
	// would find a binding to rewrite instead of failing on a missing folder.
	private static void PlantBindingCopies(ConfinementContext context) {
		string[] targets = [context.OutsideFolder, Path.Combine(context.OutsideFolder, "Data", "SysSettings")];
		foreach (string target in targets) {
			Directory.CreateDirectory(Path.Combine(target, "Localization"));
			foreach (string fileName in new[] { "descriptor.json", "data.json" }) {
				File.Copy(Path.Combine(context.BindingDirectory, fileName), Path.Combine(target, fileName));
			}
			File.WriteAllText(Path.Combine(target, "Localization", "keep.json"), "{}");
		}
	}

	private static Dictionary<string, object?> BuildArgs(ConfinementContext context, string toolName) {
		Dictionary<string, object?> args = new() {
			["package-name"] = context.PackageName,
			["workspace-path"] = context.WorkspacePath
		};
		if (toolName == CreateToolName) {
			args["schema-name"] = "SysSettings";
			args["values"] = JsonSerializer.Serialize(new { Code = "UsrChanged", Name = "Changed" });
		} else if (toolName == AddRowToolName) {
			args["binding-name"] = "SysSettings";
			args["values"] = JsonSerializer.Serialize(new { Id = RowId, Name = "Changed" });
		} else {
			args["binding-name"] = "SysSettings";
			args["key-value"] = RowId;
		}
		return args;
	}

	private static async Task<CommandExecutionEnvelope> ActAsync(
		ConfinementContext context, string toolName, Dictionary<string, object?> args) {
		CallToolResult callResult = await context.Session.CallToolAsync(toolName,
			new Dictionary<string, object?> { ["args"] = args }, context.CancellationTokenSource.Token);
		callResult.IsError.Should().NotBeTrue(
			because: "a refusal is a normal command result, not a protocol error");
		return McpCommandExecutionParser.Extract(callResult);
	}

	private static void AssertRefused(CommandExecutionEnvelope execution, string expectedMessagePart) =>
		AllureApi.Step("Verify the refusal", () => {
			execution.ExitCode.Should().Be(1, because: "an argument that leaves the workspace must be refused");
			execution.Output.Should().Contain(message => message.MessageType == LogDecoratorType.Error
					&& message.Value != null && message.Value.Contains(expectedMessagePart),
				because: $"the error must say why the argument was refused ('{expectedMessagePart}')");
		});

	private static Dictionary<string, string> SnapshotFolder(string folder) =>
		Directory.Exists(folder)
			? Directory.GetFileSystemEntries(folder, "*", SearchOption.AllDirectories)
				.ToDictionary(path => path, path => File.Exists(path) ? File.ReadAllText(path) : "<directory>")
			: [];

	private sealed record ConfinementContext(
		string RootDirectory,
		string WorkspacePath,
		string PackageName,
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource) : IAsyncDisposable {
		public string OutsideFolder => Path.Combine(RootDirectory, OutsideFolderName);

		public string BindingDirectory => Path.Combine(WorkspacePath, "packages", PackageName, "Data", "SysSettings");

		public ValueTask DisposeAsync() {
			CancellationTokenSource.Dispose();
			return ValueTask.CompletedTask;
		}
	}

	/// <summary>
	/// The workspace every data-binding MCP fixture starts from: an empty clio workspace with one package, made with
	/// the real clio CLI. Nested so the e2e selection does not take it for a fixture.
	/// </summary>
	internal static class Workspace {
		/// <summary>Creates the workspace under <paramref name="rootDirectory"/> and returns its path and package name.</summary>
		internal static async Task<(string WorkspacePath, string PackageName)> CreateAsync(
			McpE2ESettings settings, string rootDirectory, CancellationToken cancellationToken) {
			string workspaceName = $"workspace-{Guid.NewGuid():N}";
			string workspacePath = Path.Combine(rootDirectory, workspaceName);
			string packageName = $"Pkg{Guid.NewGuid():N}"[..18];
			await ClioCliCommandRunner.RunAndAssertSuccessAsync(settings,
				["create-workspace", workspaceName, "--empty", "--directory", rootDirectory],
				cancellationToken: cancellationToken);
			await ClioCliCommandRunner.RunAndAssertSuccessAsync(settings, ["add-package", packageName],
				workingDirectory: workspacePath, cancellationToken: cancellationToken);
			return (workspacePath, packageName);
		}
	}
}
