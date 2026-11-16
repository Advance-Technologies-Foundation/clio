using System;
using System.Collections.Generic;
using System.Text.Json;
using Clio.Command.ProcessModel;
using ErrorOr;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.ProcessModel;

/// <summary>
/// Unit tests for <see cref="ProcessResultSources"/> and <see cref="ProcessResultSourceClassifier"/>: which result
/// parameters run-process withholds because their value may come from what a Call web service element received.
/// </summary>
[TestFixture]
[Property("Module", "ProcessModel")]
[Category("Unit")]
public sealed class ProcessResultSourcesTests {

	private static readonly Guid WebServiceManager = new("652B598D-CDD5-49D5-A86D-AAFAC88213F3");
	private static readonly Guid SubProcessManager = new("49EAFDBB-A89E-4BDF-A29D-7F17B1670A45");
	private static readonly Guid ScriptTaskManager = new("0E490DDA-E140-4441-B600-6F5C64D024DF");
	private static readonly Guid FormulaTaskManager = new("D334D28F-B11A-477E-9FF0-0A95FA73D53B");
	private static readonly Guid UserTaskManager = new("1418E61A-82C3-403E-8221-01088F52C125");

	private static readonly Guid CallUId = Guid.NewGuid();
	private static readonly Guid ResponseUId = Guid.NewGuid();
	private static readonly Guid KeyOutUId = Guid.NewGuid();
	private static readonly Guid SuccessUId = Guid.NewGuid();
	private static readonly Guid StatusCodeUId = Guid.NewGuid();

	private static string ElementPath(Guid element, Guid parameter) =>
		$"[#[IsOwnerSchema:false].[IsSchema:false].[Element:{{{element}}}].[Parameter:{{{parameter}}}]#]";

	private static string ParameterPath(Guid parameter) =>
		$"[#[IsOwnerSchema:false].[IsSchema:false].[Parameter:{{{parameter}}}]#]";

	private static ProcessParameter Parameter(string name, string value = null, Guid? uid = null,
		List<ProcessParameter> items = null) =>
		new() {
			Name = name,
			UId = uid ?? Guid.NewGuid(),
			Direction = ProcessParameterDirection.Output,
			SourceValue = value is null ? new SourceValue() : new SourceValue { Source = 3, Value = value },
			ItemProperties = items
		};

	private static FlowElementParameter ElementParameter(string name, Guid uid, string value = null) =>
		new() {
			Name = name,
			UId = uid.ToString(),
			SourceValue = value is null ? new SourceValue() : new SourceValue { Source = 3, Value = value }
		};

	private static FlowElement WebServiceCall() =>
		new() {
			Name = "CallEcho",
			UId = CallUId,
			ManagerItemUId = WebServiceManager,
			Parameters = [
				ElementParameter("Response", ResponseUId),
				ElementParameter("UsrKey_Out", KeyOutUId),
				ElementParameter("Success", SuccessUId),
				ElementParameter("StatusCode", StatusCodeUId)
			]
		};

	private static FlowElement Element(string name, Guid manager, params FlowElementParameter[] parameters) =>
		new() { Name = name, UId = Guid.NewGuid(), ManagerItemUId = manager, Parameters = [.. parameters] };

	private static IReadOnlyDictionary<string, string> Classify(List<ProcessParameter> parameters,
		List<FlowElement> elements, IReadOnlyList<string> requested, Func<Guid, CalleeVerdict> callee = null) =>
		ProcessResultSources.Classify(parameters, elements, requested,
			callee ?? (_ => throw new AssertionException("no sub-process should be read")));

