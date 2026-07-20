using System.Threading;
using System.Threading.Tasks;
using Clio.Command.EntitySchemaDesigner;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class GetUserCultureToolTests {
	private static GetUserCultureTool CreateTool(CultureResolution resolution) {
		ICurrentUserCultureResolver resolver = Substitute.For<ICurrentUserCultureResolver>();
		resolver.ResolveAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(resolution));
		ICurrentUserCultureResolverFactory factory = Substitute.For<ICurrentUserCultureResolverFactory>();
		factory.Create(Arg.Any<EnvironmentSettings>()).Returns(resolver);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<EnvironmentSettings>(Arg.Any<EnvironmentOptions>()).Returns(new EnvironmentSettings());
		return new GetUserCultureTool(factory, commandResolver);
	}

	[Test]
	[Description("Maps a resolved culture to a success signal with the culture and the environment tier.")]
	public async Task GetUserCulture_ShouldReturnSuccessSignal_WhenResolutionSucceeds() {
		// Arrange
		GetUserCultureTool tool = CreateTool(CultureResolution.Resolved("uk-UA"));

		// Act
		GetUserCultureResponse response = await tool.GetUserCulture(new GetUserCultureArgs(EnvironmentName: "dev"));

		// Assert
		response.Success.Should().BeTrue(because: "a resolved culture is a success signal");
		response.Culture.Should().Be("uk-UA", because: "the resolved profile culture must be surfaced verbatim");
		response.ResolvedFrom.Should().Be(GetUserCultureTool.ResolvedFromEnvironment,
			because: "a successful resolution is tagged as coming from the environment");
		response.Reason.Should().BeNull(because: "a success signal carries no failure reason");
	}

	[Test]
	[Description("Maps each failure reason to a failure signal without leaking the en-US fallback culture (AC-04 / NEW-6).")]
	[TestCase(CurrentUserCultureResolver.ReasonUserCultureMissing)]
	[TestCase(CurrentUserCultureResolver.ReasonUserCultureInvalid)]
	[TestCase(CurrentUserCultureResolver.ReasonUnreachable)]
	[TestCase(CurrentUserCultureResolver.ReasonUnauthorized)]
	public async Task GetUserCulture_ShouldReturnFailureSignalWithReason_WhenResolutionFails(string reason) {
		// Arrange
		GetUserCultureTool tool = CreateTool(CultureResolution.Failed(reason));

		// Act
		GetUserCultureResponse response = await tool.GetUserCulture(new GetUserCultureArgs(EnvironmentName: "dev"));

		// Assert
		response.Success.Should().BeFalse(because: "a failed resolution must surface success:false");
		response.Reason.Should().Be(reason, because: "the agent needs the machine-readable failure reason to ask the user");
		response.Culture.Should().BeNull(
			because: "the en-US fallback carried by CultureResolution.Failed must never leak as a resolved culture (NEW-6)");
		response.ResolvedFrom.Should().Be(GetUserCultureTool.ResolvedFromFailed,
			because: "a failed resolution is not tagged as coming from the environment");
	}
}
