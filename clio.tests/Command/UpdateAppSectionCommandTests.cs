using Clio.Command;
using Clio.Common;
using Clio.UserEnvironment;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
public sealed class UpdateAppSectionCommandTests : BaseCommandTests<UpdateAppSectionOptions> {
	private IApplicationSectionUpdateService _applicationSectionUpdateService = null!;
	private ILogger _logger = null!;
	private UpdateAppSectionCommand _command = null!;

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_applicationSectionUpdateService = Substitute.For<IApplicationSectionUpdateService>();
		_logger = Substitute.For<ILogger>();
		containerBuilder.AddSingleton(_applicationSectionUpdateService);
		containerBuilder.AddSingleton(_logger);
	}

	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<UpdateAppSectionCommand>();
	}

	public override void TearDown() {
		_applicationSectionUpdateService.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		base.TearDown();
	}

	[Test]
	[Description("Maps CLI options to the section-update service request and writes the structured result to the logger on success.")]
	public void Execute_Should_Map_Options_To_Service_Request() {
		// Arrange
		UpdateAppSectionOptions options = new() {
			Environment = "sandbox",
			ApplicationCode = "UsrOrdersApp",
			SectionCode = "UsrOrders",
			Caption = "Orders",
			Description = "Order workspace",
			IconId = "11111111-1111-1111-1111-111111111111",
			IconBackground = "#123456"
		};
		_applicationSectionUpdateService
			.UpdateSection("sandbox", Arg.Any<ApplicationSectionUpdateRequest>())
			.Returns(CreateResult());

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0,
			because: "successful section updates should return the standard success exit code");
		_applicationSectionUpdateService.Received(1).UpdateSection(
			"sandbox",
			Arg.Is<ApplicationSectionUpdateRequest>(request =>
				request.ApplicationCode == "UsrOrdersApp" &&
				request.SectionCode == "UsrOrders" &&
				request.Caption == "Orders" &&
				request.Description == "Order workspace" &&
				request.IconId == "11111111-1111-1111-1111-111111111111" &&
				request.IconBackground == "#123456" &&
				request.CaptionCulture == null));
		_logger.Received(1).WriteInfo(Arg.Is<string>(message => message.Contains("\"ApplicationId\":\"app-id\"")));
	}

	[Test]
	[Description("TC-U-48: passes --caption-culture to the service, and prints service warnings as warnings.")]
	public void Execute_Should_Pass_CaptionCulture_And_Print_Warnings() {
		// Arrange
		UpdateAppSectionOptions options = new() {
			Environment = "sandbox",
			ApplicationCode = "UsrOrdersApp",
			SectionCode = "UsrOrders",
			Caption = "Pedidos",
			CaptionCulture = "es-ES"
		};
		const string inactiveWarning = "Culture 'es-ES' exists but is inactive; users cannot select it until it is activated in the Languages section.";
		_applicationSectionUpdateService
			.UpdateSection("sandbox", Arg.Any<ApplicationSectionUpdateRequest>())
			.Returns(CreateResult() with {
				CaptionCulture = "es-ES",
				CaptionCultureValue = "Pedidos",
				Warnings = [inactiveWarning]
			});

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0,
			because: "a caption written in another culture is a successful update");
		_applicationSectionUpdateService.Received(1).UpdateSection(
			"sandbox",
			Arg.Is<ApplicationSectionUpdateRequest>(request =>
				request.Caption == "Pedidos" && request.CaptionCulture == "es-ES"));
		_logger.Received(1).WriteWarning(inactiveWarning);
	}

	[Test]
	[Description("Returns a failure exit code and logs a readable error when the CLI call omits environment-name.")]
	public void Execute_Should_Fail_When_Environment_Is_Missing() {
		// Arrange
		UpdateAppSectionOptions options = new() {
			ApplicationCode = "UsrOrdersApp",
			SectionCode = "UsrOrders",
			Caption = "Orders"
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1,
			because: "the command should fail fast when environment resolution input is missing");
		_applicationSectionUpdateService.DidNotReceiveWithAnyArgs().UpdateSection(default(string)!, default!);
		_applicationSectionUpdateService.DidNotReceiveWithAnyArgs().UpdateSection(default(EnvironmentSettings)!, default!);
		_logger.Received(1).WriteError(Arg.Is<string>(message => message.Contains("Environment name is required")));
	}

	private static ApplicationSectionUpdateResult CreateResult() =>
		new(
			"pkg-uid",
			"UsrOrdersApp",
			"app-id",
			"Orders App",
			"UsrOrdersApp",
			"8.3.0",
			new ApplicationSectionInfoResult("section-id", "UsrOrders", "{\"en-US\":\"Orders\"}", "Old", "UsrOrder", "pkg-uid", null, "icon-old", "#111111", null),
			new ApplicationSectionInfoResult("section-id", "UsrOrders", "Orders", "Order workspace", "UsrOrder", "pkg-uid", null, "11111111-1111-1111-1111-111111111111", "#123456", null));
}
