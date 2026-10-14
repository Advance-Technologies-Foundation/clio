using System.Collections.Generic;
using Clio.Command.ProcessModel;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.ProcessModel;

/// <summary>
/// Covers the two warnings a process-parameter mapping earns (ENG-103081). A <c>targetProcessParameter</c> mapping
/// is a BINDING the parameter is read through, not an assignment made when a branch runs: a second binding of the
/// same parameter silently replaces the first, and in a multi-start process a binding to one branch's element reads
/// the default on a run started from another start. Measured on a stand; these tests pin the pure analysis, and in
/// particular that it stays silent where the binding works, so the warning cannot teach a caller to ignore it.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "ProcessModel")]
public class ProcessParameterBindingWarningsTests {

	#region Fields: Private

	// The ENG-103081 scenario: two signal starts on one object, each followed by its own read of the triggering record,
	// both branches joining into one consumer.
	private const string TwoStartElements = """
		"elements":[
			{"name":"SigA","type":"signalStart"},{"name":"SigB","type":"signalStart"},
			{"name":"ReadA","type":"readData"},{"name":"ReadB","type":"readData"},
			{"name":"Join","type":"formulaTask"},{"name":"End1","type":"endEvent"}],
		"flows":[
			{"source":"SigA","target":"ReadA"},{"source":"SigB","target":"ReadB"},
			{"source":"ReadA","target":"Join"},{"source":"ReadB","target":"Join"},
			{"source":"Join","target":"End1"}]
		""";

	private const string ReadAName =
		"""{"targetProcessParameter":"PName","sourceElement":"ReadA","sourceElementParameter":"ResultEntity","sourceColumn":"Name"}""";

	private const string ReadBName =
		"""{"targetProcessParameter":"PName","sourceElement":"ReadB","sourceElementParameter":"ResultEntity","sourceColumn":"Name"}""";

	#endregion

	#region Methods: Private

	private static string Descriptor(string elementsAndFlows, params string[] mappings) =>
		$$"""{"name":"UsrProc",{{elementsAndFlows}},"mappings":[{{string.Join(",", mappings)}}]}""";

	private static DescribeProcessResult TwoStartProcess(params DescribedParameter[] parameters) =>
		new() {
			Elements = [
				new DescribedElement { Name = "SigA", Uid = "a0000000-0000-0000-0000-000000000001", Type = "ProcessSchemaStartSignalEvent", BuildType = "signalstart" },
				new DescribedElement { Name = "SigB", Uid = "a0000000-0000-0000-0000-000000000002", Type = "ProcessSchemaStartSignalEvent", BuildType = "signalstart" },
				new DescribedElement {
					Name = "ReadA", Uid = "a0000000-0000-0000-0000-000000000003", Type = "ProcessSchemaUserTask",
					BuildType = "readdata",
					Parameters = [new DescribedParameter { Name = "ResultCount", UId = "b0000000-0000-0000-0000-000000000003" }]
				},
				new DescribedElement { Name = "ReadB", Uid = "a0000000-0000-0000-0000-000000000004", Type = "ProcessSchemaUserTask", BuildType = "readdata" },
				new DescribedElement { Name = "Join", Uid = "a0000000-0000-0000-0000-000000000005", Type = "ProcessSchemaFormulaTask", BuildType = "formulatask" }
			],
			Flows = [
				new DescribedFlow { Source = "SigA", Target = "ReadA" },
				new DescribedFlow { Source = "SigB", Target = "ReadB" },
				new DescribedFlow { Source = "ReadA", Target = "Join" },
				new DescribedFlow { Source = "ReadB", Target = "Join" }
			],
			Parameters = [.. parameters]
		};

	private static string AddMapping(string mapping) => $$"""{"op":"addMapping","mapping":{{mapping}}}""";

	#endregion

	#region Methods: ForDescriptor

	[Test]
	[Description("Two mappings onto one process parameter in a build warn once, naming both sources and the one that is saved (the last listed, measured), and point at formulaTask + formula.resultProcessParameter as the per-branch assignment. This is the ENG-103081 scenario: the build used to succeed in silence and the shared parameter read empty on the run.")]
	public void ForDescriptor_ShouldWarn_WhenTwoMappingsBindTheSameProcessParameter() {
		// Arrange
		string descriptor = Descriptor(TwoStartElements, ReadAName, ReadBName);

		// Act
		IReadOnlyList<string> warnings = ProcessParameterBindingWarnings.ForDescriptor(descriptor);

		// Assert
		warnings.Should().ContainSingle(w => w.Contains("binds process parameter 'PName' 2 times"),
			because: "a second binding of one parameter silently replaces the first");
		warnings.Should().Contain(w => w.Contains("only the last one listed (ReadB.ResultEntity.Name) is saved")
				&& w.Contains("formula.resultProcessParameter 'PName'"),
			because: "the caller must learn which binding survived and how to assign per branch instead");
	}

