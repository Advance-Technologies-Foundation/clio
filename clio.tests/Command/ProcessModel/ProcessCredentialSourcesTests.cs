using System;
using System.Collections.Generic;
using System.Text.Json;
using Clio.Command.ProcessModel;
using Clio.Common;
using ErrorOr;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using ProcessModelType = Clio.Command.ProcessModel.ProcessModel;

namespace Clio.Tests.Command.ProcessModel;

/// <summary>
/// Unit tests for <see cref="ProcessCredentialSourceDetector"/>: whether a process can put a credential into its
/// result parameters - a Call web service element, or a SecureText system setting it reads.
/// </summary>
[TestFixture]
[Property("Module", "ProcessModel")]
[Category("Unit")]
public sealed class ProcessCredentialSourcesTests {

	private static readonly Guid WebServiceManager = new("652B598D-CDD5-49D5-A86D-AAFAC88213F3");
	private static readonly Guid SubProcessManager = new("49EAFDBB-A89E-4BDF-A29D-7F17B1670A45");
	private static readonly Guid FormulaTaskManager = new("D334D28F-B11A-477E-9FF0-0A95FA73D53B");
	private static readonly Guid EventSubProcessManager = new("0824AF03-1340-47A3-8787-EF542F566992");

	private IProcessModelGenerator _generator;
	private IProcessVersionLibReader _versions;
	private IApplicationClient _client;
	private List<string> _queries;
	private string _settingsAnswer;

	[SetUp]
	public void SetUp() {
		_generator = Substitute.For<IProcessModelGenerator>();
		_versions = Substitute.For<IProcessVersionLibReader>();
		_versions.Read(Arg.Any<string>(), Arg.Any<TimeSpan>())
			.Returns(call => new ProcessVersionFacts { ActiveVersionSchemaUId = call.ArgAt<string>(0) });
		_client = Substitute.For<IApplicationClient>();
		_queries = [];
		_settingsAnswer = """{"success":true,"rows":[]}""";
		_client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(call => {
				_queries.Add(call.ArgAt<string>(1));
				return _settingsAnswer;
			});
	}

	private ProcessCredentialSourceDetector Detector() {
		IServiceUrlBuilder urls = Substitute.For<IServiceUrlBuilder>();
		urls.Build(Arg.Any<ServiceUrlBuilder.KnownRoute>()).Returns("DataService/json/SyncReply/SelectQuery");
		return new ProcessCredentialSourceDetector(_generator, _versions, _client, urls);
	}

	private static DateTimeOffset Later => DateTimeOffset.UtcNow.AddSeconds(30);

	private static ProcessModelType Model(List<FlowElement> elements, params ProcessParameter[] parameters) =>
		new(Guid.NewGuid(), "UsrProbe") { Parameters = [.. parameters], FlowElements = elements };

	private static MetaDataSchema Schema(List<FlowElement> elements, params ProcessParameter[] parameters) =>
		new() { FlowElements = elements, Parameters = [.. parameters] };

	private static FlowElement Element(string name, Guid manager) =>
		new() { Name = name, UId = Guid.NewGuid(), ManagerItemUId = manager, Parameters = [] };

	private static ProcessParameter Parameter(string name, int source, string value) =>
		new() { Name = name, UId = Guid.NewGuid(), SourceValue = new SourceValue { Source = source, Value = value } };

	private void SettingsAre(params (string Code, string Type)[] rows) =>
		_settingsAnswer = JsonSerializer.Serialize(new {
			success = true,
			rows = Array.ConvertAll(rows, row => new Dictionary<string, string> {
				["Code"] = row.Code, ["ValueTypeName"] = row.Type
			})
		});

	[Test]
	[Description("A process with a Call web service element is a source, and no setting is queried.")]
	public void FindCredentialSource_Should_Report_A_WebService_Element() {
		// Arrange
		ProcessModelType model = Model([Element("CallEcho", WebServiceManager)]);

		// Act
		string source = Detector().FindCredentialSource(model, Later);

		// Assert
		source.Should().Contain("calls a web service (element 'CallEcho')",
			because: "the response may echo the credentials the call sent");
		_queries.Should().BeEmpty(because: "the web service already decides it");
	}