	[Test]
	[Description("A result mapped from a Call web service element's Response or response parameter is withheld; Success and StatusCode are returned.")]
	public void Classify_Should_Withhold_WebService_Outputs_But_Not_Success_And_StatusCode() {
		// Arrange
		List<ProcessParameter> parameters = [
			Parameter("ResponseText", ElementPath(CallUId, ResponseUId)),
			Parameter("EchoKey", ElementPath(CallUId, KeyOutUId)),
			Parameter("Ok", ElementPath(CallUId, SuccessUId)),
			Parameter("Code", ElementPath(CallUId, StatusCodeUId)),
			Parameter("Note", null)
		];

		// Act
		IReadOnlyDictionary<string, string> withheld = Classify(parameters, [WebServiceCall()],
			["ResponseText", "EchoKey", "Ok", "Code", "Note"]);

		// Assert
		withheld.Keys.Should().BeEquivalentTo(["ResponseText", "EchoKey"],
			because: "the response body and a response parameter can carry an echoed credential, a flag and a status code cannot");
		withheld["ResponseText"].Should().Contain("'CallEcho'",
			because: "the reason names the element the value comes from, so the user knows where to look");
	}

	[Test]
	[Description("A value reaching the response through a formula or another process parameter is withheld too.")]
	public void Classify_Should_Follow_Formulas_And_Parameter_Chains() {
		// Arrange
		Guid firstUId = Guid.NewGuid();
		List<ProcessParameter> parameters = [
			Parameter("First", ElementPath(CallUId, ResponseUId), firstUId),
			Parameter("Second", ParameterPath(firstUId)),
			Parameter("Formula", $"{ElementPath(CallUId, KeyOutUId)} + \"suffix\"")
		];

		// Act
		IReadOnlyDictionary<string, string> withheld = Classify(parameters, [WebServiceCall()],
			["Second", "Formula"]);

		// Assert
		withheld.Keys.Should().BeEquivalentTo(["Second", "Formula"],
			because: "a parameter copied from the response, or built from it by a formula, still holds the echo");
	}

	[Test]
	[Description("A collection whose item is mapped from a web service output is withheld.")]
	public void Classify_Should_Withhold_A_Collection_With_An_Item_From_The_Response() {
		// Arrange
		List<ProcessParameter> parameters = [
			Parameter("Orders", items: [Parameter("Number", ElementPath(CallUId, KeyOutUId))])
		];

		// Act
		IReadOnlyDictionary<string, string> withheld = Classify(parameters, [WebServiceCall()], ["Orders"]);

		// Assert
		withheld.Should().ContainKey("Orders", because: "the collection's rows are read from the response");
	}

	[Test]
	[Description("A process with no web service element and no sub-process withholds nothing and reads nothing.")]
	public void Classify_Should_Withhold_Nothing_Without_A_WebService_Or_SubProcess() {
		// Arrange
		FlowElement script = Element("Script1", ScriptTaskManager);
		List<ProcessParameter> parameters = [Parameter("Out", ElementPath(script.UId, Guid.NewGuid()))];

		// Act
		IReadOnlyDictionary<string, string> withheld = Classify(parameters, [script], ["Out"]);

		// Assert
		withheld.Should().BeEmpty(because: "nothing in the process received a web service's answer");
	}

	[Test]
	[Description("A Script task in a process that calls a web service makes every requested result withheld.")]
	public void Classify_Should_Withhold_Everything_When_A_Script_Task_Shares_The_Process_With_A_WebService() {
		// Arrange
		List<ProcessParameter> parameters = [Parameter("Plain"), Parameter("Code", ElementPath(CallUId, StatusCodeUId))];

		// Act
		IReadOnlyDictionary<string, string> withheld = Classify(parameters,
			[WebServiceCall(), Element("Script1", ScriptTaskManager)], ["Plain", "Code"]);

		// Assert
		withheld.Keys.Should().BeEquivalentTo(["Plain", "Code"],
			because: "a Script task can write any parameter, so no stored source proves a value clean");
		withheld["Plain"].Should().Contain("Script task", because: "the reason says why a plain parameter is withheld");
	}