	[Test]
	[Description("In a process with two starts, a binding whose source element only one start reaches warns, naming the start whose runs read the default. The surviving binding of the ENG-103081 scenario is exactly this: ReadB never runs on a SigA run.")]
	public void ForDescriptor_ShouldWarn_WhenSomeStartDoesNotReachTheSourceElement() {
		// Arrange
		string descriptor = Descriptor(TwoStartElements, ReadAName);

		// Act
		IReadOnlyList<string> warnings = ProcessParameterBindingWarnings.ForDescriptor(descriptor);

		// Assert
		warnings.Should().ContainSingle(because: "one binding, one start that bypasses its source");
		warnings[0].Should().Contain("started by 'SigB' does not reach",
			because: "the warning has to name the start whose runs read the default value");
		warnings[0].Should().NotContain("'SigA'",
			because: "the start that does reach the element is not a problem and must not be named as one");
	}

	[Test]
	[Description("A single mapping of a single-start process raises no warning, even though its source element runs after the start: that is the supported 'expose an element output as a process output' pattern, measured to read the element's value once it ran, so warning there would be false.")]
	public void ForDescriptor_ShouldNotWarn_ForOneBindingInASingleStartProcess() {
		// Arrange
		const string singleStart = """
			"elements":[{"name":"Start1","type":"startEvent"},{"name":"ReadA","type":"readData"},{"name":"End1","type":"endEvent"}],
			"flows":[{"source":"Start1","target":"ReadA"},{"source":"ReadA","target":"End1"}]
			""";
		string descriptor = Descriptor(singleStart, ReadAName);

		// Act
		IReadOnlyList<string> warnings = ProcessParameterBindingWarnings.ForDescriptor(descriptor);

		// Assert
		warnings.Should().BeEmpty(because: "the binding reads correctly once the element ran; a warning would be false");
	}

	[Test]
	[Description("In a multi-start process a binding whose source element sits AFTER the join - so every start reaches it - raises no warning: the value exists on every run that reads it after that element.")]
	public void ForDescriptor_ShouldNotWarn_WhenEveryStartReachesTheSourceElement() {
		// Arrange
		string descriptor = Descriptor(TwoStartElements,
			"""{"targetProcessParameter":"PTitle","sourceElement":"Join","sourceElementParameter":"Result"}""");

		// Act
		IReadOnlyList<string> warnings = ProcessParameterBindingWarnings.ForDescriptor(descriptor);

		// Assert
		warnings.Should().BeEmpty(because: "both starts reach the element, so neither run reads a value that cannot exist");
	}

	[Test]
	[Description("Mappings onto ELEMENT inputs and non-element sources available from the start (a value, another process parameter) raise no warning: neither the duplicate rule nor the reachability rule concerns them.")]
	public void ForDescriptor_ShouldNotWarn_ForElementInputsOrSourcesAvailableAtStart() {
		// Arrange
		string descriptor = Descriptor(TwoStartElements,
			"""{"elementName":"Join","elementParameter":"Body","value":"x"}""",
			"""{"elementName":"Join","elementParameter":"Body","value":"y"}""",
			"""{"targetProcessParameter":"PName","processParameter":"PInput"}""",
			"""{"targetProcessParameter":"PCount","value":3}""");

		// Act
		IReadOnlyList<string> warnings = ProcessParameterBindingWarnings.ForDescriptor(descriptor);

		// Assert
		warnings.Should().BeEmpty(because: "only a process parameter bound twice, or bound to an element some start skips, is a hazard");
	}

	[Test]
	[Description("An unparseable descriptor yields no warning rather than an exception: the analysis runs after a build that already succeeded, and must not turn it into a reported failure.")]
	public void ForDescriptor_ShouldReturnNothing_WhenTheDescriptorIsNotJson() {
		// Act
		IReadOnlyList<string> warnings = ProcessParameterBindingWarnings.ForDescriptor("{not json");

		// Assert
		warnings.Should().BeEmpty(because: "a post-success analysis must be tolerant of any payload");
	}