	[Test]
	[Description("A Call web service element nested in an event sub-process is found.")]
	public void FindCredentialSource_Should_See_A_WebService_Element_In_An_Event_SubProcess() {
		// Arrange
		FlowElement eventSubProcess = Element("EventSub1", EventSubProcessManager);
		eventSubProcess.FlowElements = [Element("CallEcho", WebServiceManager)];

		// Act
		string source = Detector().FindCredentialSource(Model([eventSubProcess]), Later);

		// Assert
		source.Should().Contain("calls a web service", because: "an event sub-process keeps its own elements");
	}

	[TestCase(3, "[#SysSettings.UsrApiKey#]")]
	[TestCase(3, "[#SysSettings.UsrApiKey<Text>#] + \"-suffix\"")]
	[TestCase(5, "UsrApiKey")]
	[Description("A parameter that reads a SecureText setting - through a macro, or with the setting as its value source - is a source.")]
	public void FindCredentialSource_Should_Report_A_SecureText_Setting_A_Parameter_Reads(int source, string value) {
		// Arrange
		SettingsAre(("UsrApiKey", "SecureText"));
		ProcessModelType model = Model([], Parameter("Leak", source, value));

		// Act
		string found = Detector().FindCredentialSource(model, Later);

		// Assert
		found.Should().Be("reads the SecureText system setting 'UsrApiKey'",
			because: "the platform decrypts the setting into the parameter, and run-process would return it");
	}

	[Test]
	[Description("A SecureText setting a Formula element's body reads is a source.")]
	public void FindCredentialSource_Should_Report_A_SecureText_Setting_In_A_Formula_Body() {
		// Arrange
		SettingsAre(("UsrApiKey", "SecureText"));
		FlowElement formula = Element("CopyKey", FormulaTaskManager);
		formula.Body = JsonSerializer.SerializeToElement("[#SysSettings.UsrApiKey#]");

		// Act
		string found = Detector().FindCredentialSource(Model([formula]), Later);

		// Assert
		found.Should().Contain("UsrApiKey", because: "a Formula element keeps its formula on the element, in body");
	}

	[Test]
	[Description("A process that reads only ordinary settings is not a source, and it costs one query.")]
	public void FindCredentialSource_Should_Ignore_A_Text_Setting() {
		// Arrange
		SettingsAre(("UsrCurrency", "Text"));
		ProcessModelType model = Model([], Parameter("Currency", 3, "[#SysSettings.UsrCurrency#]"));

		// Act
		string found = Detector().FindCredentialSource(model, Later);

		// Assert
		found.Should().BeNull(because: "a Text setting is no credential");
		_queries.Should().ContainSingle(because: "every setting the process reads is typed in one query");
	}

	[Test]
	[Description("A setting whose type cannot be read counts as SecureText.")]
	public void FindCredentialSource_Should_Fail_Closed_On_An_Unknown_Setting_Type() {
		// Arrange
		ProcessModelType model = Model([], Parameter("Value", 3, "[#SysSettings.UsrMissing#]"));

		// Act
		string found = Detector().FindCredentialSource(model, Later);

		// Assert
		found.Should().Contain("UsrMissing", because: "a setting clio cannot type is not proven harmless");
	}

