using System.Linq;
using Clio.Command.SqlScriptCommand;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[Property("Module", "Command")]
public class SqlScriptCommandTests : BaseCommandTests<ExecuteSqlScriptOptions> {
	private ISqlScriptExecutor _executor;
	private ILogger _logger;
	private SqlScriptCommand _sut;

	protected override void AdditionalRegistrations(IServiceCollection services) {
		_executor = Substitute.For<ISqlScriptExecutor>();
		_logger = Substitute.For<ILogger>();
		services.AddSingleton(_executor);
		services.AddSingleton(_logger);
	}

	public override void Setup() {
		base.Setup();
		_sut = Container.GetRequiredService<SqlScriptCommand>();
	}

	public override void TearDown() {
		_executor.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		base.TearDown();
	}

	[TestCase("csv", null, false)]
	[TestCase("CSV", "", false)]
	[TestCase("CsV", " \t", true)]
	[Description("CSV without a usable destination reports the missing option before executing SQL, including in silent mode.")]
	public void Execute_ShouldRejectBeforeSqlExecution_WhenCsvDestinationIsMissing(
		string view, string destination, bool silent) {
		// Arrange
		var options = new ExecuteSqlScriptOptions {
			Script = "SELECT 1 AS a", ViewType = view, DestPath = destination, IsSilent = silent
		};

		// Act
		int result = _sut.Execute(options);

		// Assert
		result.Should().Be(1, because: "missing CSV output is an invalid invocation");
		_executor.ReceivedCalls().Should().BeEmpty(because: "invalid output options must not execute SQL");
		_logger.ReceivedCalls().Select(call => call.GetArguments().FirstOrDefault()).Should()
			.Equal(new object[] { "-d/--destination-path is required when -v csv is used." },
				because: "the diagnostic must name both options without reporting success");
	}

	[TestCase("table", null)]
	[TestCase("json", null)]
	[Description("Table and JSON allow execution without a destination file.")]
	public void Execute_ShouldExecuteSql_WhenOutputOptionsAreValid(string view, string destination) {
		// Arrange
		var options = new ExecuteSqlScriptOptions {
			Script = "SELECT 1 AS a", ViewType = view, DestPath = destination, IsSilent = true
		};
		_executor.Execute(options.Script, Arg.Any<IApplicationClient>(), Arg.Any<EnvironmentSettings>())
			.Returns("[]");

		// Act
		int result = _sut.Execute(options);

		// Assert
		result.Should().Be(0, because: "valid output options must retain the successful execution path");
		_executor.ReceivedCalls().Should().ContainSingle(because: "SQL must execute exactly once");
		_logger.ReceivedCalls().Select(call => call.GetArguments().FirstOrDefault()).Should()
			.Equal(new object[] { "Done" }, because: "silent successful execution retains its completion message");
	}

	[Test]
	[Description("Reports SQL server errors as failures instead of a successful Done message, including silent exports.")]
	public void Execute_ShouldReturnFailure_WhenServerReportsSqlError() {
		// Arrange
		ExecuteSqlScriptOptions options = new() { Script = "invalid SQL", ViewType = "json", IsSilent = true };
		_executor.Execute(options.Script, Arg.Any<IApplicationClient>(), Arg.Any<EnvironmentSettings>())
			.Returns("ExecuteSQL ERROR: invalid query");

		// Act
		int exitCode = _sut.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "SQL errors must not be reported as successful exports");
		_logger.ReceivedCalls().Should().ContainSingle(call => call.GetMethodInfo().Name == nameof(ILogger.WriteError)
			&& (string)call.GetArguments()[0] == "invalid query", because: "silent mode must retain the actionable error");
		_logger.ReceivedCalls().Should().NotContain(call => call.GetArguments().Contains("Done"),
			because: "failure must never include a success marker");
	}

	[TestCase("<html>Service unavailable</html>")]
	[TestCase("{\"success\":false}")]
	[Description("Rejects malformed or unexpected JSON export responses instead of reporting successful SQL execution.")]
	public void Execute_ShouldRejectResponse_WhenJsonResultIsNotRowsOrCount(string response) {
		// Arrange
		ExecuteSqlScriptOptions options = new() { Script = "SELECT 1", ViewType = "json", IsSilent = true };
		_executor.Execute(options.Script, Arg.Any<IApplicationClient>(), Arg.Any<EnvironmentSettings>()).Returns(response);

		// Act
		int exitCode = _sut.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "an error page or unexpected object is not a SQL result");
		_logger.ReceivedCalls().Should().ContainSingle(call => call.GetMethodInfo().Name == nameof(ILogger.WriteError),
			because: "silent mode must not hide response validation errors");
	}

	[TestCase("table")]
	[TestCase("csv")]
	[TestCase("xlsx")]
	[Description("Rejects a non-JSON affected-row-count export instead of reporting success without writing the requested file.")]
	public void Execute_ShouldRejectCountExport_WhenFormatIsNotJson(string view) {
		// Arrange
		ExecuteSqlScriptOptions options = new() { Script = "UPDATE example", ViewType = view, DestPath = "unused", IsSilent = true };
		_executor.Execute(options.Script, Arg.Any<IApplicationClient>(), Arg.Any<EnvironmentSettings>()).Returns("3");

		// Act
		int exitCode = _sut.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "non-JSON count responses cannot satisfy the requested file format");
		_logger.ReceivedCalls().Should().ContainSingle(call => call.GetMethodInfo().Name == nameof(ILogger.WriteError)
			&& call.GetArguments()[0].ToString().Contains("-v json"), because: "the error must identify the supported count format");
	}
}
