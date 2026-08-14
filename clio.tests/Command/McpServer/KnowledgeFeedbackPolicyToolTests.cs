using System.Linq;
using System.Reflection;
using Clio.Command;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class KnowledgeFeedbackPolicyToolTests {
	private ServiceProvider _container;
	private IKnowledgeFeedbackPolicyService _service;
	private KnowledgeFeedbackPolicyTools _sut;
	private KnowledgeFeedbackPolicy _policy;

	[SetUp]
	public void SetUp() {
		_policy = new KnowledgeFeedbackPolicy(
			"ask",
			"ask",
			"https://github.com/Advance-Technologies-Foundation/clio",
			"sanitized",
			"sha256:policy",
			null,
			"ask-each-time");
		_service = Substitute.For<IKnowledgeFeedbackPolicyService>();
		_service.GetPolicy().Returns(_policy);
		_service.Configure(
			Arg.Any<KnowledgeFeedbackPolicyUpdate>(),
			Arg.Any<bool>(),
			Arg.Any<KnowledgeFeedbackConsent>()).Returns(_policy);
		ServiceCollection services = new();
		services.AddSingleton(_service);
		services.AddTransient<KnowledgeFeedbackPolicyTools>();
		_container = services.BuildServiceProvider();
		_sut = _container.GetRequiredService<KnowledgeFeedbackPolicyTools>();
	}

	[TearDown]
	public void TearDown() {
		_container.Dispose();
	}

	[Test]
	[Description("Keeps feedback policy inspection and configuration out of the resident MCP schema budget.")]
	public void PolicyTools_ShouldRemainNonResident_WhenProfileEvaluated() {
		// Arrange
		string[] names = [
			KnowledgeFeedbackPolicyTools.GetToolName,
			KnowledgeFeedbackPolicyTools.ConfigureToolName
		];

		// Act
		bool[] residency = names.Select(McpCoreToolProfile.IsResident).ToArray();

		// Assert
		residency.Should().OnlyContain(value => !value,
			because: "infrequent policy administration belongs in the discoverable long tail");
	}

	[Test]
	[Description("Classifies inspection as read-only and configuration as a high-impact consent-gated mutation.")]
	public void PolicyTools_ShouldExposeDistinctSafetyClassifications_WhenReflected() {
		// Arrange
		MethodInfo get = typeof(KnowledgeFeedbackPolicyTools).GetMethod(nameof(KnowledgeFeedbackPolicyTools.Get))!;
		MethodInfo configure = typeof(KnowledgeFeedbackPolicyTools)
			.GetMethod(nameof(KnowledgeFeedbackPolicyTools.Configure))!;

		// Act
		McpServerToolAttribute getAttribute = get.GetCustomAttribute<McpServerToolAttribute>()!;
		McpServerToolAttribute configureAttribute = configure.GetCustomAttribute<McpServerToolAttribute>()!;

		// Assert
		getAttribute.ReadOnly.Should().BeTrue(
			because: "policy inspection does not mutate appsettings");
		configureAttribute.ReadOnly.Should().BeFalse(
			because: "policy configuration persists standing approval");
		configureAttribute.Destructive.Should().BeTrue(
			because: "changing standing approval can authorize future external disclosure and must reach the host consent gate");
	}

	[Test]
	[Description("Refuses automatic mode unless the agent confirms the user granted standing approval.")]
	public void Configure_ShouldRejectAuto_WhenConfirmationMissing() {
		// Arrange
		KnowledgeFeedbackConfigureArgs args = new(Mode: "auto", Confirmed: false);

		// Act
		KnowledgeFeedbackConfigureResponse result = _sut.Configure(args);

		// Assert
		result.Success.Should().BeFalse(
			because: "an agent cannot manufacture standing approval without an explicit confirmation signal");
		_service.DidNotReceive().Configure(
			Arg.Any<KnowledgeFeedbackPolicyUpdate>(), Arg.Any<bool>(), Arg.Any<KnowledgeFeedbackConsent>());
	}

	[Test]
	[Description("Refuses whitespace-padded automatic mode unless explicit confirmation is present.")]
	public void Configure_ShouldRejectNormalizedAuto_WhenConfirmationMissing() {
		// Arrange
		KnowledgeFeedbackConfigureArgs args = new(Mode: " auto ", Confirmed: false);

		// Act
		KnowledgeFeedbackConfigureResponse result = _sut.Configure(args);

		// Assert
		result.Success.Should().BeFalse(
			because: "normalization must not provide a path around the standing-approval confirmation");
		_service.DidNotReceive().Configure(
			Arg.Any<KnowledgeFeedbackPolicyUpdate>(), Arg.Any<bool>(), Arg.Any<KnowledgeFeedbackConsent>());
	}

	[Test]
	[Description("Refuses a confirmed automatic update when the reviewed policy snapshot is omitted.")]
	public void Configure_ShouldRejectAuto_WhenExpectedSnapshotMissing() {
		// Arrange
		KnowledgeFeedbackConfigureArgs args = new(Mode: "auto", Confirmed: true);

		// Act
		KnowledgeFeedbackConfigureResponse result = _sut.Configure(args);

		// Assert
		result.Success.Should().BeFalse(
			because: "a reusable boolean must not approve policy terms or a destination the user did not review");
		_service.DidNotReceive().Configure(
			Arg.Any<KnowledgeFeedbackPolicyUpdate>(), Arg.Any<bool>(), Arg.Any<KnowledgeFeedbackConsent>());
	}

	[Test]
	[Description("Records automatic full-report approval after explicit confirmation.")]
	public void Configure_ShouldApplyAutoFull_WhenConfirmed() {
		// Arrange
		KnowledgeFeedbackConfigureArgs args = new(
			Mode: "auto",
			Destination: "https://creatio.ghe.com/engineering/clio-feedback",
			ReportingScope: "full",
			Confirmed: true,
			ExpectedPolicyHash: "sha256:policy",
			ExpectedDestination: "https://creatio.ghe.com/engineering/clio-feedback",
			ExpectedReportingScope: "full");

		// Act
		KnowledgeFeedbackConfigureResponse result = _sut.Configure(args);

		// Assert
		result.Success.Should().BeTrue(
			because: "confirmed standing approval should be persisted by the shared service");
		_service.Received(1).Configure(Arg.Is<KnowledgeFeedbackPolicyUpdate>(update =>
			update.Mode == "auto"
			&& update.Destination == "https://creatio.ghe.com/engineering/clio-feedback"
			&& update.ReportingScope == "full"), true, Arg.Is<KnowledgeFeedbackConsent>(consent =>
				consent.PolicyHash == "sha256:policy"
				&& consent.Destination == "https://creatio.ghe.com/engineering/clio-feedback"
				&& consent.ReportingScope == "full"));
	}

	[Test]
	[Description("Requires confirmation before an agent retargets an already automatic policy.")]
	public void Configure_ShouldRejectDestinationChange_WhenAutoConfiguredAndConfirmationMissing() {
		// Arrange
		_service.GetPolicy().Returns(_policy with { ConfiguredMode = "auto", EffectiveMode = "auto" });
		KnowledgeFeedbackConfigureArgs args = new(
			Destination: "https://creatio.ghe.com/engineering/clio-feedback",
			Confirmed: false);

		// Act
		KnowledgeFeedbackConfigureResponse result = _sut.Configure(args);

		// Assert
		result.Success.Should().BeFalse(
			because: "a write-capable agent must not silently retarget standing automatic reporting");
		_service.DidNotReceive().Configure(
			Arg.Any<KnowledgeFeedbackPolicyUpdate>(), Arg.Any<bool>(), Arg.Any<KnowledgeFeedbackConsent>());
	}
}