	[Test]
	[Description("The parameter a Formula element writes (its body and target as the schema stores them, on the element) is withheld when the body reads the response.")]
	public void Classify_Should_Withhold_A_Parameter_Written_By_A_Formula_That_Reads_The_Response() {
		// Arrange
		Guid targetUId = Guid.NewGuid();
		Guid readOnlyUId = Guid.NewGuid();
		FlowElement formula = Element("Formula1", FormulaTaskManager);
		formula.Body = JsonSerializer.SerializeToElement(
			$"{ElementPath(CallUId, ResponseUId)} + {ParameterPath(readOnlyUId)}");
		formula.ResultParameterMetaPath = JsonSerializer.SerializeToElement(targetUId.ToString());
		List<ProcessParameter> parameters = [
			Parameter("Target", uid: targetUId), Parameter("ReadOnly", uid: readOnlyUId), Parameter("Other")
		];

		// Act
		IReadOnlyDictionary<string, string> withheld = Classify(parameters, [WebServiceCall(), formula],
			["Target", "ReadOnly", "Other"]);

		// Assert
		withheld.Keys.Should().BeEquivalentTo(["Target"],
			because: "the formula writes the response into the parameter it targets, and only reads the other one");
		withheld["Target"].Should().Contain("Formula element 'Formula1'",
			because: "the reason names the element that wrote the value");
	}

	[Test]
	[Description("A Formula element whose target cannot be read counts as writing every parameter, referenced or not.")]
	public void Classify_Should_Fail_Closed_On_A_Formula_Without_A_Readable_Target() {
		// Arrange
		Guid readUId = Guid.NewGuid();
		FlowElement formula = Element("Formula1", FormulaTaskManager);
		formula.Body = JsonSerializer.SerializeToElement($"{ElementPath(CallUId, ResponseUId)} + {ParameterPath(readUId)}");
		List<ProcessParameter> parameters = [Parameter("Read", uid: readUId), Parameter("Unreferenced")];

		// Act
		IReadOnlyDictionary<string, string> withheld = Classify(parameters, [WebServiceCall(), formula],
			["Read", "Unreferenced"]);

		// Assert
		withheld.Keys.Should().BeEquivalentTo(["Read", "Unreferenced"],
			because: "without its target clio cannot tell which parameter the formula writes, so it may write any");
	}

	[Test]
	[Description("An output of an ordinary element whose input is fed from the response is withheld.")]
	public void Classify_Should_Withhold_An_Output_Of_An_Element_Fed_From_The_Response() {
		// Arrange
		Guid outputUId = Guid.NewGuid();
		FlowElement read = Element("ReadData1", UserTaskManager,
			ElementParameter("Filter", Guid.NewGuid(), ElementPath(CallUId, ResponseUId)),
			ElementParameter("ResultEntity", outputUId));
		List<ProcessParameter> parameters = [Parameter("Record", ElementPath(read.UId, outputUId))];

		// Act
		IReadOnlyDictionary<string, string> withheld = Classify(parameters, [WebServiceCall(), read], ["Record"]);

		// Assert
		withheld.Should().ContainKey("Record", because: "an element may pass on what one of its inputs received");
	}

	[Test]
	[Description("A reference to an element the schema does not list is withheld in a process that calls a web service.")]
	public void Classify_Should_Withhold_A_Reference_To_An_Unknown_Element() {
		// Arrange
		List<ProcessParameter> parameters = [Parameter("Out", ElementPath(Guid.NewGuid(), Guid.NewGuid()))];

		// Act
		IReadOnlyDictionary<string, string> withheld = Classify(parameters, [WebServiceCall()], ["Out"]);

		// Assert
		withheld.Should().ContainKey("Out", because: "a source clio cannot see is not proven clean");
	}

	[Test]
	[Description("A sub-process output is withheld when the called process may call a web service, and returned when it cannot.")]
	public void Classify_Should_Ask_About_The_Called_Process_Of_A_SubProcess() {
		// Arrange
		Guid calleeCalling = Guid.NewGuid();
		Guid calleeClean = Guid.NewGuid();
		FlowElement calling = Element("Sub1", SubProcessManager, ElementParameter("Result", Guid.NewGuid()));
		calling.SchemaUId = calleeCalling;
		FlowElement clean = Element("Sub2", SubProcessManager, ElementParameter("Result", Guid.NewGuid()));
		clean.SchemaUId = calleeClean;
		List<ProcessParameter> parameters = [
			Parameter("FromCalling", ElementPath(calling.UId, Guid.Parse(calling.Parameters[0].UId))),
			Parameter("FromClean", ElementPath(clean.UId, Guid.Parse(clean.Parameters[0].UId)))
		];
		List<Guid> asked = [];

		// Act
		IReadOnlyDictionary<string, string> withheld = Classify(parameters, [calling, clean],
			["FromCalling", "FromClean"], callee => {
				asked.Add(callee);
				return callee == calleeCalling ? CalleeVerdict.CallsWebService : CalleeVerdict.Clean;
			});

		// Assert
		withheld.Keys.Should().BeEquivalentTo(["FromCalling"],
			because: "only the called process that calls a web service can hand an echo back");
		asked.Should().BeEquivalentTo([calleeCalling, calleeClean],
			because: "each called process is read once, and only because a requested value reaches it");
	}