	[Test]
	[Description("The unreached-start warning stays quiet for a start whose path assigns the parameter with a Formula: that assignment wins over the binding (measured), so after the caller applied the remedy the warning recommends, warning again would be false.")]
	public void ForDescriptor_ShouldNotWarn_WhenTheOtherStartAssignsTheParameterWithAFormula() {
		// Arrange
		const string withFormulaOnB = """
			"elements":[
				{"name":"SigA","type":"signalStart"},{"name":"SigB","type":"signalStart"},
				{"name":"ReadA","type":"readData"},{"name":"ReadB","type":"readData"},
				{"name":"SetFromB","type":"formulaTask","formula":{"body":"1","resultProcessParameter":"PName"}},
				{"name":"Join","type":"formulaTask"}],
			"flows":[
				{"source":"SigA","target":"ReadA"},{"source":"SigB","target":"ReadB"},
				{"source":"ReadB","target":"SetFromB"},
				{"source":"ReadA","target":"Join"},{"source":"SetFromB","target":"Join"}]
			""";
		string descriptor = Descriptor(withFormulaOnB, ReadAName);

		// Act
		IReadOnlyList<string> warnings = ProcessParameterBindingWarnings.ForDescriptor(descriptor);

		// Assert
		warnings.Should().BeEmpty(because: "the SigB run sets PName in its own Formula, so its value exists there");
	}

	#endregion

	#region Methods: ForOperations

	[Test]
	[Description("Two addMapping operations onto one process parameter in one modify warn, the same way the build path does.")]
	public void ForOperations_ShouldWarn_WhenOneBatchBindsTheSameParameterTwice() {
		// Arrange
		string operations = $"[{AddMapping(ReadAName)},{AddMapping(ReadBName)}]";

		// Act
		IReadOnlyList<string> warnings = ProcessParameterBindingWarnings.ForOperations(operations, null, null);

		// Assert
		warnings.Should().ContainSingle(w => w.Contains("this addMapping batch binds process parameter 'PName' 2 times"),
			because: "the batch keeps only the last binding");
	}

	[Test]
	[Description("An addMapping onto a parameter the process already binds to a DIFFERENT source warns, naming the binding it replaced - modify has no removeMapping, so a re-map is a silent overwrite of what another branch may still rely on.")]
	public void ForOperations_ShouldWarn_WhenTheEditReplacesADifferentStoredBinding() {
		// Arrange
		DescribeProcessResult before = TwoStartProcess(new DescribedParameter {
			Name = "PName", Source = "Script", Value = "[#...#]",
			SourceElement = "ReadA", SourceElementParameter = "ResultEntity", SourceColumn = "Name"
		});

		// Act
		IReadOnlyList<string> warnings = ProcessParameterBindingWarnings.ForOperations(
			$"[{AddMapping(ReadBName)}]", before, null);

		// Assert
		warnings.Should().ContainSingle(w => w.Contains("replaced the existing binding of process parameter 'PName'")
				&& w.Contains("was ReadA.ResultEntity.Name, now ReadB.ResultEntity.Name"),
			because: "the caller must see which binding the edit dropped");
	}

	[Test]
	[Description("Re-applying the binding a parameter already has - including a whole element output the read-back reports only as a UId meta path - and overwriting a literal default raise no warning: nothing a caller relies on is lost.")]
	public void ForOperations_ShouldNotWarn_WhenTheStoredValueIsTheSameBindingOrALiteral() {
		// Arrange
		DescribeProcessResult before = TwoStartProcess(
			new DescribedParameter {
				Name = "PCount", Source = "Script",
				Value = "[#[IsOwnerSchema:false].[IsSchema:false].[Element:{a0000000-0000-0000-0000-000000000003}]."
					+ "[Parameter:{b0000000-0000-0000-0000-000000000003}]#]",
				ValueDisplay = "Number of records"
			},
			new DescribedParameter { Name = "PLabel", Source = "ConstValue", Value = "draft" });
		string operations = "[" + AddMapping(
				"""{"targetProcessParameter":"PCount","sourceElement":"ReadA","sourceElementParameter":"ResultCount"}""")
			+ "," + AddMapping("""{"targetProcessParameter":"PLabel","value":"final"}""") + "]";

		// Act
		IReadOnlyList<string> warnings = ProcessParameterBindingWarnings.ForOperations(operations, before, null);

		// Assert
		warnings.Should().BeEmpty(because: "the same binding re-applied and a replaced literal default lose nothing");
	}

