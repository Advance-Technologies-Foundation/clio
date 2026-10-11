using System;
using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Text.Json;
using Clio;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Knowledge;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Common.Telemetry;
using Clio.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// Covers what <see cref="BindingsModule.Register"/> does and does not put in the graph: the ENG-92563
/// Lever 1 gate (the MCP stdio host, and the <see cref="McpServerCommand"/> that depends on the
/// McpServer singleton, are registered only when the caller opts in via <c>registerMcpHost:true</c>),
/// the knowledge-bundle version fallback for source builds, and - since clio#1421 - the rule that a
/// registered factory must outlive the scope it was resolved from.
/// </summary>
[TestFixture]
[Property("Module", "Command")]
public class BindingsModuleMcpHostGateTests {
	[TestCase(false, typeof(NullServedContentMeter))]
	[TestCase(true, typeof(ServedContentMeter))]
	[Category("Unit")]
	[Description("Counts served content only in the stdio MCP host, where one process is one agent session, and hands every resolution in a container the same meter (ENG-100157).")]
	public void Register_ShouldCountServedContentOnlyInTheStdioHost_WhenHostModeChanges(
		bool registerMcpHost, Type expectedMeterType) {
		// Arrange
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		using ServiceProvider provider = (ServiceProvider)new BindingsModule(fileSystem)
			.Register(profile: BindingsModuleRegistrationProfile.Bootstrap, registerMcpHost: registerMcpHost);

		// Act
		IServedContentMeter first = provider.GetRequiredService<IServedContentMeter>();
		IServedContentMeter second = provider.GetRequiredService<IServedContentMeter>();

		// Assert
		first.Should().BeOfType(expectedMeterType,
			because: "only the stdio host counts; every other container, mcp-http's included, keeps the inert meter so one count never mixes sessions");
		second.Should().BeSameAs(first,
			because: "a transient meter would hand every resolution its own empty count");
	}

	[Test]
	[Category("Unit")]
	[Description("Wires get-tool-contract and the telemetry service of the stdio host to one count, so a contract the tool served is on the next event the host records (ENG-100157).")]
	public void Register_ShouldStampWhatTheHostToolServed_WhenTheStdioHostRecordsAnEvent() {
		// Arrange
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		using ServiceProvider provider = (ServiceProvider)new BindingsModule(fileSystem)
			.Register(profile: BindingsModuleRegistrationProfile.Bootstrap, registerMcpHost: true);
		ToolContractGetTool contractTool = provider.GetRequiredService<ToolContractGetTool>();
		ITelemetryService telemetry = provider.GetRequiredService<ITelemetryService>();
		contractTool.GetToolContracts();

		// Act
		TelemetryEventResult result = telemetry.Send(new TelemetryEventRequest(
			"018f6e4a-0000-7000-9000-000000000158", "workflow_started", TelemetryConsent: "granted"));

		// Assert
		result.Status.Should().Be("recorded",
			because: "a granted-consent stage event is stored in the host's telemetry spool");
		string eventsDirectory = TelemetryStoragePaths.EventsDirectory(TelemetryStoragePaths.ResolveRoot());
		string eventFile = fileSystem.Directory.GetFiles(eventsDirectory, "*.json").Should().ContainSingle(
			because: "exactly one event was recorded").Subject;
		using JsonDocument document = JsonDocument.Parse(fileSystem.File.ReadAllText(eventFile));
		Dictionary<string, JsonElement> attributes = document.RootElement.GetProperty("attributes")
			.EnumerateArray()
			.ToDictionary(attribute => attribute.GetProperty("key").GetString(),
				attribute => attribute.GetProperty("value").Clone());
		attributes.Should().ContainKey("contract_reads",
			because: "the contract the host's tool served must reach the host's telemetry service through one shared meter; a meter the DI graph failed to hand to either side stamps nothing");
		attributes["contract_reads"].GetProperty("int_value").GetInt64().Should().Be(1,
			because: "exactly one contract response was served before the event was recorded");
	}

