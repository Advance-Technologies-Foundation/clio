using System;
using Clio.Command;
using Clio.Command.McpServer.Knowledge;
using Clio.UserEnvironment;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class KnowledgeFeedbackPolicyServiceTests {
	private ServiceProvider _container;
	private ISettingsRepository _settingsRepository;
	private IKnowledgeGuidanceSource _guidanceSource;
	private IKnowledgeFeedbackPolicyService _sut;
	private KnowledgeFeedbackSettings _settings;
	private string _policyText;

	[SetUp]
	public void SetUp() {
		_settings = new KnowledgeFeedbackSettings();
		_policyText = "Reporting policy v1.";
		_settingsRepository = Substitute.For<ISettingsRepository>();
		_settingsRepository.GetKnowledgeFeedbackSettings().Returns(_ => _settings);
		_settingsRepository.UpdateKnowledgeFeedbackSettings(Arg.Any<Func<KnowledgeFeedbackSettings, KnowledgeFeedbackSettings>>())
			.Returns(call => {
				_settings = call.Arg<Func<KnowledgeFeedbackSettings, KnowledgeFeedbackSettings>>()(_settings);
				return _settings;
			});
		_guidanceSource = Substitute.For<IKnowledgeGuidanceSource>();
		_guidanceSource.FindByName(KnowledgeFeedbackPolicyService.ReportingGuidanceName)
			.Returns(_ => ActivePolicy(_policyText, sequence: 1));
		ServiceCollection services = new();
		services.AddSingleton(_settingsRepository);
		services.AddSingleton(_guidanceSource);
		services.AddTransient<IKnowledgeFeedbackPolicyService, KnowledgeFeedbackPolicyService>();
		_container = services.BuildServiceProvider();
		_sut = _container.GetRequiredService<IKnowledgeFeedbackPolicyService>();
	}

	[TearDown]
	public void TearDown() {
		_container.Dispose();
	}

	[Test]
	[Description("Approves automatic comprehensive reporting under the exact reporting article hash.")]
	public void Configure_ShouldPersistExactStandingApproval_WhenAutoFullSelected() {
		// Arrange
		KnowledgeFeedbackPolicyUpdate update = new(
			"auto",
			"https://creatio.ghe.com/engineering/clio-feedback/",
			"full");

		// Act
		KnowledgeFeedbackPolicy result = _sut.Configure(update);

		// Assert
		result.EffectiveMode.Should().Be("auto",
			because: "explicit auto selection grants standing approval for the current contract");
		result.ReportingScope.Should().Be("full",
			because: "private internal reporting must retain comprehensive evidence");
		_settings.StandingApproval.Should().NotBeNull(
			because: "automatic reporting needs a durable approval record");
		_settings.Destination.Should().Be("https://creatio.ghe.com/engineering/clio-feedback",
			because: "the configured repository must be normalized independently from approval versioning");
		_settings.StandingApproval.PolicyHash.Should().Be(
			KnowledgeFeedbackPolicyService.ComputePolicyHash(_policyText),
			because: "approval must bind only to the dedicated reporting article bytes");
	}

	[Test]
	[Description("Keeps automatic approval valid when unrelated bundle metadata changes but the reporting article bytes do not.")]
	public void GetPolicy_ShouldRemainAuto_WhenOnlyUnrelatedBundleGenerationChanges() {
		// Arrange
		_sut.Configure(new KnowledgeFeedbackPolicyUpdate("auto"));
		_guidanceSource.FindByName(KnowledgeFeedbackPolicyService.ReportingGuidanceName)
			.Returns(_ => ActivePolicy(_policyText, sequence: 999));

		// Act
		KnowledgeFeedbackPolicy result = _sut.GetPolicy();

		// Assert
		result.EffectiveMode.Should().Be("auto",
			because: "library sequence and unrelated guidance changes are not part of the approval key");
		result.ApprovalState.Should().Be("approved",
			because: "unchanged reporting article bytes preserve standing approval");
	}

	[Test]
	[Description("Falls back to ask without rewriting configured auto when the dedicated reporting article changes.")]
	public void GetPolicy_ShouldRequireApproval_WhenReportingArticleHashChanges() {
		// Arrange
		_sut.Configure(new KnowledgeFeedbackPolicyUpdate("auto"));
		_policyText = "Reporting policy v2.";

		// Act
		KnowledgeFeedbackPolicy result = _sut.GetPolicy();

		// Assert
		result.ConfiguredMode.Should().Be("auto",
			because: "a policy update must not erase the user's saved preference");
		result.EffectiveMode.Should().Be("ask",
			because: "new reporting instructions require renewed user approval");
		result.ApprovalState.Should().Be("reporting-policy-changed",
			because: "the agent must know exactly why it may ask again");
	}

	[Test]
	[Description("Keeps existing auto approval effective when the reporting article is temporarily unavailable.")]
	public void GetPolicy_ShouldRemainAuto_WhenApprovedReportingArticleIsTemporarilyUnavailable() {
		// Arrange
		_sut.Configure(new KnowledgeFeedbackPolicyUpdate("auto"));
		_guidanceSource.FindByName(KnowledgeFeedbackPolicyService.ReportingGuidanceName)
			.Returns(new KnowledgeArticleLookup(KnowledgeArticleLookupStatus.Unavailable, null, null));

		// Act
		KnowledgeFeedbackPolicy result = _sut.GetPolicy();

		// Assert
		result.EffectiveMode.Should().Be("auto",
			because: "only an observed different reporting-policy hash may downgrade standing approval");
		result.ApprovalState.Should().Be("approved-policy-unavailable",
			because: "the agent should distinguish unavailable verification from a changed policy");
	}

	[Test]
	[Description("Refuses automatic reporting when the stored approval hash is missing or malformed.")]
	public void GetPolicy_ShouldRequireApproval_WhenStoredHashIsMalformed() {
		// Arrange
		_settings.Mode = "auto";
		_settings.StandingApproval = new KnowledgeFeedbackStandingApproval { PolicyHash = "" };
		_guidanceSource.FindByName(KnowledgeFeedbackPolicyService.ReportingGuidanceName)
			.Returns(new KnowledgeArticleLookup(KnowledgeArticleLookupStatus.Unavailable, null, null));

		// Act
		KnowledgeFeedbackPolicy result = _sut.GetPolicy();

		// Assert
		result.EffectiveMode.Should().Be("ask",
			because: "temporary unavailability preserves only a valid standing approval");
		result.ApprovalState.Should().Be("approval-missing",
			because: "a malformed hash must never authorize external disclosure");
	}

	[Test]
	[Description("Honors explicit off even when dormant filing fields are malformed.")]
	public void GetPolicy_ShouldRemainOff_WhenDestinationIsMalformed() {
		// Arrange
		_settings.Mode = "off";
		_settings.Destination = "not-a-repository";

		// Act
		KnowledgeFeedbackPolicy result = _sut.GetPolicy();

		// Assert
		result.EffectiveMode.Should().Be("off",
			because: "disabled feedback must neither file nor prompt based on unused destination data");
		result.ApprovalState.Should().Be("disabled",
			because: "the agent should receive an unambiguous disabled state");
	}

	[Test]
	[Description("Keeps auto approval valid when an explicitly configured destination changes but the reporting article does not.")]
	public void Configure_ShouldRemainAuto_WhenDestinationChangesAndReportingPolicyDoesNot() {
		// Arrange
		_sut.Configure(new KnowledgeFeedbackPolicyUpdate("auto"));

		// Act
		KnowledgeFeedbackPolicy result = _sut.Configure(new KnowledgeFeedbackPolicyUpdate(
			Destination: "https://github.com/Advance-Technologies-Foundation/clio-knowledge"));

		// Assert
		result.EffectiveMode.Should().Be("auto",
			because: "only a reporting-article hash change may downgrade standing approval");
		result.ApprovalState.Should().Be("approved",
			because: "destination configuration is not part of approval versioning");
	}

	[Test]
	[Description("Rechecks confirmation against the latest locked settings before retargeting automatic reporting.")]
	public void Configure_ShouldRejectRetarget_WhenLatestPolicyBecameAutoAndConfirmationIsRequired() {
		// Arrange
		_settings.Mode = "auto";
		_settings.StandingApproval = new KnowledgeFeedbackStandingApproval {
			PolicyHash = KnowledgeFeedbackPolicyService.ComputePolicyHash(_policyText)
		};

		// Act
		Action act = () => _sut.Configure(new KnowledgeFeedbackPolicyUpdate(
			Destination: "https://creatio.ghe.com/engineering/clio-feedback"), requireConsent: true);

		// Assert
		act.Should().Throw<InvalidOperationException>(
			because: "the consent decision must be made against the latest settings inside the atomic mutation");
	}

	[Test]
	[Description("Rejects consent when the current policy hash differs from the snapshot shown to the user.")]
	public void Configure_ShouldRejectAuto_WhenConsentSnapshotIsStale() {
		// Arrange
		string displayedHash = KnowledgeFeedbackPolicyService.ComputePolicyHash(_policyText);
		_policyText = "Reporting policy v2.";
		KnowledgeFeedbackConsent consent = new(
			displayedHash,
			"https://github.com/Advance-Technologies-Foundation/clio",
			"sanitized");

		// Act
		Action act = () => _sut.Configure(
			new KnowledgeFeedbackPolicyUpdate(Mode: "auto"),
			requireConsent: true,
			consent: consent);

		// Assert
		act.Should().Throw<InvalidOperationException>(
			because: "confirmation must authorize only the exact reporting policy the user reviewed");
	}

	private static KnowledgeArticleLookup ActivePolicy(string text, ulong sequence) => new(
		KnowledgeArticleLookupStatus.Active,
		new KnowledgeArticle(
			KnowledgeFeedbackPolicyService.ReportingGuidanceName,
			"docs://knowledge/com.creatio.clio/knowledge-feedback",
			text),
		sequence);
}
