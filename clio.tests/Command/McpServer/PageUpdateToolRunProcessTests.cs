using System.Collections.Generic;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Command.ProcessModel;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Unit tests for the run-process button orchestration inside <see cref="PageUpdateTool"/>:
/// environment gating, per-process signature caching, hard-fail vs. warning routing, and
/// warning aggregation. The pure code/signature comparison itself is covered by
/// <see cref="RunProcessButtonSignatureValidatorTests"/>; here we exercise the glue that wires
/// the resolver, the cache, and the warning/failure channels together.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class PageUpdateToolRunProcessTests {

	private static string WrapRunProcessButton(string processName) =>
		"define(\"UsrTest_FormPage\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function() { return {"
		+ "viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[ { \"operation\": \"insert\", \"name\": \"RunBpButton\","
		+ " \"values\": { \"type\": \"crt.Button\", \"clicked\": { \"request\": \"crt.RunBusinessProcessRequest\","
		+ " \"params\": { \"processName\": \"" + processName + "\","
		+ " \"processParameters\": { \"ProcessSchemaParameter2\": \"x\" } } } } } ]/**SCHEMA_VIEW_CONFIG_DIFF*/,"
		+ "handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/ }; });";

	private static ProcessSignatureParameter Param(string name, string direction = "Input") =>
		new() { Name = name, Direction = direction };

	private static (PageUpdateTool Tool, IToolCommandResolver Resolver) BuildTool(GetProcessSignatureCommand signatureCommand) {
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		if (signatureCommand != null) {
			resolver.Resolve<GetProcessSignatureCommand>(Arg.Any<GetProcessSignatureOptions>())
				.Returns(signatureCommand);
		}
		PageUpdateTool tool = new(
			command: null,
			logger: ConsoleLogger.Instance,
			commandResolver: resolver,
			mobileComponentCatalog: Substitute.For<IMobileComponentInfoCatalog>(),
			webComponentCatalog: Substitute.For<IComponentInfoCatalog>(),
			samplingService: Substitute.For<IPageBodySamplingService>(),
			pageBaselineGuard: new PageBaselineGuard(Substitute.For<System.IO.Abstractions.IFileSystem>()),
			guidanceAccessLedger: PageLayoutGuidanceGateTestSupport.SatisfiedLedger(),
			layoutCompositionDetector: PageLayoutGuidanceGateTestSupport.Detector());
		return (tool, resolver);
	}

	[Test]
	[Description("Warns (does not block) and never resolves a signature when no environment is provided.")]
	public void ValidateRunProcessButtons_Should_Warn_When_No_Environment() {
		// Arrange
		(PageUpdateTool tool, IToolCommandResolver resolver) = BuildTool(signatureCommand: null);
		PageUpdateOptions options = new() { Body = WrapRunProcessButton("UsrProcess_e629820") };

		// Act
		(PageUpdateResponse failure, IReadOnlyList<string> warnings) = tool.ValidateRunProcessButtons(options);

		// Assert
		failure.Should().BeNull(because: "a missing environment must not block the write");
		warnings.Should().ContainSingle(because: "the operator should be told validation was skipped")
			.Which.Should().Contain("no environment", because: "the warning should explain why codes were not validated");
		resolver.DidNotReceive().Resolve<GetProcessSignatureCommand>(Arg.Any<GetProcessSignatureOptions>());
	}

	[Test]
	[Description("Returns no failure and no warnings when the body has no run-process buttons.")]
	public void ValidateRunProcessButtons_Should_Be_Noop_Without_RunProcess_Buttons() {
		// Arrange
		(PageUpdateTool tool, _) = BuildTool(signatureCommand: null);
		PageUpdateOptions options = new() {
			Environment = "test-env",
			Body = "define(\"X\", [], function(){ return {}; });"
		};

		// Act
		(PageUpdateResponse failure, IReadOnlyList<string> warnings) = tool.ValidateRunProcessButtons(options);

		// Assert
		failure.Should().BeNull(because: "there is nothing to validate");
		warnings.Should().BeNull(because: "no buttons means no warnings are produced");
	}

	[Test]
	[Description("Hard-fails when the process cannot be uniquely resolved (ProcessResolutionFailed).")]
	public void ValidateAgainstSignatures_Should_HardFail_When_Process_Resolution_Failed() {
		// Arrange
		var signature = new GetProcessSignatureResponse {
			Success = false,
			ProcessResolutionFailed = true,
			Error = "Could not find process with name or caption:UsrMissing"
		};
		(PageUpdateTool tool, _) = BuildTool(new StubSignatureCommand(signature));
		PageUpdateOptions options = new() { Environment = "test-env" };
		RunProcessButtonConfig config = new("RunBpButton", "UsrMissing", "RegardlessOfThePage",
			["ProcessSchemaParameter2"]);

		// Act
		(PageUpdateResponse failure, IReadOnlyList<string> warnings) =
			tool.ValidateRunProcessButtonsAgainstSignatures(options, [config]);

		// Assert
		failure.Should().NotBeNull(because: "an unresolved process is a definitive, blocking error");
		failure.Success.Should().BeFalse(because: "the write must not proceed");
		failure.Error.Should().Contain("UsrMissing", because: "the failure should name the offending process");
		failure.Error.Should().Contain("get-process-signature",
			because: "the operator should be pointed at the tool that returns the correct processCode");
		warnings.Should().BeNull(because: "a hard failure does not also emit warnings");
	}

	[Test]
	[Description("Downgrades a transient/transport failure to a warning so a backend hiccup does not block the write.")]
	public void ValidateAgainstSignatures_Should_Warn_On_Transient_Failure() {
		// Arrange
		var signature = new GetProcessSignatureResponse {
			Success = false,
			ProcessResolutionFailed = false,
			Error = "Error at step: ExecuteRequest. timeout"
		};
		(PageUpdateTool tool, _) = BuildTool(new StubSignatureCommand(signature));
		PageUpdateOptions options = new() { Environment = "test-env" };
		RunProcessButtonConfig config = new("RunBpButton", "UsrProcess_e629820", "RegardlessOfThePage",
			["ProcessSchemaParameter2"]);

		// Act
		(PageUpdateResponse failure, IReadOnlyList<string> warnings) =
			tool.ValidateRunProcessButtonsAgainstSignatures(options, [config]);

		// Assert
		failure.Should().BeNull(because: "a transport hiccup is not a definitive resolution failure");
		warnings.Should().ContainSingle(because: "the skipped validation should be surfaced as advisory")
			.Which.Should().Contain("were not validated",
				because: "the warning should explain codes could not be checked this time");
	}

	[Test]
	[Description("Hard-fails when a referenced parameter code does not exist on the resolved signature.")]
	public void ValidateAgainstSignatures_Should_HardFail_On_Unknown_Code() {
		// Arrange
		var signature = new GetProcessSignatureResponse {
			Success = true,
			Parameters = [Param("ProcessSchemaParameter2")]
		};
		(PageUpdateTool tool, _) = BuildTool(new StubSignatureCommand(signature));
		PageUpdateOptions options = new() { Environment = "test-env" };
		RunProcessButtonConfig config = new("RunBpButton", "UsrProcess_e629820", "RegardlessOfThePage",
			["Parameter2"]);

		// Act
		(PageUpdateResponse failure, IReadOnlyList<string> warnings) =
			tool.ValidateRunProcessButtonsAgainstSignatures(options, [config]);

		// Assert
		failure.Should().NotBeNull(because: "a code that is not a real parameter is silently dropped by the platform");
		failure.Error.Should().Contain("Parameter2", because: "the failure should name the unknown code");
		warnings.Should().BeNull(because: "a hard failure does not also emit warnings");
	}

	[Test]
	[Description("Succeeds with an advisory warning when a referenced code resolves to an Output-only parameter.")]
	public void ValidateAgainstSignatures_Should_Pass_With_Warning_For_Output_Only_Code() {
		// Arrange
		var signature = new GetProcessSignatureResponse {
			Success = true,
			Parameters = [Param("ResultParam", "Output")]
		};
		(PageUpdateTool tool, _) = BuildTool(new StubSignatureCommand(signature));
		PageUpdateOptions options = new() { Environment = "test-env" };
		RunProcessButtonConfig config = new("RunBpButton", "UsrProcess_e629820", "RegardlessOfThePage",
			["ResultParam"]);

		// Act
		(PageUpdateResponse failure, IReadOnlyList<string> warnings) =
			tool.ValidateRunProcessButtonsAgainstSignatures(options, [config]);

		// Assert
		failure.Should().BeNull(because: "an output-only target exists, so it is not a hard error");
		warnings.Should().ContainSingle(because: "an output-only target is advisory, not blocking")
			.Which.Should().Contain("ResultParam", because: "the warning should name the parameter");
	}

	[Test]
	[Description("Resolves each distinct process signature only once, reusing the cache across buttons.")]
	public void ValidateAgainstSignatures_Should_Cache_Signature_Per_Process() {
		// Arrange
		var signature = new GetProcessSignatureResponse {
			Success = true,
			Parameters = [Param("ProcessSchemaParameter2")]
		};
		(PageUpdateTool tool, IToolCommandResolver resolver) = BuildTool(new StubSignatureCommand(signature));
		PageUpdateOptions options = new() { Environment = "test-env" };
		RunProcessButtonConfig first = new("ButtonA", "UsrProcess_e629820", "RegardlessOfThePage",
			["ProcessSchemaParameter2"]);
		RunProcessButtonConfig second = new("ButtonB", "UsrProcess_e629820", "RegardlessOfThePage",
			["ProcessSchemaParameter2"]);

		// Act
		(PageUpdateResponse failure, _) =
			tool.ValidateRunProcessButtonsAgainstSignatures(options, [first, second]);

		// Assert
		failure.Should().BeNull(because: "both buttons reference a valid code on the same process");
		resolver.Received(1).Resolve<GetProcessSignatureCommand>(Arg.Any<GetProcessSignatureOptions>());
	}

	/// <summary>
	/// Stand-in <see cref="GetProcessSignatureCommand"/> that returns a preset signature without
	/// touching the network, so the orchestration paths can be exercised deterministically.
	/// </summary>
	private sealed class StubSignatureCommand : GetProcessSignatureCommand {
		private readonly GetProcessSignatureResponse _response;

		public StubSignatureCommand(GetProcessSignatureResponse response)
			: base(Substitute.For<IProcessModelGenerator>(), ConsoleLogger.Instance) => _response = response;

		public override bool TryGetSignature(GetProcessSignatureOptions options,
			out GetProcessSignatureResponse response) {
			response = _response;
			return _response.Success;
		}
	}
}
