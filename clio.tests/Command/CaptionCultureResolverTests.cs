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

	private static (CaptionCultureResolver Resolver, ICurrentUserCultureResolverFactory Factory,
		ISettingsRepository SettingsRepository) CreateResolverWithMocks(CultureResolution profileResolution)
	{
		ICurrentUserCultureResolver inner = Substitute.For<ICurrentUserCultureResolver>();
		inner.ResolveAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(profileResolution));
		ICurrentUserCultureResolverFactory factory = Substitute.For<ICurrentUserCultureResolverFactory>();
		factory.Create(Arg.Any<EnvironmentSettings>()).Returns(inner);
		ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();
		CaptionCultureResolver resolver =
			new(factory, settingsRepository, Substitute.For<ILogger>());
		return (resolver, factory, settingsRepository);
	}

	[Test]
	[Description("The settings-based overload resolves the profile culture without any ISettingsRepository lookup (AC-01).")]
	public void Resolve_ShouldReturnProfileCultureWithoutRepositoryLookup_WhenSettingsProvided()
	{
		// Arrange
		(CaptionCultureResolver resolver, _, ISettingsRepository settingsRepository) =
			CreateResolverWithMocks(CultureResolution.Resolved("uk-UA"));

		// Act
		string culture = resolver.Resolve(new EnvironmentSettings(), null);

		// Assert
		culture.Should().Be("uk-UA",
			because: "the settings-based overload applies the same profile-culture precedence as the name-based one");
		settingsRepository.ReceivedCalls().Should().BeEmpty(
			because: "the settings-based overload must never consult ISettingsRepository — the caller already supplied settings");
	}

	[Test]
	[Description("The settings-based overload lets an explicit --caption-culture override win without touching the factory or the repository.")]
	public void Resolve_ShouldReturnNormalizedOverride_WhenSettingsProvidedAndOverrideGiven()
	{
		// Arrange
		(CaptionCultureResolver resolver, ICurrentUserCultureResolverFactory factory,
			ISettingsRepository settingsRepository) = CreateResolverWithMocks(CultureResolution.Resolved("uk-UA"));

		// Act
		string culture = resolver.Resolve(new EnvironmentSettings(), "EN-us");

		// Assert
		culture.Should().Be("en-US",
			because: "the override wins and is normalized to the canonical culture name, same as the name-based overload");
		factory.DidNotReceiveWithAnyArgs().Create(default!);
		settingsRepository.ReceivedCalls().Should().BeEmpty(
			because: "the override short-circuit must not trigger any repository lookup on the settings-based path");
	}

	[Test]
	[Description("The settings-based overload degrades to en-US when profile resolution fails, mirroring the name-based non-fatal behavior (M-4).")]
	public void Resolve_ShouldFallBackToEnUs_WhenSettingsProvidedAndProfileUnresolved()
	{
		// Arrange
		(CaptionCultureResolver resolver, _, _) = CreateResolverWithMocks(
			CultureResolution.Failed(CurrentUserCultureResolver.ReasonUserCultureMissing));

		// Act
		string culture = resolver.Resolve(new EnvironmentSettings(), null);

		// Assert
		culture.Should().Be(EntitySchemaDesignerSupport.DefaultCultureName,
			because: "an unresolved profile culture must degrade to en-US on the settings-based path too");
	}

	[Test]
	[Description("A null EnvironmentSettings argument is rejected with ArgumentNullException before the culture-resolver factory is invoked (AC-ERR).")]
	public void Resolve_ShouldThrowArgumentNullException_WhenSettingsIsNull()
	{
		// Arrange
		(CaptionCultureResolver resolver, ICurrentUserCultureResolverFactory factory, _) =
			CreateResolverWithMocks(CultureResolution.Resolved("uk-UA"));

		// Act
		Action act = () => resolver.Resolve((EnvironmentSettings)null, null);

		// Assert
		act.Should().Throw<ArgumentNullException>(
			because: "a null settings argument is a programming error that must fail fast in the guard clause");
		factory.DidNotReceiveWithAnyArgs().Create(default!);
	}
}
