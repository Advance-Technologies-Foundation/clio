using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[Author("Kirill Krylov", "k.krylov@creatio.com")]
[Category("Unit")]
[TestFixture]
[Property("Module", "Command")]
public class GetInfoCommandTests : BaseCommandTests<GetCreatioInfoCommandOptions>
{
	private const string ApplicationInfoMarker = "ApplicationInfoService.svc/GetApplicationInfo";
	private const string GetSysInfoMarker = "rest/CreatioApiGateway/GetSysInfo";

	private const string ApplicationInfoResponse =
		"""{ "applicationInfo": { "sysValues": { "coreVersion": "8.3.3.3292" } } }""";
	private const string SysInfoResponse =
		"""{ "SysInfo": { "CoreVersion": "8.3.3.3292", "ProductName": "studio" } }""";

	private static GetCreatioInfoCommand CreateCommand(IApplicationClient client, IClioGateway gateway)
	{
		EnvironmentSettings env = new() { Uri = "https://creatio.test", IsNetCore = true };
		return new GetCreatioInfoCommand(client, env, gateway) { Logger = Substitute.For<ILogger>() };
	}

	[Test]
	[Description("When cliogate is absent the command degrades to ApplicationInfoService instead of failing, returning success.")]
	public void Execute_Falls_Back_To_ApplicationInfo_When_Cliogate_Absent()
	{
		// Arrange — no cliogate gateway at all.
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(ApplicationInfoResponse);
		GetCreatioInfoCommand command = CreateCommand(client, gateway: null);

		// Act
		int result = command.Execute(new GetCreatioInfoCommandOptions());

		// Assert
		result.Should().Be(0,
			because: "the command must not fail when cliogate is missing — ApplicationInfoService still answers");
		client.Received().ExecutePostRequest(
			Arg.Is<string>(url => url.Contains(ApplicationInfoMarker)),
			Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
		client.DidNotReceive().ExecuteGetRequest(
			Arg.Is<string>(url => url.Contains(GetSysInfoMarker)), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("When the installed cliogate is older than required the command degrades to ApplicationInfoService rather than erroring out.")]
	public void Execute_Falls_Back_To_ApplicationInfo_When_Cliogate_Incompatible()
	{
		// Arrange — cliogate present but below the required version.
		IClioGateway gateway = Substitute.For<IClioGateway>();
		gateway.IsCompatibleWith(Arg.Any<string>()).Returns(false);
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(ApplicationInfoResponse);
		GetCreatioInfoCommand command = CreateCommand(client, gateway);

		// Act
		int result = command.Execute(new GetCreatioInfoCommandOptions());

		// Assert
		result.Should().Be(0,
			because: "an incompatible cliogate must trigger the ApplicationInfoService fallback, not a hard failure");
		client.Received().ExecutePostRequest(
			Arg.Is<string>(url => url.Contains(ApplicationInfoMarker)),
			Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("When a compatible cliogate is installed the command uses the full GetSysInfo report and does not call ApplicationInfoService.")]
	public void Execute_Uses_GetSysInfo_When_Cliogate_Compatible()
	{
		// Arrange — compatible cliogate present.
		IClioGateway gateway = Substitute.For<IClioGateway>();
		gateway.IsCompatibleWith(Arg.Any<string>()).Returns(true);
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(SysInfoResponse);
		GetCreatioInfoCommand command = CreateCommand(client, gateway);

		// Act
		int result = command.Execute(new GetCreatioInfoCommandOptions());

		// Assert
		result.Should().Be(0,
			because: "the full cliogate path must still work when cliogate is present and compatible");
		client.Received().ExecuteGetRequest(
			Arg.Is<string>(url => url.Contains(GetSysInfoMarker)), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
		client.DidNotReceive().ExecutePostRequest(
			Arg.Is<string>(url => url.Contains(ApplicationInfoMarker)),
			Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("When the ApplicationInfoService fallback returns an unexpected shape the command reports a clean failure.")]
	public void Execute_Returns_Error_When_ApplicationInfo_Shape_Unexpected()
	{
		// Arrange — no cliogate, and ApplicationInfoService gives an unusable body.
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("""{ "applicationInfo": { } }""");
		GetCreatioInfoCommand command = CreateCommand(client, gateway: null);

		// Act
		int result = command.Execute(new GetCreatioInfoCommandOptions());

		// Assert
		result.Should().Be(1,
			because: "an unusable ApplicationInfoService response must surface as a clean failure, not a crash");
	}
}
