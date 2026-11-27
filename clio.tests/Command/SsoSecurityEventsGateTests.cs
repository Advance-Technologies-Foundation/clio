using System;
using System.Collections.Generic;
using System.Reflection;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
///     The security-events commands are internal operator commands for a service that ships with Creatio 10.2: each of
///     the seven is hidden behind one clio feature flag and refused before any call on an older site.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class SsoSecurityEventsGateTests
{

	private static readonly Type[] SecurityEventsOptionsTypes = [
		typeof(GetSsoSecurityEventsOptions),
		typeof(EnableSsoSecurityEventsOptions),
		typeof(DisableSsoSecurityEventsOptions),
		typeof(SendSsoSecurityTestEventOptions),
		typeof(SetSsoSecurityEventsAddressOptions),
		typeof(UpdateSsoProviderOptions),
		typeof(RemoveSsoProviderOptions)
	];

	private static IEnumerable<object> NewOptionsOfEveryCommand() {
		foreach (Type type in SecurityEventsOptionsTypes) {
			SsoSecurityEventsOptions options = (SsoSecurityEventsOptions)Activator.CreateInstance(type);
			options.Code = "identity-v3";
			yield return new TestCaseData(options).SetArgDisplayNames(type.Name);
		}
	}

	private static ICreatioVersionChecker CheckerFor(string siteVersion) {
		ICreatioVersionProvider provider = Substitute.For<ICreatioVersionProvider>();
		provider.Resolve().Returns(CreatioVersionResolution.Resolved(Version.Parse(siteVersion)));
		return new CreatioVersionChecker(provider);
	}

	[TestCaseSource(nameof(NewOptionsOfEveryCommand))]
	[Description("Every security-events command is refused at dispatch while the sso-security-events flag is off, and the refusal names that flag.")]
	public void Dispatch_ShouldRefuseCommand_WhenSsoSecurityEventsFlagIsOff(SsoSecurityEventsOptions options) {
		// Arrange
		IFeatureToggleService featureToggles = new FrozenFeatureToggleService(new Dictionary<string, bool>());

		// Act
		bool blocked = Clio.Program.TryGetDisabledFeatureName(options, featureToggles, out string featureName);

		// Assert
		blocked.Should().BeTrue(because: "these operator commands are hidden until the flag is enabled");
		featureName.Should().Be("sso-security-events",
			because: "the refusal tells the user which flag clio experimental has to enable");
	}

	[TestCaseSource(nameof(NewOptionsOfEveryCommand))]
	[Description("Every security-events command dispatches once the sso-security-events flag is enabled.")]
	public void Dispatch_ShouldAllowCommand_WhenSsoSecurityEventsFlagIsOn(SsoSecurityEventsOptions options) {
		// Arrange
		IFeatureToggleService featureToggles = new FrozenFeatureToggleService(
			new Dictionary<string, bool> { ["sso-security-events"] = true });

		// Act
		bool blocked = Clio.Program.TryGetDisabledFeatureName(options, featureToggles, out string featureName);

		// Assert
		blocked.Should().BeFalse(because: "an enabled flag makes the command available");
		featureName.Should().BeNull(because: "nothing is refused");
	}

	[TestCaseSource(nameof(NewOptionsOfEveryCommand))]
	[Description("Every security-events command is refused before any call with the version-too-old code when the site runs Creatio below 10.2.")]
	public void Dispatch_ShouldRefuseCommand_WhenSiteRunsCreatioBelow102(SsoSecurityEventsOptions options) {
		// Arrange
		ICreatioVersionChecker checker = CheckerFor("10.1.999.0");

		// Act
		bool checkedAtDispatch = RequiresCreatioVersionAttribute.IsDefinedOn(options.GetType());
		bool blocked = Clio.Program.TryGetCreatioVersionRequirementError(options, checker, out string error);

		// Assert
		checkedAtDispatch.Should().BeTrue(
			because: "dispatch runs the version check only for options types that declare the requirement");
		blocked.Should().BeTrue(because: "the settings service ships with Creatio 10.2, so an older site cannot answer it");
		error.Should().Contain(CreatioVersionRequirementException.VersionTooOldCode,
			because: "automation branches on the stable version-too-old code");
		error.Should().Contain("10.2",
			because: "the user is told which Creatio version the command needs");
	}

	[TestCaseSource(nameof(NewOptionsOfEveryCommand))]
	[Description("Every security-events command passes the version gate on a Creatio 10.2 site.")]
	public void Dispatch_ShouldAllowCommand_WhenSiteRunsCreatio102(SsoSecurityEventsOptions options) {
		// Arrange
		ICreatioVersionChecker checker = CheckerFor("10.2.1138.0");

		// Act
		bool blocked = Clio.Program.TryGetCreatioVersionRequirementError(options, checker, out string error);

		// Assert
		blocked.Should().BeFalse(because: "a 10.2 site ships the settings service");
		error.Should().BeNull(because: "nothing is refused");
	}

	[Test]
	[Description("register-sso-provider stays outside the flag and the 10.2 floor: it is not a security-events command and works on older sites.")]
	public void RegisterSsoProvider_ShouldStayUngated() {
		// Arrange
		Type registerOptions = typeof(RegisterSsoProviderOptions);

		// Act
		bool gatedByFlag = registerOptions.GetCustomAttribute<FeatureToggleAttribute>(inherit: false) is not null;
		bool gatedByVersion = RequiresCreatioVersionAttribute.IsDefinedOn(registerOptions);

		// Assert
		gatedByFlag.Should().BeFalse(because: "registering a provider is a shipped command, unchanged by ENG-106080");
		gatedByVersion.Should().BeFalse(because: "registering a provider does not need the 10.2 settings service");
	}

}
