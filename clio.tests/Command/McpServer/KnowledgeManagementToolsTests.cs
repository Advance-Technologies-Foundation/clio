using System;
using System.IO;
using System.Reflection;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Verifies the non-resident multi-source knowledge MCP contract.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class KnowledgeManagementToolsTests {
	private IKnowledgeSourceManagementService _service = null!;
	private ServiceProvider _container = null!;
	private KnowledgeManagementTools _tools = null!;

	[SetUp]
	public void SetUp() {
		_service = Substitute.For<IKnowledgeSourceManagementService>();
		ServiceCollection services = new();
		services.AddSingleton(_service);
		services.AddTransient<KnowledgeManagementTools>();
		_container = services.BuildServiceProvider();
		_tools = _container.GetRequiredService<KnowledgeManagementTools>();
	}

	[TearDown]
	public void TearDown() {
		_service.ClearReceivedCalls();
		_container.Dispose();
	}

	[Test]
	[Description("Maps each required per-source trust field from the MCP add-source contract into command orchestration.")]
	public void Add_ShouldMapPerSourceSigningTrust_WhenArgumentsAreValid() {
		// Arrange
		string publicKeyPath = Path.GetFullPath("partner-public.pem");
		KnowledgeSourceCommandResult expected = new(true, "added", "partner");
		_service.Add(Arg.Any<KnowledgeSourceAddRequest>()).Returns(expected);
		KnowledgeSourceAddArgs args = new(
			"partner",
			"com.example.partner",
			"nuget",
			"https://packages.example.test/v3/index.json",
			"partner-signing-2026",
			publicKeyPath,
			PackageId: "Example.Partner.Knowledge",
			Confirmed: true);

		// Act
		KnowledgeSourceCommandResult result = _tools.Add(args);

		// Assert
		result.Should().BeSameAs(expected,
			because: "the MCP adapter should return the command-layer result without rewriting it");
		_service.Received(1).Add(Arg.Is<KnowledgeSourceAddRequest>(request =>
			request.Alias == "partner"
			&& request.TrustedKeyId == "partner-signing-2026"
			&& request.TrustedPublicKeyPath == publicKeyPath
			&& request.PackageId == "Example.Partner.Knowledge"));
	}

	[Test]
	[Description("Refuses to persist a publisher trust root when explicit MCP confirmation is absent.")]
	public void Add_ShouldRequireConfirmation_BeforeCallingService() {
		// Arrange
		KnowledgeSourceAddArgs args = new(
			"partner", "com.example.partner", "nuget", "https://packages.example.test/v3/index.json",
			"partner-signing-2026", Path.GetFullPath("partner-public.pem"),
			PackageId: "Example.Partner.Knowledge", Confirmed: false);

		// Act
		KnowledgeSourceCommandResult result = _tools.Add(args);

		// Assert
		result.Success.Should().BeFalse(
			because: "adding a publisher key expands the local trust boundary and needs user approval");
		_service.DidNotReceiveWithAnyArgs().Add(default!);
	}

	[TestCase(false)]
	[TestCase(true)]
	[Description("Maps the explicit checkUpdates MCP flag directly so info remains local-only by default.")]
	public void Info_ShouldMapExplicitUpdateCheck_WithoutOfflineInversion(bool checkUpdates) {
		// Arrange
		KnowledgeSourceInfoResult expected = new(
			true, "settings.json", "knowledge", Array.Empty<KnowledgeSourceInfo>(), null);
		_service.GetInfo("partner", checkUpdates).Returns(expected);
		KnowledgeInfoArgs args = new("partner", checkUpdates);

		// Act
		KnowledgeSourceInfoResult result = _tools.Info(args);

		// Assert
		result.Should().BeSameAs(expected,
			because: "the MCP adapter should preserve the management service's structured status result");
		_service.Received(1).GetInfo("partner", checkUpdates);
	}

	[TestCase(nameof(KnowledgeManagementTools.Add))]
	[TestCase(nameof(KnowledgeManagementTools.Install))]
	[TestCase(nameof(KnowledgeManagementTools.Remove))]
	[TestCase(nameof(KnowledgeManagementTools.Update))]
	[Description("Classifies trust and active-knowledge mutations as destructive so the MCP host confirmation gate applies.")]
	public void TrustSourceMutation_ShouldDeclareDestructiveMetadata(string methodName) {
		// Arrange
		MethodInfo method = typeof(KnowledgeManagementTools).GetMethod(methodName)!;

		// Act
		McpServerToolAttribute attribute = method.GetCustomAttribute<McpServerToolAttribute>()!;

		// Assert
		attribute.Destructive.Should().BeTrue(
			because: "trust or active-guidance changes must never execute through the durable silent-write path");
	}
}
