using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Clio.Command;
using Clio.Common;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// HTTP-layer tests for <see cref="CompileBusinessProcessService"/>: the wrapped <c>{"request":{name|uid}}</c>
/// body, the route, the long single-attempt timeout, and the response mapping. The tool tests substitute the
/// command, so this is the only coverage of the clio-to-server contract for a process compile.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class CompileBusinessProcessServiceTests {

	private const string Env = "sandbox";
	private const string CompileUrl = "http://sandbox/0/rest/ProcessDesignService/CompileProcess";

	private static CompileBusinessProcessService CreateService(IApplicationClient client) {
		EnvironmentSettings env = new() { Uri = "http://sandbox", Login = "Supervisor", Password = "Supervisor" };
		ISettingsRepository settings = Substitute.For<ISettingsRepository>();
		settings.FindEnvironment(Env).Returns(env);
		IApplicationClientFactory factory = Substitute.For<IApplicationClientFactory>();
		factory.CreateEnvironmentClient(env).Returns(client);
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		urlBuilder.Build(ServiceUrlBuilder.KnownRoute.CompileProcess, env).Returns(CompileUrl);
		return new CompileBusinessProcessService(settings, factory, urlBuilder, Substitute.For<ILogger>());
	}

	private static JsonNode Wrapped(string body) => JsonNode.Parse(body)["request"];

	[Test]
	[Description("Posts the process name wrapped under 'request' to the CompileProcess route, once, with the compile bound rather than the default timeout, and maps the server's answer.")]
	public void Compile_ShouldPostTheWrappedRequestOnce_WithTheCompileTimeout() {
		// Arrange
		IOwnedApplicationClient client = Substitute.For<IOwnedApplicationClient>();
		client.ExecutePostRequest(CompileUrl, Arg.Any<string>(), Arg.Any<int>()).Returns(
			"{\"CompileProcessResult\":{\"success\":true,\"processName\":\"UsrProc\",\"packageName\":\"Custom\","
			+ "\"packageType\":\"general\",\"compileRequired\":true,\"compiled\":true,\"durationMs\":201000,"
			+ "\"errorCount\":0}}");
		CompileBusinessProcessService service = CreateService(client);

		// Act
		CompileBusinessProcessResult result =
			service.Compile(Env, new CompileBusinessProcessRequest("UsrProc", null));

		// Assert
		result.Success.Should().BeTrue(because: "the server reported a clean compile");
		result.PackageName.Should().Be("Custom", because: "the package compiled is the server's answer");
		result.PackageType.Should().Be("general", because: "the type says what the compile rebuilt");
		result.DurationMs.Should().Be(201000, because: "the compile's own duration is relayed");
		client.Received(1).ExecutePostRequest(CompileUrl,
			Arg.Is<string>(body => Wrapped(body)["name"].GetValue<string>() == "UsrProc"
				&& Wrapped(body)["uid"] == null),
			CompileBusinessProcessService.CompileTimeoutMs);
	}

	[Test]
	[Description("The compiler errors are mapped with their attribution, so the command can put the process's own first and mark the rest as another schema's.")]
	public void Compile_ShouldMapTheCompilerErrors_WithTheirAttribution() {
		// Arrange
		IOwnedApplicationClient client = Substitute.For<IOwnedApplicationClient>();
		client.ExecutePostRequest(CompileUrl, Arg.Any<string>(), Arg.Any<int>()).Returns(
			"{\"CompileProcessResult\":{\"success\":false,\"errorMessage\":\"Compiling package 'Custom' failed\","
			+ "\"compiled\":true,\"compileRequired\":true,\"errorCount\":3,\"errors\":["
			+ "{\"fileName\":\"UsrProc.Custom.cs\",\"line\":34,\"column\":12,\"code\":\"CS0103\","
			+ "\"message\":\"The name 'x' does not exist\",\"inThisProcess\":true},"
			+ "{\"fileName\":\"UsrOther.Custom.cs\",\"line\":5,\"column\":1,\"code\":\"CS1002\","
			+ "\"message\":\"; expected\",\"inThisProcess\":false}]}}");
		CompileBusinessProcessService service = CreateService(client);

		// Act
		CompileBusinessProcessResult result =
			service.Compile(Env, new CompileBusinessProcessRequest("UsrProc", null));

		// Assert
		result.Success.Should().BeFalse(because: "the server reported a failed compile");
		result.Errors.Should().HaveCount(2, because: "every reported error is mapped");
		result.Errors[0].Should().Be(new CompileBusinessProcessError("UsrProc.Custom.cs", 34, 12, "CS0103",
			"The name 'x' does not exist", true), because: "each field survives the mapping");
		result.Errors[1].InThisProcess.Should().BeFalse(because: "the attribution is the server's");
		result.ErrorCount.Should().Be(3, because: "the total past the server's cap is kept");
	}

	[Test]
	[Description("A process identified by uid is sent as 'uid' alone; the two identities are alternatives.")]
	public void Compile_ShouldSendTheUidAlone_WhenIdentifiedByUid() {
		// Arrange
		IOwnedApplicationClient client = Substitute.For<IOwnedApplicationClient>();
		client.ExecutePostRequest(CompileUrl, Arg.Any<string>(), Arg.Any<int>()).Returns(
			"{\"CompileProcessResult\":{\"success\":true,\"compileRequired\":false}}");
		CompileBusinessProcessService service = CreateService(client);

		// Act
		CompileBusinessProcessResult result = service.Compile(Env,
			new CompileBusinessProcessRequest(null, "5c58c4c4-134b-4744-9c67-96d9c69c9d55"));

		// Assert
		result.Success.Should().BeTrue(because: "the server answered success");
		client.Received(1).ExecutePostRequest(CompileUrl,
			Arg.Is<string>(body => Wrapped(body)["uid"].GetValue<string>() == "5c58c4c4-134b-4744-9c67-96d9c69c9d55"
				&& Wrapped(body)["name"] == null),
			CompileBusinessProcessService.CompileTimeoutMs);
	}

	[Test]
	[Description("A transport failure mid-compile says the outcome is unknown and not to compile again blindly: the compile may still be running, and the platform refuses a second one.")]
	public void Compile_ShouldSayTheOutcomeIsUnknown_WhenTheCallFails() {
		// Arrange
		IOwnedApplicationClient client = Substitute.For<IOwnedApplicationClient>();
		client.ExecutePostRequest(CompileUrl, Arg.Any<string>(), Arg.Any<int>())
			.Returns(_ => throw new TimeoutException("The operation has timed out."));
		CompileBusinessProcessService service = CreateService(client);

		// Act
		Action act = () => service.Compile(Env, new CompileBusinessProcessRequest("UsrProc", null));

		// Assert
		act.Should().Throw<InvalidOperationException>(because: "the call did not answer")
			.WithMessage("*UNKNOWN*still be running*", because: "a retry would be refused while the compile runs");
	}

	[Test]
	[Description("A body that is not the envelope - an HTML error page from a wrong route - says the compile outcome is unknown and where to read it, instead of a parser message.")]
	public void Compile_ShouldSayTheOutcomeIsUnknown_WhenTheBodyIsNotTheEnvelope() {
		// Arrange
		IOwnedApplicationClient client = Substitute.For<IOwnedApplicationClient>();
		client.ExecutePostRequest(CompileUrl, Arg.Any<string>(), Arg.Any<int>()).Returns("<html>error</html>");
		CompileBusinessProcessService service = CreateService(client);

		// Act
		Action act = () => service.Compile(Env, new CompileBusinessProcessRequest("UsrProc", null));

		// Assert
		act.Should().Throw<InvalidOperationException>(because: "the answer cannot be read")
			.WithMessage("*UNKNOWN*last-compilation-log*",
				because: "the caller is told what is unknown and where to find out");
	}
}

