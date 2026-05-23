using System;
using Clio.Command;
using Clio.Common;
using ConsoleTables;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
public sealed class GetAppSectionsCommandTests : BaseCommandTests<ApplicationSectionGetListOptions> {
	private IApplicationSectionGetListService _service;
	private ILogger _logger;
	private GetAppSectionsCommand _command;

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_service = Substitute.For<IApplicationSectionGetListService>();
		_logger = Substitute.For<ILogger>();
		containerBuilder.AddSingleton(_service);
		containerBuilder.AddSingleton(_logger);
	}

	[SetUp]
	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<GetAppSectionsCommand>();
	}

	[TearDown]
	public void TearDown() {
		_service.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
	}

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
		ApplicationSectionGetListOptions options = new() {
			Environment = "sandbox",
			ApplicationCode = "UsrOrdersApp"
		};
		_service.GetSections("sandbox", Arg.Any<ApplicationSectionGetListRequest>())
			.Returns(BuildResult());

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, "successful section listing should return the standard success exit code");
		_service.Received(1).GetSections(
			"sandbox",
			Arg.Is<ApplicationSectionGetListRequest>(r => r.ApplicationCode == "UsrOrdersApp"));
		_logger.Received(1).WriteInfo(
			Arg.Is<string>(msg => msg.Contains("Orders App") && msg.Contains("UsrOrdersApp")));
		_logger.Received(1).PrintTable(Arg.Any<ConsoleTable>());
	}

	[Test]
	[Description("With --json flag: calls the service and writes indented JSON to the logger without printing a table.")]
	public void Execute_Should_Write_Indented_Json_When_Json_Flag_Is_Set() {
		// Arrange
		ApplicationSectionGetListOptions options = new() {
			Environment = "sandbox",
			ApplicationCode = "UsrOrdersApp",
			JsonFormat = true
		};
		_service.GetSections("sandbox", Arg.Any<ApplicationSectionGetListRequest>())
			.Returns(BuildResult());

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, "successful section listing should return the standard success exit code");
		_logger.Received(1).WriteInfo(
			Arg.Is<string>(msg => msg.Contains("\"ApplicationId\"") && msg.Contains(Environment.NewLine)));
		_logger.DidNotReceive().PrintTable(Arg.Any<ConsoleTable>());
	}

	[Test]
	[Description("Returns a failure exit code and logs a readable error when the CLI call omits environment-name.")]
	public void Execute_Should_Fail_When_Environment_Is_Missing() {
		// Act
		int result = _command.Execute(new ApplicationSectionGetListOptions { ApplicationCode = "UsrOrdersApp" });

		// Assert
		result.Should().Be(1, "the command should fail fast when environment resolution input is missing");
		_service.DidNotReceiveWithAnyArgs().GetSections(default!, default!);
		_logger.Received(1).WriteError(Arg.Is<string>(message => message.Contains("Environment name is required")));
	}

	[Test]
	[Description("Returns a failure exit code and logs the service exception message when the service throws.")]
	public void Execute_Should_Fail_When_Service_Throws() {
		// Arrange
		ApplicationSectionGetListOptions options = new() {
			Environment = "sandbox",
			ApplicationCode = "UsrMissing"
		};
		_service.GetSections(Arg.Any<string>(), Arg.Any<ApplicationSectionGetListRequest>())
			.Throws(new InvalidOperationException("Application 'UsrMissing' not found."));

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, "the command should return an error code when the service throws");
		_logger.Received(1).WriteError(Arg.Is<string>(message => message.Contains("not found")));
	}
}