	[Test]
	[Description("A sub-process whose called process is clean still passes on an input fed from the response.")]
	public void Classify_Should_Withhold_A_SubProcess_Output_When_Its_Input_Is_The_Response() {
		// Arrange
		Guid outputUId = Guid.NewGuid();
		FlowElement sub = Element("Sub1", SubProcessManager,
			ElementParameter("Input", Guid.NewGuid(), ElementPath(CallUId, ResponseUId)),
			ElementParameter("Result", outputUId));
		sub.SchemaUId = Guid.NewGuid();
		List<ProcessParameter> parameters = [Parameter("Out", ElementPath(sub.UId, outputUId))];

		// Act
		IReadOnlyDictionary<string, string> withheld = Classify(parameters, [WebServiceCall(), sub], ["Out"],
			_ => CalleeVerdict.Clean);

		// Assert
		withheld.Should().ContainKey("Out", because: "the called process may return its input unchanged");
	}

	[Test]
	[Description("The classifier reads every version of a called family and counts a failed read as calling a web service.")]
	public void Classifier_Should_Read_The_Called_Family_And_Fail_Closed() {
		// Arrange
		Guid calleeUId = Guid.NewGuid();
		Guid activeUId = Guid.NewGuid();
		Guid brokenCallee = Guid.NewGuid();
		IProcessModelGenerator generator = Substitute.For<IProcessModelGenerator>();
		generator.ReadFlowElements(calleeUId, Arg.Any<int>()).Returns(new List<FlowElement>());
		generator.ReadFlowElements(activeUId, Arg.Any<int>()).Returns(new List<FlowElement> { WebServiceCall() });
		generator.ReadFlowElements(brokenCallee, Arg.Any<int>()).Returns(Error.Failure("read", "boom"));
		IProcessVersionLibReader versions = Substitute.For<IProcessVersionLibReader>();
		versions.Read(calleeUId.ToString("D"), Arg.Any<TimeSpan>()).Returns(new ProcessVersionFacts {
			ActiveVersionSchemaUId = activeUId.ToString(),
			Versions = [new ProcessVersionFamilyMember { SchemaUId = calleeUId.ToString() }]
		});
		versions.Read(brokenCallee.ToString("D"), Arg.Any<TimeSpan>()).Returns(new ProcessVersionFacts { Versions = [] });
		FlowElement sub = Element("Sub1", SubProcessManager, ElementParameter("Result", Guid.NewGuid()));
		sub.SchemaUId = calleeUId;
		FlowElement broken = Element("Sub2", SubProcessManager, ElementParameter("Result", Guid.NewGuid()));
		broken.SchemaUId = brokenCallee;
		Clio.Command.ProcessModel.ProcessModel model = new(Guid.NewGuid(), "UsrCaller") {
			Parameters = [
				Parameter("FromActive", ElementPath(sub.UId, Guid.Parse(sub.Parameters[0].UId))),
				Parameter("FromBroken", ElementPath(broken.UId, Guid.Parse(broken.Parameters[0].UId)))
			],
			FlowElements = [sub, broken]
		};
		ProcessResultSourceClassifier classifier = new(generator, versions);

		// Act
		IReadOnlyDictionary<string, string> withheld = classifier.FindWebServiceFed(model, ["FromActive", "FromBroken"],
			DateTimeOffset.UtcNow.AddSeconds(30));

		// Assert
		withheld.Keys.Should().BeEquivalentTo(["FromActive", "FromBroken"],
			because: "a sub-process runs the ACTIVE version of its family, and a schema clio could not read is not proven clean");
		withheld["FromBroken"].Should().Contain("could not check",
			because: "a process clio could not read is not reported as one that calls a web service");
		generator.DidNotReceive().ReadFlowElements(calleeUId, Arg.Any<int>());
	}