	[Test]
	[Description("A sub-process element is checked by its called process's ACTIVE version, not the version the element stores.")]
	public void FindCredentialSource_Should_Read_The_Active_Version_Of_A_Called_Process() {
		// Arrange
		Guid storedUId = Guid.NewGuid();
		Guid activeUId = Guid.NewGuid();
		_versions.Read(storedUId.ToString("D"), Arg.Any<TimeSpan>())
			.Returns(new ProcessVersionFacts { ActiveVersionSchemaUId = activeUId.ToString() });
		_generator.ReadSchema(activeUId, Arg.Any<int>())
			.Returns(Schema([Element("CallEcho", WebServiceManager)]));
		FlowElement sub = Element("Sub1", SubProcessManager);
		sub.SchemaUId = storedUId;

		// Act
		string found = Detector().FindCredentialSource(Model([sub]), Later);

		// Assert
		found.Should().Contain("in a process it calls", because: "the called process's response reaches the caller's outputs");
		_generator.DidNotReceive().ReadSchema(storedUId, Arg.Any<int>());
	}

	[Test]
	[Description("A called process with no source leaves the caller clean.")]
	public void FindCredentialSource_Should_Accept_A_Clean_Called_Process() {
		// Arrange
		Guid callee = Guid.NewGuid();
		_generator.ReadSchema(callee, Arg.Any<int>()).Returns(Schema([]));
		FlowElement sub = Element("Sub1", SubProcessManager);
		sub.SchemaUId = callee;

		// Act
		string found = Detector().FindCredentialSource(Model([sub]), Later);

		// Assert
		found.Should().BeNull(because: "nothing the run reaches can return a credential");
	}

	[Test]
	[Description("A called process that cannot be read, or not before the stop time, and a sub-process element that names no process, are sources.")]
	public void FindCredentialSource_Should_Fail_Closed_On_What_It_Cannot_Read() {
		// Arrange
		Guid broken = Guid.NewGuid();
		_generator.ReadSchema(broken, Arg.Any<int>()).Returns(Error.Failure("read", "boom"));
		FlowElement toBroken = Element("Sub1", SubProcessManager);
		toBroken.SchemaUId = broken;
		FlowElement late = Element("Sub2", SubProcessManager);
		late.SchemaUId = Guid.NewGuid();
		FlowElement unnamed = Element("Sub3", SubProcessManager);

		// Act
		string unreadable = Detector().FindCredentialSource(Model([toBroken]), Later);
		string outOfTime = Detector().FindCredentialSource(Model([late]), DateTimeOffset.UtcNow.AddSeconds(-1));
		string noProcess = Detector().FindCredentialSource(Model([unnamed]), Later);

		// Assert
		unreadable.Should().Contain("could not check", because: "a process clio cannot read is not proven clean");
		outOfTime.Should().Contain("no time left", because: "the check never delays the launch past its stop time");
		noProcess.Should().Contain("names no process", because: "an unconfigured element calls something unknown");
	}

	[Test]
	[Description("Two processes calling each other are read once each and do not loop.")]
	public void FindCredentialSource_Should_Stop_On_Mutually_Calling_Processes() {
		// Arrange
		Guid a = Guid.NewGuid();
		Guid b = Guid.NewGuid();
		FlowElement toA = Element("CallA", SubProcessManager);
		toA.SchemaUId = a;
		FlowElement toB = Element("CallB", SubProcessManager);
		toB.SchemaUId = b;
		_generator.ReadSchema(a, Arg.Any<int>()).Returns(Schema([toB]));
		_generator.ReadSchema(b, Arg.Any<int>()).Returns(Schema([toA]));

		// Act
		string found = Detector().FindCredentialSource(Model([toA]), Later);

		// Assert
		found.Should().BeNull(because: "neither process has a source");
		_generator.Received(1).ReadSchema(a, Arg.Any<int>());
		_generator.Received(1).ReadSchema(b, Arg.Any<int>());
	}

	[Test]
	[Description("A process with no web service, sub-process or setting is clean, and nothing is read.")]
	public void FindCredentialSource_Should_Read_Nothing_For_A_Plain_Process() {
		// Arrange
		ProcessModelType model = Model([Element("Formula1", FormulaTaskManager)], Parameter("Plain", 1, "constant"));

		// Act
		string found = Detector().FindCredentialSource(model, Later);

		// Assert
		found.Should().BeNull(because: "nothing in the process can return a credential");
		_queries.Should().BeEmpty(because: "a plain run must not pay for the check");
		_versions.DidNotReceive().Read(Arg.Any<string>(), Arg.Any<TimeSpan>());
	}

