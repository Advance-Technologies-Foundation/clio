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
public sealed class CaptionCultureResolverTests
{
	private static CaptionCultureResolver CreateResolver(CultureResolution profileResolution, bool settingsThrows = false)
	{
		ICurrentUserCultureResolver inner = Substitute.For<ICurrentUserCultureResolver>();
		inner.ResolveAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(profileResolution));
		ICurrentUserCultureResolverFactory factory = Substitute.For<ICurrentUserCultureResolverFactory>();
		factory.Create(Arg.Any<EnvironmentSettings>()).Returns(inner);
		ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();
		if (settingsThrows)
		{
			settingsRepository.GetEnvironment(Arg.Any<EnvironmentOptions>())
				.Returns(_ => throw new InvalidOperationException("environment 'x' not found"));
		}
		else
		{
			settingsRepository.GetEnvironment(Arg.Any<EnvironmentOptions>()).Returns(new EnvironmentSettings());
		}

		return new CaptionCultureResolver(factory, settingsRepository, Substitute.For<ILogger>());
	}

	[Test]
	[Description("An explicit --caption-culture override wins and is returned as the canonical CultureInfo.Name.")]
	public void Resolve_ShouldReturnNormalizedOverride_WhenOverrideProvided()
	{
		// Arrange — profile would resolve to uk-UA, but the override must take precedence.
		CaptionCultureResolver resolver = CreateResolver(CultureResolution.Resolved("uk-UA"));

		// Act
		string culture = resolver.Resolve(new CreateEntitySchemaOptions(), "EN-us");

		// Assert
		culture.Should().Be("en-US",
			because: "the override wins over the profile culture and is normalized to the canonical culture name");
	}

	[Test]
	[Description("An invalid --caption-culture override is rejected with a user-friendly designer exception.")]
	public void Resolve_ShouldThrow_WhenOverrideIsInvalid()
	{
		// Arrange
		CaptionCultureResolver resolver = CreateResolver(CultureResolution.Resolved("en-US"));

		// Act
		Action act = () => resolver.Resolve(new CreateEntitySchemaOptions(), "not_a_culture!!");

		// Assert
		act.Should().Throw<EntitySchemaDesignerException>(
			because: "an invalid override is a user error that must surface a clear message");
	}

	[Test]
	[Description("With no override the resolved profile culture is returned.")]
	public void Resolve_ShouldReturnProfileCulture_WhenResolvedAndNoOverride()
	{
		// Arrange
		CaptionCultureResolver resolver = CreateResolver(CultureResolution.Resolved("uk-UA"));

		// Act
		string culture = resolver.Resolve(new CreateEntitySchemaOptions(), null);

		// Assert
		culture.Should().Be("uk-UA",
			because: "without an override the connected user's profile culture is used");
	}

	[Test]
	[Description("With no override and an unresolved profile culture the resolver degrades to en-US (non-fatal, M-4).")]
	public void Resolve_ShouldFallBackToEnUs_WhenProfileUnresolved()
	{
		// Arrange
		CaptionCultureResolver resolver = CreateResolver(
			CultureResolution.Failed(CurrentUserCultureResolver.ReasonUserCultureMissing));

		// Act
		string culture = resolver.Resolve(new CreateEntitySchemaOptions(), null);

		// Assert
		culture.Should().Be(EntitySchemaDesignerSupport.DefaultCultureName,
			because: "an unresolved profile culture must degrade to en-US so creation is not aborted");
	}

	[Test]
	[Description("An exception while resolving the environment degrades to en-US rather than aborting (non-fatal, M-4).")]
	public void Resolve_ShouldFallBackToEnUs_WhenEnvironmentResolutionThrows()
	{
		// Arrange
		CaptionCultureResolver resolver = CreateResolver(CultureResolution.Resolved("uk-UA"), settingsThrows: true);

		// Act
		string culture = resolver.Resolve(new CreateEntitySchemaOptions { Environment = "x" }, null);

		// Assert
		culture.Should().Be(EntitySchemaDesignerSupport.DefaultCultureName,
			because: "environment-resolution failure must be non-fatal and degrade to en-US");
	}
}