	[Test]
	[Description("No requested result parameter means nothing is classified and nothing is read.")]
	public void Classifier_Should_Do_Nothing_Without_Result_Parameters() {
		// Arrange
		IProcessModelGenerator generator = Substitute.For<IProcessModelGenerator>();
		IProcessVersionLibReader versions = Substitute.For<IProcessVersionLibReader>();
		ProcessResultSourceClassifier classifier = new(generator, versions);
		Clio.Command.ProcessModel.ProcessModel model = new(Guid.NewGuid(), "UsrCaller") { FlowElements = [WebServiceCall()] };

		// Act
		IReadOnlyDictionary<string, string> withheld = classifier.FindWebServiceFed(model, [], DateTimeOffset.UtcNow.AddSeconds(30));

		// Assert
		withheld.Should().BeEmpty(because: "a run that reads no result returns no value to withhold");
		versions.DidNotReceive().Read(Arg.Any<string>(), Arg.Any<TimeSpan>());
	}

	[TestCase("Accum", "Copy")]
	[TestCase("Copy", "Accum")]
	[Description("A loop between two Formula elements cannot hide the response: both parameters are withheld whichever is requested first.")]
	public void Classify_Should_Withhold_Every_Parameter_Of_A_Formula_Loop_That_Reads_The_Response(string first, string second) {
		// Arrange
		Guid xUId = Guid.NewGuid();
		Guid accumUId = Guid.NewGuid();
		Guid copyUId = Guid.NewGuid();
		FlowElement accumulate = Element("Accumulate", FormulaTaskManager);
		accumulate.Body = JsonSerializer.SerializeToElement($"{ParameterPath(copyUId)} + {ParameterPath(xUId)}");
		accumulate.ResultParameterMetaPath = JsonSerializer.SerializeToElement(accumUId.ToString());
		FlowElement copy = Element("Copy", FormulaTaskManager);
		copy.Body = JsonSerializer.SerializeToElement(ParameterPath(accumUId));
		copy.ResultParameterMetaPath = JsonSerializer.SerializeToElement(copyUId.ToString());
		List<ProcessParameter> parameters = [
			Parameter("X", ElementPath(CallUId, ResponseUId), xUId),
			Parameter("Accum", uid: accumUId),
			Parameter("Copy", uid: copyUId)
		];

		// Act
		IReadOnlyDictionary<string, string> withheld = Classify(parameters, [WebServiceCall(), accumulate, copy], [first, second]);

		// Assert
		withheld.Keys.Should().BeEquivalentTo(["Accum", "Copy"],
			because: "Copy copies Accum, which reads the response, so the order the loop is walked in must not matter");
	}

	[TestCase("P1", "P2")]
	[TestCase("P2", "P1")]
	[Description("A loop between ordinary elements cannot hide the response: an output of either element is withheld whichever is requested first.")]
	public void Classify_Should_Withhold_Outputs_Of_An_Element_Loop_That_Reads_The_Response(string first, string second) {
		// Arrange
		Guid xOut = Guid.NewGuid();
		Guid yOut = Guid.NewGuid();
		Guid zOut = Guid.NewGuid();
		FlowElement x = Element("X", UserTaskManager, ElementParameter("Out", xOut));
		FlowElement y = Element("Y", UserTaskManager, ElementParameter("Out", yOut));
		FlowElement z = Element("Z", UserTaskManager, ElementParameter("Out", zOut),
			ElementParameter("In", Guid.NewGuid(), ElementPath(CallUId, ResponseUId)));
		x.Parameters.Add(ElementParameter("FromY", Guid.NewGuid(), ElementPath(y.UId, yOut)));
		x.Parameters.Add(ElementParameter("FromZ", Guid.NewGuid(), ElementPath(z.UId, zOut)));
		y.Parameters.Add(ElementParameter("FromX", Guid.NewGuid(), ElementPath(x.UId, xOut)));
		List<ProcessParameter> parameters = [
			Parameter("P1", ElementPath(x.UId, xOut)),
			Parameter("P2", ElementPath(y.UId, yOut))
		];

		// Act
		IReadOnlyDictionary<string, string> withheld = Classify(parameters, [WebServiceCall(), x, y, z], [first, second]);

		// Assert
		withheld.Keys.Should().BeEquivalentTo(["P1", "P2"],
			because: "Y passes on what X received, and X received the response through Z");
	}

