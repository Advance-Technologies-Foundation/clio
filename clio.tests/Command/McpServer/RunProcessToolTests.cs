using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Command.McpServer.Tools.ProcessDesigner;
using Clio.Command.ProcessModel;
using Clio.Command.StartProcess;
using Clio.Common;
using ErrorOr;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using ProcessModelType = Clio.Command.ProcessModel.ProcessModel;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public sealed class RunProcessToolTests {

	private const string ProcessCode = "MigrateDashboardsProcess";
	private const string GuidTypeUId = "23018567-a13c-4320-8687-fd6f9e3699bd";
	private const string TextTypeUId = "c0f04627-4620-4bc0-84e5-9419dc8516b1";

	private static ProcessParameter Parameter(string name, string dataValueTypeUId,
		ProcessParameterDirection direction, Guid? referenceSchemaUId = null) =>
		new() {
			Name = name,
			DataValueType = Guid.Parse(dataValueTypeUId),
			Direction = direction,
			ReferenceSchemaUId = referenceSchemaUId
		};

	private static List<ProcessParameter> MigratorSignature() => [
		Parameter("SysModulesSelectedId", GuidTypeUId, ProcessParameterDirection.Input),
		Parameter("SysDashboardsSelectionStateFilter", TextTypeUId, ProcessParameterDirection.Input),
		Parameter("MigratedCount", GuidTypeUId, ProcessParameterDirection.Output)
	];

	private sealed record Harness(
		RunProcessCommand Command,
		IApplicationClient ApplicationClient,
		List<string> PostedBodies,
		List<int> PostedTimeouts,
		List<int> PostedAttempts);

	private static Harness BuildHarness(List<ProcessParameter> signature, string platformResponseJson = null) {
		IProcessModelGenerator generator = Substitute.For<IProcessModelGenerator>();
		generator.Generate(Arg.Any<GenerateProcessModelCommandOptions>())
			.Returns(_ => new ProcessModelType(Guid.NewGuid(), ProcessCode) {
				Name = "Migrate dashboards process",
				Parameters = signature
			});

		List<string> bodies = [];
		List<int> timeouts = [];
		List<int> attempts = [];
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		applicationClient
			.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(call => {
				bodies.Add(call.ArgAt<string>(1));
				timeouts.Add(call.ArgAt<int>(2));
				attempts.Add(call.ArgAt<int>(3));
				return platformResponseJson
					?? """{"processId":"0f5e3a2a-2c8f-4f1e-9d0b-6d4b2f1a7c31","processStatus":2,"success":true}""";
			});

		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build(Arg.Any<ServiceUrlBuilder.KnownRoute>())
			.Returns("ServiceModel/ProcessEngineService.svc/RunProcess");

		RunProcessCommand command = new(generator, applicationClient, serviceUrlBuilder, ConsoleLogger.Instance);
		return new Harness(command, applicationClient, bodies, timeouts, attempts);
	}

	private static Dictionary<string, JsonElement> Values(string json) =>
		JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);

	private static ProcessStartResponse PlatformResponse(string json) =>
		JsonSerializer.Deserialize<ProcessStartResponse>(json);

	#region Pre-call validation

	[Test]
	[Category("Unit")]
	[Description("An unknown parameter code is rejected before any Creatio call, and the error lists the codes the process really accepts.")]
	public void TryRun_Should_Reject_Unknown_Parameter_Code_Before_Any_Server_Call() {
		// Arrange
		Harness harness = BuildHarness(MigratorSignature());
		RunProcessOptions options = new() {
			ProcessName = ProcessCode,
			Parameters = Values("""{"SysModuleSelectedId":"a"}""")
		};

		// Act
		bool launched = harness.Command.TryRun(options, out RunProcessResponse response);

		// Assert
		launched.Should().BeFalse(because: "an unknown code is a hard error, not a warning");
		response.Error.Should().Contain("SysModulesSelectedId",
			because: "the caller needs the list of codes the process actually accepts to fix the call");
		harness.PostedBodies.Should().BeEmpty(
			because: "validation must run BEFORE the server call so a bad code never reaches the platform");
	}

	[Test]
	[Category("Unit")]
	[Description("A code that differs only by case is reported as a case mismatch with the correct spelling, because the platform matches parameter names with Ordinal comparison.")]
	public void TryRun_Should_Report_Case_Sensitivity_On_Case_Only_Mismatch() {
		// Arrange
		Harness harness = BuildHarness(MigratorSignature());
		RunProcessOptions options = new() {
			ProcessName = ProcessCode,
			Parameters = Values("""{"sysmodulesselectedid":"11111111-1111-1111-1111-111111111111"}""")
		};

		// Act
		harness.Command.TryRun(options, out RunProcessResponse response);

		// Assert
		response.Error.Should().Contain("case-sensitive",
			because: "the platform compares parameter names with StringComparison.Ordinal, so the caller must "
				+ "be told the difference is the casing rather than a missing parameter");
		response.Error.Should().Contain("SysModulesSelectedId",
			because: "naming the correctly-cased code makes the fix a single edit");
		harness.PostedBodies.Should().BeEmpty(because: "the call is refused before any server round-trip");
	}

	[Test]
	[Category("Unit")]
	[Description("An Output parameter cannot be assigned through 'parameters'; the error points the caller at 'result-parameters'.")]
	public void TryRun_Should_Reject_Output_Parameter_In_Parameters() {
		// Arrange
		Harness harness = BuildHarness(MigratorSignature());
		RunProcessOptions options = new() {
			ProcessName = ProcessCode,
			Parameters = Values("""{"MigratedCount":"11111111-1111-1111-1111-111111111111"}""")
		};

		// Act
		harness.Command.TryRun(options, out RunProcessResponse response);

		// Assert
		response.Error.Should().Contain("Output",
			because: "the caller must learn the direction is what makes the assignment invalid");
		response.Error.Should().Contain("result-parameters",
			because: "the message must name the argument that CAN read an output back");
		harness.PostedBodies.Should().BeEmpty(because: "the direction check runs before the server call");
	}

	[Test]
	[Category("Unit")]
	[Description("An Input parameter cannot be read through 'result-parameters'. The platform verifies requested result names before the process starts and throws, so catching it first turns an opaque server failure into an actionable message.")]
	public void TryRun_Should_Reject_Input_Parameter_In_ResultParameters() {
		// Arrange
		Harness harness = BuildHarness(MigratorSignature());
		RunProcessOptions options = new() {
			ProcessName = ProcessCode,
			ResultParameters = ["SysModulesSelectedId"]
		};

		// Act
		harness.Command.TryRun(options, out RunProcessResponse response);

		// Assert
		response.Error.Should().Contain("Input",
			because: "the direction is the reason the request is invalid");
		harness.PostedBodies.Should().BeEmpty(
			because: "the platform would abort the launch itself, so clio must refuse first with a better message");
	}

	[Test]
	[Category("Unit")]
	[Description("A lookup parameter given a display name instead of a record id is rejected with a message that names the fix.")]
	public void TryRun_Should_Reject_Non_Guid_For_Lookup_Parameter() {
		// Arrange
		Harness harness = BuildHarness([
			Parameter("Owner", GuidTypeUId, ProcessParameterDirection.Input, Guid.NewGuid())
		]);
		RunProcessOptions options = new() {
			ProcessName = ProcessCode,
			Parameters = Values("""{"Owner":"Supervisor"}""")
		};

		// Act
		harness.Command.TryRun(options, out RunProcessResponse response);

		// Assert
		response.Error.Should().Contain("record id",
			because: "a lookup parameter takes the referenced record's Id, never its display value");
		harness.PostedBodies.Should().BeEmpty(because: "the coercion check runs before the server call");
	}

	#endregion

	#region Value serialization

	[Test]
	[Category("Unit")]
	[Description("A String parameter is posted VERBATIM. Re-encoding a serialized ESQ filter yields an empty selection instead of an error, so the raw text must survive untouched.")]
	public void TryRun_Should_Pass_String_Parameter_Verbatim() {
		// Arrange
		const string filter =
			"""{"filterType":6,"items":{"f":{"comparisonType":3,"leftExpression":{"columnPath":"Id"}}}}""";
		Harness harness = BuildHarness(MigratorSignature());
		RunProcessOptions options = new() {
			ProcessName = ProcessCode,
			Parameters = new Dictionary<string, JsonElement> {
				["SysDashboardsSelectionStateFilter"] = JsonSerializer.SerializeToElement(filter)
			}
		};

		// Act
		harness.Command.TryRun(options, out RunProcessResponse response);

		// Assert
		response.Error.Should().BeNull(because: "a well-formed call must reach the platform");
		harness.PostedBodies.Should().HaveCount(1, because: "exactly one launch was requested");
		ProcessStartArgs posted = JsonSerializer.Deserialize<ProcessStartArgs>(harness.PostedBodies[0]);
		posted.Values.Should().ContainSingle().Which.Value.Should().Be(filter,
			because: "the structured text must arrive byte-for-byte as supplied — a second round of JSON "
				+ "encoding turns the filter into an empty selection that the platform reports as success");
	}

	[Test]
	[Category("Unit")]
	[Description("A display caption is refused, naming the code it resolved to. A caption is not unique, and this tool starts a process rather than reading one, so an ambiguous key could launch the wrong one.")]
	public void TryRun_Should_Refuse_A_Caption_And_Name_The_Code_It_Resolved_To() {
		// Arrange
		Harness harness = BuildHarness(MigratorSignature());
		RunProcessOptions options = new() { ProcessName = "Migrate dashboards process" };

		// Act
		bool launched = harness.Command.TryRun(options, out RunProcessResponse response);

		// Assert
		launched.Should().BeFalse(because: "a caption is not an accepted identifier for a launch");
		response.Error.Should().Contain(ProcessCode,
			because: "naming the code it resolved to turns the refusal into a one-edit fix");
		harness.PostedBodies.Should().BeEmpty(
			because: "the refusal must happen before the launch, not after starting the wrong process");
	}

	[Test]
	[Category("Unit")]
	[Description("The process code is posted as the schema name, which is what the platform launches by.")]
	public void TryRun_Should_Post_The_Process_Code_As_The_Schema_Name() {
		// Arrange
		Harness harness = BuildHarness(MigratorSignature());

		// Act
		harness.Command.TryRun(new RunProcessOptions { ProcessName = ProcessCode }, out _);

		// Assert
		ProcessStartArgs posted = JsonSerializer.Deserialize<ProcessStartArgs>(harness.PostedBodies[0]);
		posted.SchemaName.Should().Be(ProcessCode,
			because: "the platform launches by schema name");
	}

	[Test]
	[Category("Unit")]
	[Description("The launch never auto-retries: a retry can duplicate work, because idempotency is a property of the specific process and not of the transport.")]
	public void TryRun_Should_Never_Retry_The_Launch() {
		// Arrange
		Harness harness = BuildHarness(MigratorSignature());
		RunProcessOptions options = new() { ProcessName = ProcessCode };

		// Act
		harness.Command.TryRun(options, out _);

		// Assert
		harness.PostedAttempts.Should().AllBeEquivalentTo(1,
			because: "a second attempt would launch the process twice whenever the first one actually ran");
	}

	[Test]
	[Category("Unit")]
	[Description("A null parameter value means 'leave unset' and is omitted from the payload. Sending an empty string instead would assign a real value — Guid.Empty for a lookup — which is a different thing.")]
	public void TryRun_Should_Omit_A_Null_Parameter_Rather_Than_Send_An_Empty_Value() {
		// Arrange
		Harness harness = BuildHarness(MigratorSignature());
		RunProcessOptions options = new() {
			ProcessName = ProcessCode,
			Parameters = Values("""{"SysModulesSelectedId":null,"SysDashboardsSelectionStateFilter":"keep"}""")
		};

		// Act
		harness.Command.TryRun(options, out RunProcessResponse response);

		// Assert
		response.Error.Should().BeNull(because: "a null value is not an error, it is an absent value");
		ProcessStartArgs posted = JsonSerializer.Deserialize<ProcessStartArgs>(harness.PostedBodies[0]);
		posted.Values.Should().ContainSingle(
			because: "only the parameter that carried a value may be posted — the platform expresses 'unset' "
				+ "by the entry being absent, so an empty string would assign Guid.Empty instead")
			.Which.Name.Should().Be("SysDashboardsSelectionStateFilter");
	}

	[Test]
	[Category("Unit")]
	[Description("A timeout larger than int.MaxValue milliseconds is clamped, because the seconds-to-milliseconds multiplication would otherwise wrap to a NEGATIVE timeout and turn an absurdly large bound into a near-instant one.")]
	public void TryRun_Should_Clamp_A_Timeout_That_Would_Overflow_Milliseconds() {
		// Arrange
		Harness harness = BuildHarness(MigratorSignature());
		RunProcessOptions options = new() { ProcessName = ProcessCode, TimeoutSeconds = int.MaxValue };

		// Act
		harness.Command.TryRun(options, out _);

		// Assert
		harness.PostedTimeouts.Should().AllSatisfy(timeout => timeout.Should().BePositive(
			because: "an unclamped int.MaxValue seconds becomes a negative millisecond value, which would "
				+ "cut the request instead of extending it"));
		harness.PostedTimeouts.Should().AllBeEquivalentTo(int.MaxValue,
			because: "the clamp saturates at the largest timeout the client can express");
	}

	[Test]
	[Category("Unit")]
	[Description("With no timeout supplied the request is unbounded, which is what a long synchronous process needs; a supplied timeout is converted from seconds to milliseconds.")]
	public void TryRun_Should_Map_Timeout_Seconds_Onto_The_Request() {
		// Arrange
		Harness unbounded = BuildHarness(MigratorSignature());
		Harness bounded = BuildHarness(MigratorSignature());

		// Act
		unbounded.Command.TryRun(new RunProcessOptions { ProcessName = ProcessCode }, out _);
		bounded.Command.TryRun(new RunProcessOptions { ProcessName = ProcessCode, TimeoutSeconds = 90 }, out _);

		// Assert
		unbounded.PostedTimeouts.Should().AllBeEquivalentTo(Timeout.Infinite,
			because: "a synchronous process runs for as long as it runs; clio must not cut it short by default");
		bounded.PostedTimeouts.Should().AllBeEquivalentTo(90_000,
			because: "the argument is expressed in seconds and the client takes milliseconds");
	}

	[Test]
	[Category("Unit")]
	[Description("TryRun's return value tracks the run OUTCOME, not merely that a request was sent: a refusal and a failed run must not be reported as a launch, because the value feeds Execute's exit code.")]
	public void TryRun_Should_Return_False_When_The_Platform_Refused_Or_The_Run_Failed() {
		// Arrange
		Harness notStarted = BuildHarness(MigratorSignature(),
			"""
			{"processId":"00000000-0000-0000-0000-000000000000","processStatus":0,"success":false,
			 "errorInfo":{"errorCode":"ProcessCannotBeManuallyStartedException","message":"no manual start"}}
			""");
		Harness failed = BuildHarness(MigratorSignature(),
			"""{"processId":"0f5e3a2a-2c8f-4f1e-9d0b-6d4b2f1a7c31","processStatus":3,"success":true}""");
		Harness completed = BuildHarness(MigratorSignature());

		// Act
		bool notStartedResult = notStarted.Command.TryRun(new RunProcessOptions { ProcessName = ProcessCode }, out _);
		bool failedResult = failed.Command.TryRun(new RunProcessOptions { ProcessName = ProcessCode }, out _);
		bool completedResult = completed.Command.TryRun(new RunProcessOptions { ProcessName = ProcessCode }, out _);

		// Assert
		notStartedResult.Should().BeFalse(because: "nothing was started, so exiting 0 would misreport the run");
		failedResult.Should().BeFalse(
			because: "the run finished with the error status, which must not exit 0 just because the platform "
				+ "answered success=true");
		completedResult.Should().BeTrue(because: "a terminal successful run is the one case that exits 0");
	}

	#endregion

	#region Response projection

	[Test]
	[Category("Unit")]
	[Description("A terminal successful run reports mode 'completed' with the real process id and status.")]
	public void Project_Should_Report_Completed_For_A_Terminal_Successful_Run() {
		// Arrange
		ProcessStartResponse platform = PlatformResponse(
			"""{"processId":"0f5e3a2a-2c8f-4f1e-9d0b-6d4b2f1a7c31","processStatus":2,"success":true}""");

		// Act
		RunProcessResponse response = RunProcessCommand.Project(platform, ProcessCode);

		// Assert
		response.Status.Should().Be("completed", because: "platform code 2 is ProcessStatus.Done");
		response.Error.Should().BeNull(because: "the run finished without an error status");
		response.ProcessId.Should().Be("0f5e3a2a-2c8f-4f1e-9d0b-6d4b2f1a7c31",
			because: "the caller needs the instance id, which is also the run's SysProcessLog primary key");
	}

	[Test]
	[Category("Unit")]
	[Description("A failed run is reported as a failure even when the platform itself answered success=true, which it does whenever its Feature-SetErrorInfoIfProcessHasFailedExecution flag is off.")]
	public void Project_Should_Fail_On_Error_Status_Even_When_The_Platform_Reported_Success() {
		// Arrange
		ProcessStartResponse platform = PlatformResponse(
			"""{"processId":"0f5e3a2a-2c8f-4f1e-9d0b-6d4b2f1a7c31","processStatus":3,"success":true}""");

		// Act
		RunProcessResponse response = RunProcessCommand.Project(platform, ProcessCode);

		// Assert
		response.Status.Should().Be("error", because: "platform code 3 is ProcessStatus.Error");
		response.Error.Should().NotBeNullOrWhiteSpace(
			because: "trusting the platform's own success flag here is exactly the trap this projection exists "
				+ "to close — a failed run reports success=true whenever that platform feature flag is off, "
				+ "so the error status alone has to raise the failure");
	}

	[Test]
	[Category("Unit")]
	[Description("The platform's errorInfo message is surfaced as text. It arrives as a JsonElement on an object-typed DTO field, which a reflection-based serializer renders as {\"ValueKind\":...} and loses entirely.")]
	public void Project_Should_Surface_The_Platform_Error_Message_Not_The_JsonElement_Shape() {
		// Arrange
		ProcessStartResponse platform = PlatformResponse(
			"""
			{"processId":"0f5e3a2a-2c8f-4f1e-9d0b-6d4b2f1a7c31","processStatus":3,"success":false,
			 "errorInfo":{"errorCode":"SomeFailure","message":"Process blew up"}}
			""");

		// Act
		RunProcessResponse response = RunProcessCommand.Project(platform, ProcessCode);

		// Assert
		response.Error.Should().Contain("Process blew up",
			because: "the platform's own message is the only actionable detail the caller gets");
		response.Error.Should().Contain("SomeFailure", because: "the error code aids diagnosis");
		response.Error.Should().NotContain("ValueKind",
			because: "rendering the JsonElement wrapper instead of its members loses the message completely");
	}

	[Test]
	[Category("Unit")]
	[Description("An empty process id with the Inactive status AND success=false is a startup refusal, not a background launch: the platform verifies a manual start event before anything runs.")]
	public void Project_Should_Report_Refused_When_The_Platform_Declined_To_Start_The_Process() {
		// Arrange
		ProcessStartResponse platform = PlatformResponse(
			"""
			{"processId":"00000000-0000-0000-0000-000000000000","processStatus":0,"success":false,
			 "errorInfo":{"errorCode":"ProcessCannotBeManuallyStartedException",
			              "message":"You cannot run this process manually because it only contains automatic start events"}}
			""");

		// Act
		RunProcessResponse response = RunProcessCommand.Project(platform, ProcessCode);

		// Assert
		response.Status.Should().Be("not-started",
			because: "reading only the empty id and the zero status would report this refusal as a successful "
				+ "background launch — success/errorInfo are the only things that tell the two apart");
		response.Error.Should().NotBeNullOrWhiteSpace(because: "nothing was started, and the caller must learn why");
		response.ProcessId.Should().BeNull(because: "there is no instance to point at");
		response.Error.Should().Contain("automatic",
			because: "the caller must learn the process has no manual entry point at all");
	}

	[Test]
	[Category("Unit")]
	[Description("An empty process id with the Inactive status and success=true is the background fire-and-forget branch: the platform queues the process and returns an empty descriptor without waiting.")]
	public void Project_Should_Report_QueuedBackground_When_The_Platform_Returned_No_Handle() {
		// Arrange
		ProcessStartResponse platform = PlatformResponse(
			"""{"processId":"00000000-0000-0000-0000-000000000000","processStatus":0,"success":true}""");

		// Act
		RunProcessResponse response = RunProcessCommand.Project(platform, ProcessCode);

		// Assert
		response.Status.Should().Be("queued-background",
			because: "the launch succeeded but no verdict exists, and the caller must not read it as completion");
		response.Error.Should().BeNull(
			because: "for a fire-and-forget process the queueing IS the outcome, so this is not a failure");
		response.ProcessId.Should().BeNull(because: "the platform returned an empty descriptor");
		response.Status.Should().NotBe("inactive",
			because: "the platform's zero status here is the empty descriptor's default, not an observed run "
				+ "state, so reporting it as the Inactive status would invent a verdict");
		response.Warnings.Should().ContainSingle().Which.Should().Contain("result-parameters",
			because: "requesting result parameters is the only way to force such a process to run "
				+ "synchronously and produce a verdict, so the note must say so");
	}

	[Test]
	[Category("Unit")]
	[Description("errorInfo members are read individually rather than re-serialized, and a non-object value yields no pair.")]
	public void ReadErrorInfo_Should_Read_Members_And_Tolerate_A_Missing_Payload() {
		// Arrange
		JsonElement payload = JsonSerializer.Deserialize<JsonElement>(
			"""{"errorCode":"Boom","message":"Bad thing"}""");
		JsonElement notAnObject = JsonSerializer.Deserialize<JsonElement>("null");

		// Act
		(string code, string message) = RunProcessCommand.ReadErrorInfo(payload);
		(string missingCode, string missingMessage) = RunProcessCommand.ReadErrorInfo(notAnObject);

		// Assert
		code.Should().Be("Boom", because: "the error code is read from its own member");
		message.Should().Be("Bad thing", because: "the message is read from its own member");
		missingCode.Should().BeNull(because: "a null payload carries no code");
		missingMessage.Should().BeNull(because: "a null payload carries no message");
	}

	#endregion

	#region Tool surface

	[Test]
	[Category("Unit")]
	[Description("The tool resolves the command for the environment named in the call rather than a startup-time default.")]
	public async Task RunProcess_Should_Resolve_The_Command_For_The_Requested_Environment() {
		// Arrange
		Harness harness = BuildHarness(MigratorSignature());
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<RunProcessCommand>(Arg.Any<RunProcessOptions>()).Returns(harness.Command);
		commandResolver.GetTenantKey(Arg.Any<EnvironmentOptions>()).Returns("tenant");
		RunProcessTool tool = new(ConsoleLogger.Instance, commandResolver);

		// Act
		RunProcessResponse response = await tool.RunProcess(
			new RunProcessArgs { ProcessName = ProcessCode, EnvironmentName = "dev" });

		// Assert
		response.Error.Should().BeNull(because: "the resolved command reached the platform stub");
		commandResolver.Received(1).Resolve<RunProcessCommand>(
			Arg.Is<RunProcessOptions>(o => o.Environment == "dev"));
	}

	[Test]
	[Category("Unit")]
	[Description("A run that outlives the MCP response deadline is answered with status 'still-running' and no process id: the platform exposes no handle for an in-flight synchronous run.")]
	public async Task RunProcess_Should_Report_AcceptedStillRunning_When_The_Response_Deadline_Is_Reached() {
		// Arrange
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.GetTenantKey(Arg.Any<EnvironmentOptions>()).Returns("tenant");
		commandResolver.Resolve<RunProcessCommand>(Arg.Any<RunProcessOptions>())
			.Returns(_ => {
				Thread.Sleep(TimeSpan.FromSeconds(5));
				throw new InvalidOperationException("the deadline must win this race");
			});
		RunProcessTool tool = new(ConsoleLogger.Instance, commandResolver) {
			ResponseDeadlineOverride = TimeSpan.FromMilliseconds(50)
		};

		// Act
		RunProcessResponse response = await tool.RunProcess(
			new RunProcessArgs { ProcessName = ProcessCode, EnvironmentName = "dev" });

		// Assert
		response.Status.Should().Be("still-running",
			because: "answering before Creatio does is not a failure and not a success");
		response.Error.Should().BeNull(because: "the launch itself was accepted");
		response.ProcessId.Should().BeNull(
			because: "the id only exists in the RunProcess response and the log row is written when the run "
				+ "ends, so there is genuinely no handle to report — reporting a guessed one would be worse");
		response.Warnings.Should().ContainSingle().Which.Should().Contain("Do NOT re-run",
			because: "a second launch duplicates the work, which is the one thing the caller must not do");
	}

	[Test]
	[Category("Unit")]
	[Description("run-process must NOT carry [FeatureToggle], unlike the rest of the process-designer suite: it calls a built-in endpoint, and gating it would break every consumer on a stand without the toggle.")]
	public void RunProcessTool_Should_Not_Be_FeatureGated() {
		// Arrange & Act
		object[] toggles = typeof(RunProcessTool)
			.GetCustomAttributes(typeof(FeatureToggleAttribute), inherit: true);

		// Assert
		toggles.Should().BeEmpty(
			because: "ProcessEngineService.svc/RunProcess is present on every Creatio and never touches "
				+ "ProcessDesignService, so this tool follows get-process-signature rather than the gated "
				+ "designer tools — the same decision, recorded for the same reason");
	}

	[Test]
	[Category("Unit")]
	[Description("run-process options must NOT declare the process-builder package requirement: the endpoint is built in, and demanding the package would refuse the call on stands that do not need it.")]
	public void RunProcessOptions_Should_Not_Declare_The_ProcessBuilder_Requirement() {
		// Arrange & Act
		bool declared = RequiresPackageAttribute.IsDefinedOn(typeof(RunProcessOptions));

		// Assert
		declared.Should().BeFalse(
			because: "the tool reaches a built-in platform endpoint, so a package gate would only make the "
				+ "capability unavailable where it would have worked");
	}

	[Test]
	[Category("Unit")]
	[Description("The tool is declared destructive and non-idempotent, so a host confirms before it runs and never treats a repeat as free.")]
	public void RunProcessTool_Should_Be_Declared_Destructive_And_Non_Idempotent() {
		// Arrange & Act
		ModelContextProtocol.Server.McpServerToolAttribute attribute = (ModelContextProtocol.Server.McpServerToolAttribute)
			typeof(RunProcessTool).GetMethod(nameof(RunProcessTool.RunProcess))!
				.GetCustomAttributes(typeof(ModelContextProtocol.Server.McpServerToolAttribute), inherit: false)[0];

		// Assert
		attribute.Name.Should().Be("run-process", because: "the wire name is part of the shipped contract");
		attribute.Destructive.Should().BeTrue(
			because: "launching a process changes data, so the host must be able to confirm first");
		attribute.Idempotent.Should().BeFalse(
			because: "idempotency is a property of the specific process, never of this transport");
		attribute.ReadOnly.Should().BeFalse(because: "the call writes");
	}

	#endregion
}
