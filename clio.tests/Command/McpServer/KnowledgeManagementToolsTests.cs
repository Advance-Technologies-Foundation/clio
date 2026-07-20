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
	private IKnowledgeReferenceExampleService _referenceExamples = null!;
	private IKnowledgeSourceManagementService _service = null!;
	private ServiceProvider _container = null!;
	private KnowledgeManagementTools _tools = null!;

	[SetUp]
	public void SetUp() {
		_service = Substitute.For<IKnowledgeSourceManagementService>();
		_referenceExamples = Substitute.For<IKnowledgeReferenceExampleService>();
		ServiceCollection services = new();
		services.AddSingleton(_service);
		services.AddSingleton(_referenceExamples);
		services.AddTransient<KnowledgeManagementTools>();
		_container = services.BuildServiceProvider();
		_tools = _container.GetRequiredService<KnowledgeManagementTools>();
	}

	[TearDown]
	public void TearDown() {
		_referenceExamples.ClearReceivedCalls();
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

	[Test]
	[Description("Maps Git reference, enablement, priority, participation, and omitted NuGet trust fields into source creation.")]
	public void Add_ShouldMapGitArgumentsAndDefaults_WhenGitSourceIsProvided() {
		// Arrange
		_service.Add(Arg.Any<KnowledgeSourceAddRequest>())
			.Returns(new KnowledgeSourceCommandResult(true, "added", "creatio"));
		KnowledgeSourceAddArgs args = new(
			"creatio",
			"com.creatio.clio",
			"git",
			"https://github.com/example/knowledge.git",
			Branch: "main",
			Enabled: false,
			Priority: 75,
			Participation: "authoritative",
			Confirmed: true);

		// Act
		_tools.Add(args);

		// Assert
		_service.Received(1).Add(Arg.Is<KnowledgeSourceAddRequest>(request =>
			request.Alias == "creatio"
			&& request.LibraryId == "com.creatio.clio"
			&& request.TransportType == "git"
			&& request.Location == "https://github.com/example/knowledge.git"
			&& request.TrustedKeyId == null
			&& request.TrustedPublicKeyPath == null
			&& request.PackageId == null
			&& request.Branch == "main"
			&& request.Tag == null
			&& request.Commit == null
			&& !request.Enabled
			&& request.Priority == 75
			&& request.Participation == "authoritative"));
	}

	[TestCase(nameof(KnowledgeManagementTools.Install), "partner")]
	[TestCase(nameof(KnowledgeManagementTools.Update), "partner")]
	[TestCase(nameof(KnowledgeManagementTools.Install), null)]
	[TestCase(nameof(KnowledgeManagementTools.Update), null)]
	[Description("Maps an explicit source or the omitted all-enabled default for install and update operations.")]
	public void Lifecycle_ShouldMapOptionalSourceSelector(string methodName, string? source) {
		// Arrange
		KnowledgeSourceBatchResult expected = new(true, "complete", []);
		_service.Install(Arg.Any<string?>()).Returns(expected);
		_service.Update(Arg.Any<string?>()).Returns(expected);
		KnowledgeSourceSelectorArgs args = new(source);

		// Act
		KnowledgeSourceBatchResult result = methodName == nameof(KnowledgeManagementTools.Install)
			? _tools.Install(args)
			: _tools.Update(args);

		// Assert
		result.Should().BeSameAs(expected,
			because: "the MCP adapter should preserve the lifecycle result");
		if (methodName == nameof(KnowledgeManagementTools.Install)) {
			_service.Received(1).Install(source);
			_service.DidNotReceiveWithAnyArgs().Update(default);
		} else {
			_service.Received(1).Update(source);
			_service.DidNotReceiveWithAnyArgs().Install(default);
		}
	}

	[Test]
	[Description("Maps delete source selection and explicit confirmation without changing either value.")]
	public void Delete_ShouldMapSourceAndConfirmation_WhenArgumentsAreProvided() {
		// Arrange
		KnowledgeSourceBatchResult expected = new(true, "deleted", []);
		_service.Delete("partner", true).Returns(expected);

		// Act
		KnowledgeSourceBatchResult result = _tools.Delete(new KnowledgeConfirmedSourceArgs("partner", true));

		// Assert
		result.Should().BeSameAs(expected,
			because: "the MCP adapter should preserve the delete result");
		_service.Received(1).Delete("partner", true);
	}

	[Test]
	[Description("Maps omitted info arguments to all configured sources and a local-only status check.")]
	public void Info_ShouldPreserveLocalOnlyDefaults_WhenArgumentsAreOmitted() {
		// Arrange
		KnowledgeSourceInfoResult expected = new(
			true, "settings.json", "knowledge", Array.Empty<KnowledgeSourceInfo>(), null);
		_service.GetInfo(null, false).Returns(expected);

		// Act
		KnowledgeSourceInfoResult result = _tools.Info(new KnowledgeInfoArgs());

		// Assert
		result.Should().BeSameAs(expected,
			because: "omitted info arguments should preserve the local-only default result");
		_service.Received(1).GetInfo(null, false);
	}

	[TestCase(nameof(KnowledgeManagementTools.Enable), true)]
	[TestCase(nameof(KnowledgeManagementTools.Disable), true)]
	[TestCase(nameof(KnowledgeManagementTools.Remove), true)]
	[Description("Maps each required source alias into enable, disable, and confirmed removal operations.")]
	public void SourceMutation_ShouldMapRequiredAlias(string methodName, bool confirmed) {
		// Arrange
		KnowledgeSourceCommandResult expected = new(true, "complete", "partner");
		_service.Enable("partner").Returns(expected);
		_service.Disable("partner").Returns(expected);
		_service.Remove("partner", confirmed).Returns(expected);

		// Act
		KnowledgeSourceCommandResult result = methodName switch {
			nameof(KnowledgeManagementTools.Enable) => _tools.Enable(new KnowledgeAliasArgs("partner")),
			nameof(KnowledgeManagementTools.Disable) => _tools.Disable(new KnowledgeAliasArgs("partner")),
			_ => _tools.Remove(new KnowledgeConfirmedAliasArgs("partner", confirmed))
		};

		// Assert
		result.Should().BeSameAs(expected,
			because: "the MCP adapter should preserve the source mutation result");
		if (methodName == nameof(KnowledgeManagementTools.Enable)) {
			_service.Received(1).Enable("partner");
		} else if (methodName == nameof(KnowledgeManagementTools.Disable)) {
			_service.Received(1).Disable("partner");
		} else {
			_service.Received(1).Remove("partner", confirmed);
		}
	}

	[Test]
	[Description("Returns every configured source through the zero-argument list operation.")]
	public void List_ShouldReturnStructuredSourceCatalog() {
		// Arrange
		KnowledgeSourceListResult expected = new(true, []);
		_service.List().Returns(expected);

		// Act
		KnowledgeSourceListResult result = _tools.List();

		// Assert
		result.Should().BeSameAs(expected,
			because: "the MCP adapter should not rewrite the configured source catalog");
		_service.Received(1).List();
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

	[Test]
	[Description("Maps every reference-example discovery argument and returns structured catalog metadata unchanged.")]
	public void ListExamples_ShouldMapAllFilters_WhenArgumentsAreProvided() {
		// Arrange
		KnowledgeReferenceExampleListResult expected = new(true, [], []);
		_referenceExamples.List(Arg.Any<KnowledgeReferenceExampleQuery>()).Returns(expected);
		KnowledgeReferenceExampleListArgs args = new("partner", "pub/sub", "atf", "published");

		// Act
		KnowledgeReferenceExampleListResult result = _tools.ListExamples(args);

		// Assert
		result.Should().BeSameAs(expected,
			because: "the MCP adapter should preserve the service's structured discovery result");
		_referenceExamples.Received(1).List(Arg.Is<KnowledgeReferenceExampleQuery>(query =>
			query.SourceAlias == "partner"
			&& query.SearchText == "pub/sub"
			&& query.Capability == "atf"
			&& query.Status == "published"));
	}

	[Test]
	[Description("Preserves omitted optional reference-example filters so the MCP default discovers every catalog item.")]
	public void ListExamples_ShouldPreserveNullFilters_WhenArgumentsAreOmitted() {
		// Arrange
		_referenceExamples.List(Arg.Any<KnowledgeReferenceExampleQuery>())
			.Returns(new KnowledgeReferenceExampleListResult(true, [], []));

		// Act
		_tools.ListExamples(new KnowledgeReferenceExampleListArgs());

		// Assert
		_referenceExamples.Received(1).List(Arg.Is<KnowledgeReferenceExampleQuery>(query =>
			query.SourceAlias == null
			&& query.SearchText == null
			&& query.Capability == null
			&& query.Status == null));
	}

	[Test]
	[Description("Publishes reference-example discovery as a local read-only MCP operation under the canonical tool name.")]
	public void ListExamples_ShouldDeclareReadOnlyMetadata_WhenToolContractIsInspected() {
		// Arrange
		MethodInfo method = typeof(KnowledgeManagementTools).GetMethod(nameof(KnowledgeManagementTools.ListExamples))!;

		// Act
		McpServerToolAttribute attribute = method.GetCustomAttribute<McpServerToolAttribute>()!;

		// Assert
		attribute.Name.Should().Be(KnowledgeManagementTools.ListKnowledgeExamplesToolName,
			because: "tests and callers must share the production tool-name constant");
		attribute.ReadOnly.Should().BeTrue(
			because: "catalog discovery reads locally activated metadata and never clones a leaf repository");
		attribute.Destructive.Should().BeFalse(
			because: "listing repository coordinates does not mutate local or remote state");
		attribute.Idempotent.Should().BeTrue(
			because: "repeated discovery over an unchanged active catalog has the same result");
		attribute.OpenWorld.Should().BeFalse(
			because: "the tool does not contact leaf repositories or configured transports");
	}

	[TestCase(nameof(KnowledgeManagementTools.Add))]
	[TestCase(nameof(KnowledgeManagementTools.Delete))]
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