	[TestCase(false)]
	[TestCase(true)]
	[Category("Unit")]
	[Description("Knowledge installation uses the full enabled runtime catalog in CLI and MCP containers.")]
	public void Register_ShouldAdvertiseEnabledKnowledgeTools_WhenHostModeChanges(bool registerMcpHost) {
		// Arrange
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		using ServiceProvider provider = (ServiceProvider)new BindingsModule(fileSystem)
			.Register(profile: BindingsModuleRegistrationProfile.Bootstrap, registerMcpHost: registerMcpHost);

		// Act
		KnowledgeBundleClientCapabilities capabilities = provider.GetRequiredService<KnowledgeBundleClientCapabilities>();

		// Assert
		capabilities.Tools.Should().BeEquivalentTo(provider.GetRequiredService<IMcpToolInvokerRegistry>().ToolNames,
			because: "knowledge requirements must use the same enabled catalog as runtime invocation");
		capabilities.Tools.Should().Contain(new[] {
			ManageUserTool.InspectToolName, ManageUserTool.ToolName,
			ManageRoleTool.InspectToolName, ManageRoleTool.ToolName,
			ManageAccessTool.InspectToolName, ManageAccessTool.ToolName,
			ManageLicenseTool.InspectToolName, ManageLicenseTool.ToolName
		}, because: "the administration requirements must be recognized, including long-tail tools");
		capabilities.Tools.Should().NotContain("missing-tool", because: "unknown tools must remain unsupported");
		capabilities.Tools.Should().NotContain("deploy-identity", because: "disabled tools are not runtime capabilities");
	}

	[TestCase("0.0.0")]
	[TestCase("0.0.0.0")]
	[Category("Unit")]
	[Description("Maps source-build assembly-version sentinels to the external knowledge compatibility fallback.")]
	public void ResolveKnowledgeBundleClioVersion_ShouldUseProductFallback_WhenAssemblyVersionIsDevelopmentSentinel(
		string assemblyVersion) {
		// Arrange
		Version version = new(assemblyVersion);

		// Act
		Version result = BindingsModule.ResolveKnowledgeBundleClioVersion(version);

		// Assert
		result.Should().Be(new Version(8, 1, 0),
			because: "source builds without CI or tag versioning must remain compatible with the external bundle product range");
	}

	[Test]
	[Category("Unit")]
	[Description("Preserves a real assembly version when building external knowledge compatibility capabilities.")]
	public void ResolveKnowledgeBundleClioVersion_ShouldPreserveVersion_WhenAssemblyVersionIsReal() {
		// Arrange
		Version version = new(8, 1, 0, 86);

		// Act
		Version result = BindingsModule.ResolveKnowledgeBundleClioVersion(version);

		// Assert
		result.Should().Be(version,
			because: "published builds must advertise their actual assembly product version and revision");
	}

	[Test]
	[Category("Unit")]
	[Description("Register with registerMcpHost:false builds successfully under ValidateOnBuild and leaves the MCP host out of the graph.")]
	public void Register_Should_NotRegisterMcpHost_When_RegisterMcpHostIsFalse() {
		// Arrange
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();

		// Act — Register itself runs BuildServiceProvider(ValidateOnBuild:true); a returned provider
		// proves the whole graph validated without the McpServer dependency.
		IServiceProvider provider = new BindingsModule(fileSystem)
			.Register(profile: BindingsModuleRegistrationProfile.Bootstrap, registerMcpHost: false);

		// Assert
		provider.GetService(typeof(ModelContextProtocol.Server.McpServer)).Should().BeNull(
			because: "the MCP server singleton must be registered only when the host is explicitly requested");
		provider.GetService(typeof(McpServerCommand)).Should().BeNull(
			because: "McpServerCommand depends on the gated McpServer singleton, so it must be absent from non-mcp builds");
	}

