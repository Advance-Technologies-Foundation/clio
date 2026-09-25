using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Common;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Project.NuGet;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end coverage for the strict descriptor-key check (ENG-95244, <c>spec/adr/adr-descriptor-strict-keys.md</c>)
/// over the real clio MCP server. NOT in CI - the process-designer fixtures need CrtProcessBuilder on the stand.
/// </summary>
/// <remarks>
/// The expected outcome depends on the STAND, by design: a key this clio's bundled contract does not declare is
/// REFUSED on an environment at or below the bundled CrtProcessBuilder, and only WARNED about on a newer one,
/// because a developer build may accept it. Each test reads both versions and asserts the outcome they dictate,
/// and says which branch it took, so a run against a newer stand is not mistaken for coverage of the refusal -
/// the refusal is pinned hermetically by <c>ProcessDescriptorKeyGuardTests</c> and the service tests.
/// </remarks>
[TestFixture]
[AllureNUnit]
[AllureFeature("process-descriptor-strict-keys")]
[NonParallelizable]
[Category(McpE2ECategories.ProcessDesigner)]
public sealed class ProcessDescriptorStrictKeysE2ETests {

	private const string CreateToolName = "create-business-process";
	private const string ModifyToolName = "modify-business-process";
	private const string AsNewVersionToolName = "modify-business-process-as-new-version";

	[Test]
	[Description("Over the real MCP path, a create descriptor carrying flows[0].lable - the key the 2026-09-24 probe measured being dropped in silence - is refused naming the path and 'label' on a stand at or below the bundled CrtProcessBuilder, and built with a warning naming both on a newer one. Either way the typo no longer passes unmentioned.")]
	[AllureTag(CreateToolName)]
	[AllureName("create-business-process refuses or warns about an unknown descriptor key")]
	public async Task CreateBusinessProcess_Should_NameTheUnknownKey_WhenADescriptorCarriesATypo() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync();
		bool newerThanBundle = await EnvironmentIsNewerThanBundleAsync(context);
		string processName = $"UsrClioBpStrictKeysE2e{Guid.NewGuid():N}";