/// <summary>
/// Output and exit codes of <see cref="CompileBusinessProcessCommand"/>.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class CompileBusinessProcessCommandTests {

	private ICompileBusinessProcessService _service;
	private ILogger _logger;

	[SetUp]
	public void SetUp() {
		_service = Substitute.For<ICompileBusinessProcessService>();
		_logger = Substitute.For<ILogger>();
	}

	private int Execute(CompileBusinessProcessResult result, string processName = "UsrProc",
			string processUid = "") {
		_service.Compile("sandbox", Arg.Any<CompileBusinessProcessRequest>()).Returns(result);
		var command = new CompileBusinessProcessCommand(_service, _logger);
		return command.Execute(new CompileBusinessProcessOptions {
			Environment = "sandbox", ProcessName = processName, ProcessUid = processUid
		});
	}

	private static CompileBusinessProcessResult Result(bool success, bool compileRequired = true,
			IReadOnlyList<CompileBusinessProcessError> errors = null, int errorCount = 0,
			string errorMessage = null) =>
		new(success, errorMessage, "UsrProc", "Custom", "general", compileRequired, compileRequired, 201000,
			errors ?? [], errorCount);

	[Test]
	[Description("A clean compile exits 0 and says which package was compiled, how long it took, and that the process now runs its saved code.")]
	public void Execute_ShouldReportTheCompiledPackage_OnSuccess() {
		// Arrange
		CompileBusinessProcessResult result = Result(success: true);

		// Act
		int exitCode = Execute(result);

		// Assert
		exitCode.Should().Be(0, because: "the compile succeeded");
		_logger.Received(1).WriteInfo(Arg.Is<string>(message => message.Contains("Compiled package 'Custom'")
			&& message.Contains("3:21") && message.Contains("now runs the code it was saved with")));
	}

	[Test]
	[Description("A process without C# exits 0 and says nothing was compiled, rather than claiming a compile that did not happen.")]
	public void Execute_ShouldSayNothingWasCompiled_ForAProcessWithoutCSharp() {
		// Arrange
		CompileBusinessProcessResult result = Result(success: true, compileRequired: false);

		// Act
		int exitCode = Execute(result);

		// Assert
		exitCode.Should().Be(0, because: "there was nothing to compile");
		_logger.Received(1).WriteInfo(Arg.Is<string>(message => message.Contains("nothing was compiled")));
	}

	[Test]
	[Description("A failed compile exits 1 and prints one line per error, the ones in another schema marked as such, plus how many were not listed.")]
	public void Execute_ShouldListTheErrors_OnFailure() {
		// Arrange
		CompileBusinessProcessResult result = Result(success: false, errorCount: 5,
			errorMessage: "Compiling package 'Custom' failed with 5 error(s)",
			errors: [
				new CompileBusinessProcessError("UsrProc.Custom.cs", 34, 12, "CS0103", "The name 'x'", true),
				new CompileBusinessProcessError("UsrOther.Custom.cs", 5, 1, "CS1002", "; expected", false)
			]);

		// Act
		int exitCode = Execute(result);

		// Assert
		exitCode.Should().Be(1, because: "the compile failed");
		_logger.Received(1).WriteError("UsrProc.Custom.cs(34,12): CS0103 The name 'x'");
		_logger.Received(1).WriteError(Arg.Is<string>(message => message.StartsWith("UsrOther.Custom.cs(5,1)")
			&& message.Contains("[another schema of the package]")));
		_logger.Received(1).WriteError("... and 3 more error(s) not listed.");
	}

	[Test]
	[Description("Exactly one identity is required: both or neither is refused without calling the server.")]
	[TestCase("UsrProc", "5c58c4c4-134b-4744-9c67-96d9c69c9d55")]
	[TestCase("", "")]
	public void Execute_ShouldRequireExactlyOneIdentity(string processName, string processUid) {
		// Arrange
		CompileBusinessProcessResult result = Result(success: true);

		// Act
		int exitCode = Execute(result, processName, processUid);

		// Assert
		exitCode.Should().Be(1, because: "the identity is ambiguous or missing");
		_service.DidNotReceiveWithAnyArgs().Compile(default, default);
	}
}