	[Test]
	[Description("Re-applying a stored binding with the element named by its UId - which the modify path accepts - is recognised as the same binding, and a stored non-element source (a formula or another process parameter) is not reported as a replaced branch binding: its read-back cannot be compared with the request, and replacing a default is ordinary.")]
	public void ForOperations_ShouldNotWarn_ForAUidNamedSameBindingOrANonElementStoredSource() {
		// Arrange
		DescribeProcessResult before = TwoStartProcess(
			new DescribedParameter {
				Name = "PName", Source = "Script", Value = "[#...#]",
				SourceElement = "ReadA", SourceElementParameter = "ResultEntity", SourceColumn = "Name"
			},
			new DescribedParameter { Name = "PCopy", Source = "Script", Value = "[#Amount#]", ValueDisplay = "Amount" });
		string operations = "[" + AddMapping("""
				{"targetProcessParameter":"PName","sourceElement":"a0000000-0000-0000-0000-000000000003",
				 "sourceElementParameter":"ResultEntity","sourceColumn":"Name"}
				""")
			+ "," + AddMapping("""{"targetProcessParameter":"PCopy","processParameter":"Amount"}""") + "]";

		// Act
		IReadOnlyList<string> warnings = ProcessParameterBindingWarnings.ForOperations(operations, before, null);

		// Assert
		warnings.Should().BeEmpty(because: "neither mapping drops a branch binding a caller relies on");
	}

	[Test]
	[Description("A binding the saved read-back no longer carries - a later operation in the same batch replaced it with a literal - earns neither the replaced nor the unreached warning: both would describe a binding that does not exist.")]
	public void ForOperations_ShouldNotWarn_WhenTheSavedParameterNoLongerCarriesTheBinding() {
		// Arrange
		DescribeProcessResult before = TwoStartProcess(new DescribedParameter {
			Name = "PName", Source = "Script", Value = "[#...#]",
			SourceElement = "ReadB", SourceElementParameter = "ResultEntity", SourceColumn = "Name"
		});
		DescribeProcessResult after = TwoStartProcess(new DescribedParameter {
			Name = "PName", Source = "ConstValue", Value = "x"
		});

		// Act
		IReadOnlyList<string> warnings = ProcessParameterBindingWarnings.ForOperations(
			$"[{AddMapping(ReadAName)}]", before, after);

		// Assert
		warnings.Should().BeEmpty(because: "the saved parameter holds a literal, not the binding the batch added");
	}

	[Test]
	[Description("On modify, reachability is judged on the SAVED graph: an added binding to one branch's element in a two-start process warns, and one to an element after the join does not.")]
	public void ForOperations_ShouldJudgeReachabilityOnTheSavedGraph() {
		// Arrange
		DescribeProcessResult after = TwoStartProcess();
		string unreached = $"[{AddMapping(ReadAName)}]";
		string reached = "[" + AddMapping(
			"""{"targetProcessParameter":"PTitle","sourceElement":"Join","sourceElementParameter":"Result"}""") + "]";

		// Act
		IReadOnlyList<string> unreachedWarnings = ProcessParameterBindingWarnings.ForOperations(unreached, null, after);
		IReadOnlyList<string> reachedWarnings = ProcessParameterBindingWarnings.ForOperations(reached, null, after);

		// Assert
		unreachedWarnings.Should().ContainSingle(w => w.Contains("started by 'SigB' does not reach"),
			because: "a SigB run never executes ReadA, so PName reads its default there");
		reachedWarnings.Should().BeEmpty(because: "both starts reach the element after the join");
	}

	[Test]
	[Description("A single addMapping onto an unbound parameter, with nothing else in the batch and a single-start read-back, raises no warning.")]
	public void ForOperations_ShouldNotWarn_ForOneNewBindingInASingleStartProcess() {
		// Arrange
		DescribeProcessResult process = new() {
			Elements = [
				new DescribedElement { Name = "Start1", Type = "ProcessSchemaStartEvent", BuildType = "startevent" },
				new DescribedElement { Name = "ReadA", Type = "ProcessSchemaUserTask", BuildType = "readdata" }
			],
			Flows = [new DescribedFlow { Source = "Start1", Target = "ReadA" }],
			Parameters = [new DescribedParameter { Name = "PName", Source = "None" }]
		};

		// Act
		IReadOnlyList<string> warnings = ProcessParameterBindingWarnings.ForOperations(
			$"[{AddMapping(ReadAName)}]", process, process);

		// Assert
		warnings.Should().BeEmpty(because: "a lone binding read after its element ran is the supported pattern");
	}

	#endregion
}