		// Act
		CallToolResult result = await CallToolAsync(context, CreateToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = TwoElementDescriptor(processName, "lable")
		});

		// Assert
		string json = JsonSerializer.Serialize(result);
		json.Should().Contain("flows[0].lable").And.Contain("label",
			because: "whichever branch ran, the caller is told which key is wrong and what it was meant to be");
		if (newerThanBundle) {
			TestContext.Out.WriteLine("Stand CrtProcessBuilder is NEWER than the bundle: the WARNING branch ran.");
			result.IsError.Should().NotBeTrue(because: "a warning never stops the build");
			json.Should().Contain("will be sent").And.Contain("newer than the").And.Contain(processName)
				.And.NotContain("nothing was sent",
					because: "on a newer environment the key may be valid, so the build goes ahead with a warning");
		} else {
			TestContext.Out.WriteLine("Stand CrtProcessBuilder is not newer than the bundle: the REFUSAL branch ran.");
			json.Should().Contain("nothing was sent",
				because: "on an environment at the bundled contract the key is dropped for certain, so it is refused");
		}
	}

	[Test]
	[Description("The CONTROL for the test above: the same descriptor with the correctly spelled 'label' builds with no key warning at all, so the check does not refuse or warn about a key the server accepts.")]
	[AllureTag(CreateToolName)]
	[AllureName("create-business-process builds a descriptor whose keys are all declared")]
	public async Task CreateBusinessProcess_Should_BuildWithoutAKeyWarning_WhenEveryKeyIsDeclared() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync();
		string processName = $"UsrClioBpStrictKeysOkE2e{Guid.NewGuid():N}";

		// Act
		CallToolResult result = await CallToolAsync(context, CreateToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = TwoElementDescriptor(processName, "label")
		});

		// Assert
		string json = JsonSerializer.Serialize(result);
		result.IsError.Should().NotBeTrue(because: "a descriptor of declared keys is an ordinary build");
		json.Should().Contain(processName, because: "the build reports the created schema name");
		json.Should().NotContain("not a key this clio",
			because: "a declared key must never be reported as unknown - that would be a false refusal in waiting");
	}

	[Test]
	[Description("Over the real MCP path, a modify-business-process setFlow carrying 'lable' - measured to answer '1 operation(s) applied' and change nothing - is refused naming operations[0].lable, or warned about on a newer stand.")]
	[AllureTag(ModifyToolName)]
	[AllureName("modify-business-process refuses or warns about an unknown operation key")]
	public async Task ModifyBusinessProcess_Should_NameTheUnknownKey_WhenAnOperationCarriesATypo() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync();
		bool newerThanBundle = await EnvironmentIsNewerThanBundleAsync(context);
		string processName = $"UsrClioBpStrictKeysModE2e{Guid.NewGuid():N}";
		CallToolResult created = await CallToolAsync(context, CreateToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = TwoElementDescriptor(processName, "label")
		});
		JsonSerializer.Serialize(created).Should().Contain(processName, because: "the process to modify must exist");

		// Act
		CallToolResult result = await CallToolAsync(context, ModifyToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = processName,
			["operations"] = """[{"op":"setFlow","source":"Start1","target":"End1","kind":"sequence","lable":"Renamed"}]"""
		});

		// Assert
		string json = JsonSerializer.Serialize(result);
		json.Should().Contain("operations[0].lable").And.Contain("label",
			because: "whichever branch ran, the caller is told which operation key is wrong");
		if (newerThanBundle) {
			result.IsError.Should().NotBeTrue(because: "a warning never stops the modify");
			json.Should().Contain("will be sent").And.NotContain("nothing was sent",
				because: "on a newer environment the operation goes ahead with a warning, not a refusal");
		} else {
			json.Should().Contain("nothing was sent",
				because: "on an environment at the bundled contract the key is dropped for certain, so it is refused");
		}
	}

	[Test]
	[Description("Over the real MCP path, modify-business-process-as-new-version with an unknown operation key is refused before any version exists. Runs only against a stand at or below the bundled CrtProcessBuilder: on a newer one the warning branch would CREATE a version, and a version cannot be deleted.")]
	[AllureTag(AsNewVersionToolName)]
	[AllureName("modify-business-process-as-new-version refuses an unknown operation key before creating a version")]
	public async Task ModifyAsNewVersion_Should_RefuseBeforeCreatingAVersion_WhenAnOperationCarriesATypo() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync();
		if (await EnvironmentIsNewerThanBundleAsync(context)) {
			Assert.Ignore("The stand's CrtProcessBuilder is newer than the bundle, so this call would warn and create "
				+ "an undeletable version. The refusal is pinned by the service and guard unit tests.");
		}

		// Act
		CallToolResult result = await CallToolAsync(context, AsNewVersionToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = context.Settings.Sandbox.ProcessCode,
			["operations"] = """[{"op":"addParameter","parameter":{"name":"Amount","tpye":"Integer"}}]"""
		});

		// Assert
		JsonSerializer.Serialize(result).Should().Contain("operations[0].parameter.tpye").And.Contain("nothing was sent",
			because: "the refusal happens before the POST, so no version is created from a partly honoured request");
	}

	private static string TwoElementDescriptor(string processName, string labelKey) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio strict keys E2E",
		  "packageName": "Custom",
		  "elements": [
		    { "name": "Start1", "type": "startEvent" },
		    { "name": "End1", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "Start1", "target": "End1", "{{labelKey}}": "Go" }
		  ]
		}
		""";

	// The two versions the guard compares, read the way clio reads them: the installed one from the stand's
	// package list, the bundled one from the descriptor inside the archive next to the clio binary under test.
	private static async Task<bool> EnvironmentIsNewerThanBundleAsync(ArrangeContext context) {
		CallToolResult packages = await context.Session.CallToolAsync("list-packages",
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = context.EnvironmentName, ["filter"] = BundledPackages.ProcessBuilderPackageName
				}
			}, context.CancellationTokenSource.Token);
		string packagesText = string.Concat(packages.Content.OfType<TextContentBlock>().Select(block => block.Text));
		using JsonDocument packageList = JsonDocument.Parse(packagesText);
		string installed = packageList.RootElement.GetProperty("packages").EnumerateArray()
			.First(package => package.GetProperty("name").GetString() == BundledPackages.ProcessBuilderPackageName)
			.GetProperty("version").GetString();
		return PackageVersion.ParseVersion(installed) > PackageVersion.ParseVersion(ReadBundledVersion(context));
	}

	private static string ReadBundledVersion(ArrangeContext context) {
		string archive = Path.Combine(Path.GetDirectoryName(context.Settings.ClioProcessPath)!,
			BundledPackages.ProcessBuilderPackageName, BundledPackages.ProcessBuilderArchiveFileName);
		IFileSystem fileSystem = new FileSystem(new System.IO.Abstractions.FileSystem());
		new CompressionUtilities(fileSystem, new ZipFileWrapper())
			.TryReadFileFromGZip(archive, "descriptor.json", out byte[] content).Should()
			.BeTrue(because: "the clio under test ships the CrtProcessBuilder archive it enforces keys for");
		using JsonDocument descriptor = JsonDocument.Parse(Encoding.UTF8.GetString(content));
		return descriptor.RootElement.GetProperty("Descriptor").GetProperty("PackageVersion").GetString();
	}

	private static async Task<CallToolResult> CallToolAsync(ArrangeContext context, string toolName,
			Dictionary<string, object?> args) =>
		await context.Session.CallToolAsync(toolName, new Dictionary<string, object?> { ["args"] = args },
			context.CancellationTokenSource.Token);

	private static async Task<ArrangeContext> ArrangeAsync() {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string? environmentName = settings.Sandbox.EnvironmentName;
		if (string.IsNullOrWhiteSpace(environmentName)) {
			Assert.Ignore("Configure McpE2E:Sandbox:EnvironmentName (with CrtProcessBuilder installed) to run the strict-keys E2E.");
		}
		if (!await ClioCliCommandRunner.IsEnvironmentReachableAsync(settings, environmentName!)) {
			Assert.Ignore($"The strict-keys E2E requires a reachable sandbox environment. '{environmentName}' was not reachable.");
		}
		CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		return new ArrangeContext(session, cancellationTokenSource, environmentName!, settings);
	}

	private sealed record ArrangeContext(
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource,
		string EnvironmentName,
		McpE2ESettings Settings) : IAsyncDisposable {
		public async ValueTask DisposeAsync() {
			await Session.DisposeAsync();
			CancellationTokenSource.Dispose();
		}
	}
}