	[Test]
	[Description("A Call web service element nested in an event sub-process is found, and its Response is withheld.")]
	public void Classify_Should_See_A_WebService_Element_Nested_In_An_Event_SubProcess() {
		// Arrange
		FlowElement eventSubProcess = Element("EventSub1", new Guid("0824AF03-1340-47A3-8787-EF542F566992"));
		eventSubProcess.FlowElements = [WebServiceCall()];
		List<ProcessParameter> parameters = [Parameter("Echoed", $"[#[IsOwnerSchema:false].[IsSchema:false].[Element:{{{eventSubProcess.UId}}}].[Element:{{{CallUId}}}].[Parameter:{{{ResponseUId}}}]#]")];

		// Act
		IReadOnlyDictionary<string, string> withheld = Classify(parameters, [eventSubProcess], ["Echoed"]);

		// Assert
		withheld.Should().ContainKey("Echoed",
			because: "an event sub-process keeps its own elements, and the web service element there is a source too");
	}

	[Test]
	[Description("A Script task in a process whose only web service element is nested in an event sub-process still withholds every result.")]
	public void Classify_Should_Withhold_Everything_For_A_Script_Task_Beside_A_Nested_WebService() {
		// Arrange
		FlowElement eventSubProcess = Element("EventSub1", new Guid("0824AF03-1340-47A3-8787-EF542F566992"));
		eventSubProcess.FlowElements = [WebServiceCall(), Element("Script1", ScriptTaskManager)];
		List<ProcessParameter> parameters = [Parameter("Plain")];

		// Act
		IReadOnlyDictionary<string, string> withheld = Classify(parameters, [eventSubProcess], ["Plain"]);

		// Assert
		withheld.Should().ContainKey("Plain", because: "the nested Script task can write the parameter from the response");
	}

	[Test]
	[Description("The classifier reads only the active version of a called family when the process library names it.")]
	public void Classifier_Should_Read_Only_The_Active_Version_Of_A_Called_Family() {
		// Arrange
		Guid storedUId = Guid.NewGuid();
		Guid activeUId = Guid.NewGuid();
		Guid oldUId = Guid.NewGuid();
		IProcessModelGenerator generator = Substitute.For<IProcessModelGenerator>();
		generator.ReadFlowElements(activeUId, Arg.Any<int>()).Returns(new List<FlowElement>());
		IProcessVersionLibReader versions = Substitute.For<IProcessVersionLibReader>();
		versions.Read(storedUId.ToString("D"), Arg.Any<TimeSpan>()).Returns(new ProcessVersionFacts {
			ActiveVersionSchemaUId = activeUId.ToString(),
			Versions = [
				new ProcessVersionFamilyMember { SchemaUId = storedUId.ToString() },
				new ProcessVersionFamilyMember { SchemaUId = oldUId.ToString() }
			]
		});
		FlowElement sub = Element("Sub1", SubProcessManager, ElementParameter("Result", Guid.NewGuid()));
		sub.SchemaUId = storedUId;
		Clio.Command.ProcessModel.ProcessModel model = new(Guid.NewGuid(), "UsrCaller") {
			Parameters = [Parameter("Out", ElementPath(sub.UId, Guid.Parse(sub.Parameters[0].UId)))],
			FlowElements = [sub]
		};
		ProcessResultSourceClassifier classifier = new(generator, versions);

		// Act
		IReadOnlyDictionary<string, string> withheld = classifier.FindWebServiceFed(model, ["Out"],
			DateTimeOffset.UtcNow.AddSeconds(30));

		// Assert
		withheld.Should().BeEmpty(because: "the version that runs calls no web service");
		generator.Received(1).ReadFlowElements(activeUId, Arg.Any<int>());
		generator.DidNotReceive().ReadFlowElements(oldUId, Arg.Any<int>());
	}