	[TestCase(3, "[#SysSettings.UsrApiKey#]")]
	[TestCase(5, "UsrApiKey")]
	[Description("A called process whose own parameter reads a SecureText setting is a source: its output reaches the caller.")]
	public void FindCredentialSource_Should_Report_A_Setting_A_Called_Process_Parameter_Reads(int source, string value) {
		// Arrange
		SettingsAre(("UsrApiKey", "SecureText"));
		Guid callee = Guid.NewGuid();
		_generator.ReadSchema(callee, Arg.Any<int>()).Returns(Schema([], Parameter("ApiKey", source, value)));
		FlowElement sub = Element("GetKey", SubProcessManager);
		sub.SchemaUId = callee;

		// Act
		string found = Detector().FindCredentialSource(Model([sub]), Later);

		// Assert
		found.Should().Be("reads the SecureText system setting 'UsrApiKey'",
			because: "a helper process that hands a key to its caller is the commonest way to read one");
	}

	[Test]
	[Description("When the active version of a called process is not established, every listed version is read; an unknown family is a source.")]
	public void FindCredentialSource_Should_Read_The_Whole_Family_Without_An_Active_Version() {
		// Arrange
		Guid stored = Guid.NewGuid();
		Guid other = Guid.NewGuid();
		Guid unknown = Guid.NewGuid();
		_versions.Read(stored.ToString("D"), Arg.Any<TimeSpan>()).Returns(new ProcessVersionFacts {
			Versions = [new ProcessVersionFamilyMember { SchemaUId = other.ToString() }]
		});
		_versions.Read(unknown.ToString("D"), Arg.Any<TimeSpan>()).Returns(new ProcessVersionFacts { Warning = "no row" });
		_generator.ReadSchema(stored, Arg.Any<int>()).Returns(Schema([]));
		_generator.ReadSchema(other, Arg.Any<int>()).Returns(Schema([Element("CallEcho", WebServiceManager)]));
		FlowElement toFamily = Element("Sub1", SubProcessManager);
		toFamily.SchemaUId = stored;
		FlowElement toUnknown = Element("Sub2", SubProcessManager);
		toUnknown.SchemaUId = unknown;

		// Act
		string family = Detector().FindCredentialSource(Model([toFamily]), Later);
		string notEstablished = Detector().FindCredentialSource(Model([toUnknown]), Later);

		// Assert
		family.Should().Contain("calls a web service", because: "any listed version may be the one that runs");
		notEstablished.Should().Contain("could not establish", because: "an unknown family is not proven clean");
	}

	[Test]
	[Description("A formula that reads a setting in C# rather than through the macro is a source.")]
	public void FindCredentialSource_Should_Report_A_Setting_Read_In_Formula_Code() {
		// Arrange
		FlowElement formula = Element("CopyKey", FormulaTaskManager);
		formula.Body = JsonSerializer.SerializeToElement("SysSettings.GetValue(UserConnection, \"UsrApiKey\")");

		// Act
		string found = Detector().FindCredentialSource(Model([formula]), Later);

		// Assert
		found.Should().Contain("in a formula", because: "clio cannot tell which setting the code reads, nor its type");
		_queries.Should().BeEmpty(because: "a setting read in code cannot be typed, so nothing is queried");
	}

	[Test]
	[Description("A setting code is matched to its row ignoring case, as the database does.")]
	public void FindCredentialSource_Should_Match_Setting_Codes_Ignoring_Case() {
		// Arrange
		SettingsAre(("UsrCurrency", "Text"));
		ProcessModelType model = Model([], Parameter("Currency", 3, "[#SysSettings.usrcurrency#]"));

		// Act
		string found = Detector().FindCredentialSource(model, Later);

		// Assert
		found.Should().BeNull(because: "the row the database returns for a differently cased code is the same setting");
	}
}
