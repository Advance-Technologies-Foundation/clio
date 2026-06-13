using System;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Command.EntitySchemaDesigner;
using Clio.Common;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
internal sealed class EntitySchemaCaptionCultureResolverTests
{
	private ICurrentUserCultureResolverFactory _cultureResolverFactory = null!;
	private ISettingsRepository _settingsRepository = null!;
	private ICurrentUserCultureResolver _cultureResolver = null!;
	private ILogger _logger = null!;
	private IEntitySchemaCaptionCultureResolver _resolver = null!;

	[SetUp]
	public void Setup() {
		_cultureResolverFactory = Substitute.For<ICurrentUserCultureResolverFactory>();
		_settingsRepository = Substitute.For<ISettingsRepository>();
		_cultureResolver = Substitute.For<ICurrentUserCultureResolver>();
		_logger = Substitute.For<ILogger>();
		_settingsRepository.GetEnvironment(Arg.Any<EnvironmentOptions>()).Returns(new EnvironmentSettings());
		_cultureResolverFactory.Create(Arg.Any<EnvironmentSettings>()).Returns(_cultureResolver);
		_resolver = new EntitySchemaCaptionCultureResolver(_cultureResolverFactory, _settingsRepository, _logger);
	}

	[Test]
	[Description("Returns the normalized override culture when an explicit caption-culture override is supplied.")]
	public void ResolveEffectiveCulture_ShouldReturnOverride_WhenOverrideIsValid() {
		// Act
		string result = _resolver.ResolveEffectiveCulture(new RemoteCommandOptions(), "uk-ua");

		// Assert
		result.Should().Be("uk-UA",
			because: "an explicit override wins and is normalized to the canonical culture name");
		_settingsRepository.DidNotReceive().GetEnvironment(Arg.Any<EnvironmentOptions>());
	}

	[Test]
	[Description("Throws a friendly error when the caption-culture override is not a valid culture name.")]
	public void ResolveEffectiveCulture_ShouldThrow_WhenOverrideIsInvalid() {
		// Arrange — a name longer than the platform culture-name limit reliably fails on ICU and NLS.
		string invalidCulture = new('z', 90);

		// Act
		Action act = () => _resolver.ResolveEffectiveCulture(new RemoteCommandOptions(), invalidCulture);

		// Assert
		act.Should().Throw<EntitySchemaDesignerException>()
			.WithMessage("*is not a valid culture name*",
				because: "an invalid explicit override must fail fast instead of silently degrading");
	}

	[Test]
	[Description("Returns the resolved profile culture when no override is supplied and resolution succeeds.")]
	public void ResolveEffectiveCulture_ShouldReturnProfileCulture_WhenResolvedAndNoOverride() {
		// Arrange
		_cultureResolver.ResolveAsync(Arg.Any<CancellationToken>())
			.Returns(Task.FromResult(CultureResolution.Resolved("uk-UA")));

		// Act
		string result = _resolver.ResolveEffectiveCulture(new RemoteCommandOptions(), null);

		// Assert
		result.Should().Be("uk-UA",
			because: "with no override the connected user's resolved profile culture is used");
	}

	[Test]
	[Description("Falls back to en-US when no override is supplied and profile resolution fails.")]
	public void ResolveEffectiveCulture_ShouldFallBackToEnUs_WhenResolutionFails() {
		// Arrange
		_cultureResolver.ResolveAsync(Arg.Any<CancellationToken>())
			.Returns(Task.FromResult(CultureResolution.Failed(CurrentUserCultureResolver.ReasonUserCultureMissing)));

		// Act
		string result = _resolver.ResolveEffectiveCulture(new RemoteCommandOptions(), null);

		// Assert
		result.Should().Be("en-US",
			because: "a failed profile resolution must degrade to the en-US fallback so scripted writes keep working");
	}

	[Test]
	[Description("Warns and falls back to en-US when profile resolution throws unexpectedly.")]
	public void ResolveEffectiveCulture_ShouldWarnAndFallBack_WhenResolutionThrows() {
		// Arrange
		_settingsRepository.GetEnvironment(Arg.Any<EnvironmentOptions>())
			.Returns(_ => throw new InvalidOperationException("environment lookup failed"));

		// Act
		string result = _resolver.ResolveEffectiveCulture(new RemoteCommandOptions(), null);

		// Assert
		result.Should().Be("en-US",
			because: "an unexpected resolution failure must not abort the write; it degrades to en-US");
		_logger.Received().WriteWarning(Arg.Is<string>(message => message.Contains("Could not resolve the user profile culture")));
	}
}
