using Clio.Common;
using Clio.Tests.Command;
using FluentAssertions;
using Newtonsoft.Json;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

/// <summary>Guards SQL response decoding against changing JSON cell values.</summary>
[Property("Module", "Common")]
public sealed class SqlScriptExecutorTests : BaseClioModuleTests {
	private IApplicationClient _client;
	private ISqlScriptExecutor _executor;

	protected override void AdditionalRegistrations(IServiceCollection services) {
		_client = Substitute.For<IApplicationClient>();
		services.AddSingleton(_client);
	}

	public override void Setup() {
		base.Setup();
		_executor = Container.GetRequiredService<ISqlScriptExecutor>();
	}

	public override void TearDown() {
		_client.ClearReceivedCalls();
		base.TearDown();
	}

	[TestCase(true)]
	[TestCase(false)]
	[Description("Preserves quotes, Unicode, newlines, tabs and backslashes inside JSON cell values with either transport shape.")]
	public void Execute_ShouldPreserveJsonContent_WhenResponseContainsEscapes(bool wrapped) {
		// Arrange
		string expected = JsonConvert.SerializeObject(new[] { new { value = new string('X', 200) + "\"quoted\"\r\n\t\\path Привіт" } });
		_client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>())
			.Returns(wrapped ? JsonConvert.SerializeObject(expected) : expected);

		// Act
		string response = _executor.Execute("SELECT 1", _client, EnvironmentSettings);

		// Assert
		response.Should().Be(expected, because: "only the transport string should be decoded; inner JSON escapes are data");
	}

	[TestCase("[]", true)]
	[TestCase("3", true)]
	[TestCase("ExecuteSQL ERROR: invalid query", true)]
	[TestCase("[]", false)]
	[TestCase("3", false)]
	[TestCase("ExecuteSQL ERROR: invalid query", false)]
	[Description("Preserves empty results, row counts and errors in both wrapped and direct transport responses.")]
	public void Execute_ShouldPreserveResult_WhenResponseIsNotARowSet(string expected, bool wrapped) {
		// Arrange
		_client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>())
			.Returns(wrapped ? JsonConvert.SerializeObject(expected) : expected);

		// Act
		string response = _executor.Execute("SELECT 1", _client, EnvironmentSettings);

		// Assert
		response.Should().Be(expected, because: "error and count handling belongs to the command without transport changes");
	}
}
