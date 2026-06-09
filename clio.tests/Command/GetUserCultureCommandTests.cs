using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Command.EntitySchemaDesigner;
using Clio.Common;
using Clio.UserEnvironment;
using CommandLine;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
public sealed class GetUserCultureCommandTests : BaseCommandTests<GetUserCultureCommandOptions>
{
	private ICurrentUserCultureResolverFactory _resolverFactory;
	private ICurrentUserCultureResolver _resolver;
	private ISettingsRepository _settingsRepository;
	private ILogger _logger;
	private GetUserCultureCommand _command;

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder)
	{
		base.AdditionalRegistrations(containerBuilder);
		_resolver = Substitute.For<ICurrentUserCultureResolver>();
		_resolverFactory = Substitute.For<ICurrentUserCultureResolverFactory>();
		_resolverFactory.Create(Arg.Any<EnvironmentSettings>()).Returns(_resolver);
		_settingsRepository = Substitute.For<ISettingsRepository>();
		_settingsRepository.GetEnvironment(Arg.Any<EnvironmentOptions>()).Returns(new EnvironmentSettings());
		_logger = Substitute.For<ILogger>();
		containerBuilder.AddSingleton(_resolverFactory);
		containerBuilder.AddSingleton(_settingsRepository);
		containerBuilder.AddSingleton(_logger);
	}

	[SetUp]
	public override void Setup()
	{
		base.Setup();
		_command = Container.GetRequiredService<GetUserCultureCommand>();
	}

	[TearDown]
	public override void TearDown()
	{
		_resolver.ClearReceivedCalls();
		_resolverFactory.ClearReceivedCalls();
		_settingsRepository.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		base.TearDown();
	}

	[Test]
	[Description("Prints the resolved culture and returns zero when the resolver succeeds.")]
	public void Execute_ShouldPrintResolvedCulture_WhenResolutionSucceeds()
	{
		// Arrange
		_resolver.ResolveAsync(Arg.Any<CancellationToken>())
			.Returns(Task.FromResult(CultureResolution.Resolved("uk-UA")));
		GetUserCultureCommandOptions options = new() { Environment = "dev" };

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0,
			because: "a successful resolution is a zero exit code");
		_logger.Received(1).WriteLine("uk-UA");
		_logger.DidNotReceive().WriteError(Arg.Any<string>());
	}

	[Test]
	[Description("Prints a user-friendly Error and returns non-zero when the resolver fails.")]
	public void Execute_ShouldPrintErrorAndReturnNonZero_WhenResolutionFails()
	{
		// Arrange
		_resolver.ResolveAsync(Arg.Any<CancellationToken>())
			.Returns(Task.FromResult(CultureResolution.Failed(CurrentUserCultureResolver.ReasonUserCultureMissing)));
		GetUserCultureCommandOptions options = new() { Environment = "dev" };

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1,
			because: "the verb has no override, so a failed resolution is a hard error");
		_logger.Received(1).WriteError(Arg.Is<string>(value =>
			value.StartsWith("Error:", StringComparison.Ordinal)));
		_logger.DidNotReceive().WriteLine(Arg.Any<string>());
	}

	[Test]
	[Description("The verb exposes the profile-language alias so it behaves identically under either name.")]
	public void Verb_ShouldExposeProfileLanguageAlias_ForBackwardFriendlyNaming()
	{
		// Arrange
		VerbAttribute verb = (VerbAttribute)Attribute.GetCustomAttribute(
			typeof(GetUserCultureCommandOptions), typeof(VerbAttribute));

		// Act
		string[] aliases = verb.Aliases;

		// Assert
		verb.Name.Should().Be("get-user-culture",
			because: "the canonical verb name is kebab-case get-user-culture");
		aliases.Should().Contain("profile-language",
			because: "the friendly alias must resolve to the same command");
	}
}
