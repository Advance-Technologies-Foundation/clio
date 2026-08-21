using Clio.Common;
using Clio.Query;
using Clio.Tests.Command;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using IFileSystem = Clio.Common.IFileSystem;

namespace Clio.Tests.Query;

/// <summary>
/// Regression coverage for GH-1159: the "{{name}}" template-variable substitution used to splice
/// the raw variable name into a Regex PATTERN unescaped, allowing regex-metacharacter names to
/// change the meaning of the pattern (regex injection) instead of merely being matched as literal
/// text.
/// </summary>
[TestFixture]
[Property("Module", "Query")]
public class CallServiceCommandVariableSubstitutionTests : BaseCommandTests<CallServiceCommandOptions> {

	#region Methods: Public

	[Test]
	[Description("Verifies a normal name=value template variable is still substituted into the request body")]
	public void Execute_ShouldSubstituteVariable_WhenNameIsPlainText() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		EnvironmentSettings settings = new();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		serviceUrlBuilder.Build("svc").Returns("http://host/svc");

		CallServiceCommand command = new(applicationClient, settings, serviceUrlBuilder, fileSystem);
		CallServiceCommandOptions options = new() {
			ServicePath = "svc",
			RequestBody = "{\"user\":\"{{name}}\"}",
			Variables = ["name=John"]
		};

		// Act
		command.Execute(options);

		// Assert
		applicationClient
			.Received(1)
			.ExecutePostRequest("http://host/svc", "{\"user\":\"John\"}", Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("Verifies a variable name containing regex metacharacters is treated as a literal placeholder and does not throw")]
	public void Execute_ShouldTreatVariableNameAsLiteral_WhenNameContainsRegexMetacharacters() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		EnvironmentSettings settings = new();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		serviceUrlBuilder.Build("svc").Returns("http://host/svc");

		CallServiceCommand command = new(applicationClient, settings, serviceUrlBuilder, fileSystem);
		CallServiceCommandOptions options = new() {
			ServicePath = "svc",
			RequestBody = "{\"user\":\"{{na(me}}\"}",
			Variables = ["na(me=John"]
		};

		// Act
		System.Func<int> action = () => command.Execute(options);

		// Assert
		action.Should().NotThrow(
			because: "an unbalanced '(' in the variable name must not be interpreted as regex syntax after escaping");
		applicationClient
			.Received(1)
			.ExecutePostRequest("http://host/svc", "{\"user\":\"John\"}", Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("Verifies a variable name with nested quantifiers is matched literally instead of being compiled as a catastrophic-backtracking regex")]
	public void Execute_ShouldTreatVariableNameAsLiteral_WhenNameContainsNestedQuantifiers() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		EnvironmentSettings settings = new();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		serviceUrlBuilder.Build("svc").Returns("http://host/svc");

		CallServiceCommand command = new(applicationClient, settings, serviceUrlBuilder, fileSystem);
		CallServiceCommandOptions options = new() {
			ServicePath = "svc",
			RequestBody = "{\"user\":\"{{((a+)+)}}\"}",
			Variables = ["((a+)+)=John"]
		};

		// Act
		System.Func<int> action = () => command.Execute(options);

		// Assert
		action.Should().NotThrow(
			because: "nested quantifiers in the variable name must be escaped and matched as literal text, not compiled as regex syntax");
		applicationClient
			.Received(1)
			.ExecutePostRequest("http://host/svc", "{\"user\":\"John\"}", Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	#endregion

}
