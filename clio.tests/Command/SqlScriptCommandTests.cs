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
	[TestCase("csv", "result.csv")]
	[Description("Table and JSON still allow no destination, and CSV accepts a supplied destination.")]
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
}