	[Test]
	[Description("A called process is not read once the stop time has passed, and counts as unchecked.")]
	public void Classifier_Should_Not_Read_Past_The_Stop_Time() {
		// Arrange
		IProcessModelGenerator generator = Substitute.For<IProcessModelGenerator>();
		IProcessVersionLibReader versions = Substitute.For<IProcessVersionLibReader>();
		FlowElement sub = Element("Sub1", SubProcessManager, ElementParameter("Result", Guid.NewGuid()));
		sub.SchemaUId = Guid.NewGuid();
		Clio.Command.ProcessModel.ProcessModel model = new(Guid.NewGuid(), "UsrCaller") {
			Parameters = [Parameter("Out", ElementPath(sub.UId, Guid.Parse(sub.Parameters[0].UId)))],
			FlowElements = [sub]
		};
		ProcessResultSourceClassifier classifier = new(generator, versions);

		// Act
		IReadOnlyDictionary<string, string> withheld = classifier.FindWebServiceFed(model, ["Out"],
			DateTimeOffset.UtcNow.AddSeconds(-1));

		// Assert
		withheld.Should().ContainKey("Out", because: "a called process clio had no time to read is not proven clean");
		versions.DidNotReceive().Read(Arg.Any<string>(), Arg.Any<TimeSpan>());
		generator.DidNotReceive().ReadFlowElements(Arg.Any<Guid>(), Arg.Any<int>());
	}

	[Test]
	[Description("Two processes calling each other are not frozen as clean: the one with the web service element taints the other.")]
	public void Classifier_Should_Resolve_Mutually_Calling_Processes() {
		// Arrange
		Guid aUId = Guid.NewGuid();
		Guid bUId = Guid.NewGuid();
		FlowElement callsB = Element("CallB", SubProcessManager);
		callsB.SchemaUId = bUId;
		FlowElement callsA = Element("CallA", SubProcessManager);
		callsA.SchemaUId = aUId;
		IProcessModelGenerator generator = Substitute.For<IProcessModelGenerator>();
		generator.ReadFlowElements(aUId, Arg.Any<int>()).Returns(new List<FlowElement> { callsB, WebServiceCall() });
		generator.ReadFlowElements(bUId, Arg.Any<int>()).Returns(new List<FlowElement> { callsA });
		IProcessVersionLibReader versions = Substitute.For<IProcessVersionLibReader>();
		versions.Read(Arg.Any<string>(), Arg.Any<TimeSpan>())
			.Returns(call => new ProcessVersionFacts { ActiveVersionSchemaUId = call.ArgAt<string>(0) });
		FlowElement toA = Element("SubA", SubProcessManager, ElementParameter("Result", Guid.NewGuid()));
		toA.SchemaUId = aUId;
		FlowElement toB = Element("SubB", SubProcessManager, ElementParameter("Result", Guid.NewGuid()));
		toB.SchemaUId = bUId;
		Clio.Command.ProcessModel.ProcessModel model = new(Guid.NewGuid(), "UsrCaller") {
			Parameters = [
				Parameter("FromA", ElementPath(toA.UId, Guid.Parse(toA.Parameters[0].UId))),
				Parameter("FromB", ElementPath(toB.UId, Guid.Parse(toB.Parameters[0].UId)))
			],
			FlowElements = [toA, toB]
		};
		ProcessResultSourceClassifier classifier = new(generator, versions);

		// Act
		IReadOnlyDictionary<string, string> withheld = classifier.FindWebServiceFed(model, ["FromA", "FromB"],
			DateTimeOffset.UtcNow.AddSeconds(30));

		// Assert
		withheld.Keys.Should().BeEquivalentTo(["FromA", "FromB"],
			because: "B calls A, which calls a web service, so B's verdict must not be the one cached while A was being read");
	}
}