	[Test]
	[Category("Unit")]
	[Description("KnowledgeUnsequencedGitOptions resolves from the real container and is OFF when no feature flag is set.")]
	public void Register_Should_ResolveUnsequencedGitOptionsDisabled_When_FeatureFlagIsAbsent() {
		// Arrange
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();

		// Act
		IServiceProvider provider = new BindingsModule(fileSystem)
			.Register(profile: BindingsModuleRegistrationProfile.Bootstrap, registerMcpHost: false);
		KnowledgeUnsequencedGitOptions options = provider.GetRequiredService<KnowledgeUnsequencedGitOptions>();

		// Assert — the registration is a factory over IFeatureToggleService, so nothing else in the suite
		// proves it is reachable; a dropped or mis-scoped registration would only surface at runtime.
		options.Should().NotBeNull(
			because: "the local-iteration options must resolve from the real composition root, not only from test doubles");
		options.AllowUnsequencedGitBundles.Should().BeFalse(
			because: "knowledge-allow-unsequenced is opt-in, so an environment with no flag keeps every stock guard");
	}

	[Test]
	[Category("Unit")]
	[Description("Register with registerMcpHost:true registers the MCP host so McpServer and McpServerCommand resolve from the container.")]
	public void Register_Should_RegisterMcpHost_When_RegisterMcpHostIsTrue() {
		// Arrange
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();

		// Act
		IServiceProvider provider = new BindingsModule(fileSystem)
			.Register(profile: BindingsModuleRegistrationProfile.Bootstrap, registerMcpHost: true);

		// Assert
		provider.GetService(typeof(ModelContextProtocol.Server.McpServer)).Should().NotBeNull(
			because: "AddMcpServer registers the McpServer singleton when the host is requested");
		provider.GetRequiredService<McpServerCommand>().Should().NotBeNull(
			because: "the mcp-server command must resolve from the container that hosts the MCP server");
	}

	[Test]
	[Category("Unit")]
	[Description("The per-environment ISysSettingsManager factory still builds a manager after the scope it was resolved from has been disposed, because the long-running MCP tools invoke it from work that deliberately outlives their response.")]
	public void SysSettingsManagerFactory_ShouldStillBuildAManager_WhenTheResolvingScopeIsAlreadyDisposed() {
		// Arrange — resolve the factory the way a tool does, from a REQUEST SCOPE, then end that scope.
		// The MCP SDK gives every request its own scope (McpServerOptions.ScopeRequests defaults to true)
		// and disposes it as soon as the tool's response is returned, while create-app-section and the
		// other long-running tools keep working past their response deadline. A factory that closed over
		// the provider therefore threw ObjectDisposedException on the first call in that continuation, and
		// the caller — already holding a "still in progress, keep polling" envelope — was never told
		// (issue #1421).
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		IServiceProvider provider = new BindingsModule(fileSystem)
			.Register(profile: BindingsModuleRegistrationProfile.Bootstrap, registerMcpHost: false);
		Func<EnvironmentSettings, ISysSettingsManager> factory;
		using (IServiceScope scope = provider.CreateScope()) {
			factory = scope.ServiceProvider.GetRequiredService<Func<EnvironmentSettings, ISysSettingsManager>>();
		}

		// Act — the delegate is invoked only now, when the scope that produced it no longer exists.
		Func<ISysSettingsManager> invokeAfterDisposal = () => factory(new EnvironmentSettings {
			Uri = "http://localhost", Login = "Supervisor", Password = "Supervisor"
		});

		// Assert
		invokeAfterDisposal.Should().NotThrow(
			because: "work detached past the response deadline resolves this factory after its request scope is gone, so the factory must not depend on that scope");
		invokeAfterDisposal().Should().NotBeNull(
			because: "the detached continuation needs a usable manager, not merely the absence of an exception");
	}
}
