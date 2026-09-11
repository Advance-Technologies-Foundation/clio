using System.Text.Json;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Common;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Creatio;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;

namespace Clio.Mcp.E2E;

/// <summary>Proves synchronization item errors survive the external MCP process boundary.</summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature("package-synchronization")]
[NonParallelizable]
public sealed class PackageSynchronizationErrorsE2ETests {
	[TestCase(LoadPackagesTool.LoadPackagesToDbToolName, true)]
	[TestCase(LoadPackagesTool.LoadPackagesToDbToolName, false)]
	[TestCase(LoadPackagesTool.LoadPackagesToFileSystemToolName, true)]
	[TestCase(LoadPackagesTool.LoadPackagesToFileSystemToolName, false)]
	[Description("A successful synchronization envelope with item errors fails through the real MCP server; a clean envelope succeeds.")]
	[AllureTag(LoadPackagesTool.LoadPackagesToDbToolName, LoadPackagesTool.LoadPackagesToFileSystemToolName)]
	[AllureName("Synchronization item errors override overall success")]
	[AllureDescription("Uses an isolated configuration and local HTTP stub to verify the real clio MCP synchronization result and diagnostics.")]
	public async Task LoadPackages_ShouldReportItemErrors_WhenPlatformReportsPartialSuccess(string toolName, bool hasErrors) {
		// Arrange
		string tempHome = Path.Combine(Path.GetTempPath(), $"clio-sync-errors-{Guid.NewGuid():N}");
		Directory.CreateDirectory(tempHome);
		try {
			McpE2ESettings settings = TestConfiguration.Load();
			settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
			settings.ProcessEnvironmentVariables["CLIO_HOME"] = tempHome;
			await using RuntimeDetectionStubServer stub = RuntimeDetectionStubServer.Start(new(
				NetCoreHealthEnabled: true, NetFrameworkHealthEnabled: false,
				NetCoreServiceEnabled: true, NetFrameworkServiceEnabled: false,
				PackageSynchronizationResponse: hasErrors
					? """{"success":true,"errors":[{"workspaceItem":{"name":"LookupBinding"},"errorInfo":{"message":"ReferenceSchemaName is unsupported","errorCode":"InvalidDescriptor"}}]}"""
					: """{"success":true,"errors":[]}"""));
			using TemporaryClioSettingsOverride configuration = TemporaryClioSettingsOverride.ReplaceContent(
				JsonSerializer.Serialize(new {
					ActiveEnvironmentKey = "stub",
					Environments = new { stub = new { Uri = stub.BaseUrl, Login = "Supervisor", Password = "Supervisor", IsNetCore = true } }
				}), settings.ClioProcessPath, settings.ProcessEnvironmentVariables);
			using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(2));
			await using McpServerSession session = await McpServerSession.StartAsync(settings, timeout.Token);

			// Act
			var call = await session.CallToolAsync(toolName,
				new Dictionary<string, object?> { ["environmentName"] = "stub" }, timeout.Token);
			CommandExecutionEnvelope result = McpCommandExecutionParser.Extract(call);

			// Assert
			call.IsError.Should().NotBeTrue(because: "command outcomes are carried by the execution envelope");
			result.ExitCode.Should().Be(hasErrors ? 1 : 0,
				because: "item errors must override the overall success flag");
			result.Output.Should().NotBeNullOrEmpty(because: "the operation must explain its result");
			if (hasErrors) {
				result.Output.Should().Contain(message => message.MessageType == LogDecoratorType.Error
					&& message.Value!.Contains("LookupBinding") && message.Value.Contains("ReferenceSchemaName"),
					because: "the failure must name the rejected item and platform reason");
				result.Output.Should().NotContain(message => message.Value!.Contains("completed"),
					because: "partial synchronization must not claim completion");
			} else {
				result.Output.Should().Contain(message => message.MessageType == LogDecoratorType.Info,
					because: "successful synchronization must publish an informational completion message");
				result.Output.Should().NotContain(message => message.MessageType == LogDecoratorType.Error,
					because: "clean synchronization must remain successful");
			}
		} finally {
			Directory.Delete(tempHome, recursive: true);
		}
	}
}
