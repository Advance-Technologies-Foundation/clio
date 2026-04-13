using System;
using Clio.Command;
using Clio.Common;
using ConsoleTables;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
public sealed class GetAppSectionsCommandTests {

	private static ApplicationSectionGetListResult BuildResult() =>
		new(
			"pkg-uid",
			"UsrOrdersApp",
			"app-id",
			"Orders App",
			"UsrOrdersApp",
			"8.3.0",
			[new ApplicationSectionInfoResult("section-id", "UsrOrders", "Orders", null, "UsrOrder", "pkg-uid", null, null, null, null)]);

	[Test]
	[Description("Default (table) mode: calls the service, prints an application header, and renders a ConsoleTable.")]
	public void Execute_Should_Print_Table_And_Header_By_Default() {
		// Arrange
		IApplicationSectionGetListService service = Substitute.For<IApplicationSectionGetListService>();
		ILogger logger = Substitute.For<ILogger>();
		GetAppSectionsCommand command = new(service, logger);
		ApplicationSectionGetListOptions options = new() {
			Environment = "sandbox",
			ApplicationCode = "UsrOrdersApp"
		};
		service.GetSections("sandbox", Arg.Any<ApplicationSectionGetListRequest>())
			.Returns(BuildResult());

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(0, "successful section listing should return the standard success exit code");
		service.Received(1).GetSections(
			"sandbox",
			Arg.Is<ApplicationSectionGetListRequest>(r => r.ApplicationCode == "UsrOrdersApp"));
		logger.Received(1).WriteInfo(
			Arg.Is<string>(msg => msg.Contains("Orders App") && msg.Contains("UsrOrdersApp")));
		logger.Received(1).PrintTable(Arg.Any<ConsoleTable>());
	}

	[Test]
	[Description("With --json flag: calls the service and writes indented JSON to the logger without printing a table.")]
	public void Execute_Should_Write_Indented_Json_When_Json_Flag_Is_Set() {
		// Arrange
		IApplicationSectionGetListService service = Substitute.For<IApplicationSectionGetListService>();
		ILogger logger = Substitute.For<ILogger>();
		GetAppSectionsCommand command = new(service, logger);
		ApplicationSectionGetListOptions options = new() {
			Environment = "sandbox",
			ApplicationCode = "UsrOrdersApp",
			JsonFormat = true
		};
		service.GetSections("sandbox", Arg.Any<ApplicationSectionGetListRequest>())
			.Returns(BuildResult());

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(0, "successful section listing should return the standard success exit code");
		logger.Received(1).WriteInfo(
			Arg.Is<string>(msg => msg.Contains("\"ApplicationId\"") && msg.Contains(Environment.NewLine)));
		logger.DidNotReceive().PrintTable(Arg.Any<ConsoleTable>());
	}

	[Test]
	[Description("Returns a failure exit code and logs a readable error when the CLI call omits environment-name.")]
	public void Execute_Should_Fail_When_Environment_Is_Missing() {
		// Arrange
		IApplicationSectionGetListService service = Substitute.For<IApplicationSectionGetListService>();
		ILogger logger = Substitute.For<ILogger>();
		GetAppSectionsCommand command = new(service, logger);

		// Act
		int result = command.Execute(new ApplicationSectionGetListOptions { ApplicationCode = "UsrOrdersApp" });

		// Assert
		result.Should().Be(1, "the command should fail fast when environment resolution input is missing");
		service.DidNotReceiveWithAnyArgs().GetSections(default!, default!);
		logger.Received(1).WriteError(Arg.Is<string>(message => message.Contains("Environment name is required")));
	}

	[Test]
	[Description("Returns a failure exit code and logs the service exception message when the service throws.")]
	public void Execute_Should_Fail_When_Service_Throws() {
		// Arrange
		IApplicationSectionGetListService service = Substitute.For<IApplicationSectionGetListService>();
		ILogger logger = Substitute.For<ILogger>();
		GetAppSectionsCommand command = new(service, logger);
		ApplicationSectionGetListOptions options = new() {
			Environment = "sandbox",
			ApplicationCode = "UsrMissing"
		};
		service.GetSections(Arg.Any<string>(), Arg.Any<ApplicationSectionGetListRequest>())
			.Throws(new InvalidOperationException("Application 'UsrMissing' not found."));

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(1, "the command should return an error code when the service throws");
		logger.Received(1).WriteError(Arg.Is<string>(message => message.Contains("not found")));
	}
}
