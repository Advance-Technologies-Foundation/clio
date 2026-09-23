using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools.ProcessDesigner;
using Clio.Command.ProcessModel;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end coverage for the MULTI-INSTANCE Sub-process element (ENG-99856) over the real MCP path. NOT in
/// CI - run manually, gated on the <c>process-designer</c> feature and a reachable environment carrying a
/// CrtProcessBuilder of at least 1.6.6.13.
/// <para>What only a live server can prove here is the PLATFORM's rebuild. Assigning <c>SchemaUId</c> on a
/// converted element makes the platform clear the element's parameters and re-derive them, and every unit
/// test drives that against a substituted schema manager. This is the only place the real
/// <c>ProcessSchemaActivity</c> rebuild runs against a real saved schema - so it is the only place that can
/// show the five service parameters actually replacing the callee's, the callee's contract actually landing
/// one level down as the input collection's item properties, and a dotted per-item mapping actually
/// surviving the save-and-read round trip.</para>
/// <para>The three cases are deliberately asymmetric in what they are FOR. The happy path proves the shape;
/// the refusal proves the one write that would be silently erased is stopped at the door, because a value
/// written into the OUTPUT collection is cleared by the platform on the next synchronization with no error
/// anywhere - the failure mode the whole refusal exists for; and the DE-CONVERSION proves the destructive
/// direction, which every other test in this feature drives against a substituted applier. It is the only
/// place the round trip is closed against a real saved schema: the five go away, the callee's contract
/// comes back to the root, and the value a caller mapped per item comes back WITH it.</para>
/// <para>The remaining cases are the edits that go THROUGH the platform's rebuild on an element that is
/// already multi-instance - a mode change (which must not), a re-synchronization and a retarget (which must,
/// around the same five parameter objects) - plus a graph whose service parameters have READERS, which is the
/// only kind on which the dangling-reference scan has anything to find. What they share is the assertion no
/// in-memory test can make: the five UIds read back unchanged from a real saved schema.</para>
/// <para>Every case builds its own schemas, and they are left on the environment: nothing here deletes a
/// process. Strictly sequential - schema writes in a parallel burst trip IIS rapid-fail on a .NET Framework
/// stand and take the app pool down.</para>
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature(CreateBusinessProcessTool.CreateBusinessProcessToolName)]
[NonParallelizable]
[Category(McpE2ECategories.ProcessDesigner)]
public sealed class SubProcessMultiInstanceToolE2ETests {

	private const string ToolName = CreateBusinessProcessTool.CreateBusinessProcessToolName;
	private const string DescribeToolName = DescribeProcessTool.ToolName;
	private const string ModifyToolName = ModifyBusinessProcessTool.ModifyBusinessProcessToolName;

	// The five parameters a converted element carries INSTEAD of the callee's: two collections and three
	// iteration counters.
	private static readonly string[] ServiceParameterNames = [
		"InputRecordCollection", "OutputRecordCollection", "CompletedIterationsCount",
		"TerminatedIterationsCount", "TotalIterationsCount"
	];

	#region Methods: Tests

	[Test]
	[Description("Over the real MCP path, create-business-process converts a sub-process element to multi-instance, binds a Read data collection onto its input collection and maps ONE per-item value through a dotted path; describe-business-process then reads the whole shape back. This is the only place the platform's own parameter rebuild runs against a real saved schema: the five service parameters replace the callee's, the callee's contract lands one level down as the input collection's item properties, and the dotted mapping survives the round trip.")]
	[AllureTag(ToolName)]
	[AllureName("create-business-process builds a multi-instance sub-process and describe reads it back")]
	public async Task CreateBusinessProcess_Should_BuildAMultiInstanceSubProcess_AndReadTheShapeBack() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync();
		string calleeName = $"UsrClioBpMiCallee{Guid.NewGuid():N}";
		string callerName = $"UsrClioBpMiCaller{Guid.NewGuid():N}";
		await ArrangeProcessAsync(context, BuildCalleeDescriptor(calleeName), "called process");

		// Act
		CallToolResult callResult = await CreateAsync(context, BuildMultiInstanceCallerDescriptor(callerName, calleeName));

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "converting an element to multi-instance while building it is one call and must not fail "
				+ "at the transport");
		JsonSerializer.Serialize(callResult).Should().Contain("created (UId:",
			because: "only a genuinely successful build logs the created-schema line - the command logs "
				+ "\"Building process\" BEFORE it calls the server, so a name match alone also passes a failure");

		DescribeProcessResult graph = await DescribeAsync(context, callerName);
		DescribedElement element = graph.Elements.Single(candidate => candidate.Name == "SubProcess1");
		DescribedElement readData = graph.Elements.Single(candidate => candidate.Name == "ReadData1");
		element.SubProcess.Should().NotBeNull(
			because: "a null block on a successful build is the silent-drop signature of a server that predates "
				+ "the element, and dereferencing it below would report a NullReferenceException instead of the "
				+ "assertion this test was written for");
		element.SubProcess!.MultiInstance.Should().Be(true,
			because: "THE acceptance criterion. Asserted against TRUE rather than falsy, so a server that "
				+ "reported no such field at all fails here instead of reading as single-instance");
		element.SubProcess.MultiInstanceOptions.Should().NotBeNull(
			because: "the options block is emitted on every multi-instance element; its absence beside "
				+ "multiInstance:true is a read that lost the conversion's own configuration");
		DescribedMultiInstanceOptions options = element.SubProcess.MultiInstanceOptions!;
		options.ExecutionMode.Should().Be("Sequential",
			because: "the EFFECTIVE mode is reported as the STRING the write side spells - the stored metadata "
				+ "omits Sequential entirely, and a caller cannot act on 'absent'");
		options.IgnoreErrors.Should().Be(false,
			because: "the other mode field is reported at its EFFECTIVE value too - the stored metadata suppresses "
				+ "false, and asserting against FALSE rather than falsy makes an omitted field fail here");
		options.InputCollection.Should().Be("InputRecordCollection",
			because: "a mapping is written in terms of this name, so the read has to report it rather than "
				+ "leaving the caller to know it");
		options.OutputCollection.Should().Be("OutputRecordCollection",
			because: "the output collection is the name a downstream reader maps FROM, and the refusal of a "
				+ "write into it is phrased in terms of it");
		options.CompletedIterationsCount.Should().Be("CompletedIterationsCount",
			because: "each counter role has to resolve to the parameter the conversion created - a null role is "
				+ "the malformed-element signature, which the platform's own load path throws on");
		options.TerminatedIterationsCount.Should().Be("TerminatedIterationsCount",
			because: "all three counter roles are reported, not only the first");
		options.TotalIterationsCount.Should().Be("TotalIterationsCount",
			because: "all three counter roles are reported, not only the first");
		options.CalleeInSync.Should().Be(true,
			because: "a freshly converted element carries the callee's whole contract one level down, and this - "
				+ "not inSync - is the field that says so on a multi-instance element. Asserted against TRUE so a "
				+ "server that does not report the field fails here instead of reading as unknown");

		// The parameter SHAPE is what the conversion changes, and it is the half no in-memory test can show.
		IReadOnlyCollection<string> parameterNames = element.Parameters.Select(parameter => parameter.Name).ToList();
		parameterNames.Should().BeEquivalentTo(new[] {
				"InputRecordCollection", "OutputRecordCollection", "CompletedIterationsCount",
				"TerminatedIterationsCount", "TotalIterationsCount" },
			because: "a converted element carries the five service parameters INSTEAD of the callee's - asserted "
				+ "as the whole set rather than as Contain, because a callee parameter left behind at the root "
				+ "is exactly the half-finished conversion this suite has to catch");

		DescribedParameter input = element.Parameters.Single(parameter => parameter.Name == "InputRecordCollection");
		input.ItemProperties.Should().NotBeNull(
			because: "the callee's contract moves one level down on conversion, and the item properties ARE that "
				+ "contract - a null here means the read never descended");
		input.ItemProperties!.Select(item => item.Name).Should().Contain("ItemName",
			because: "the called process's In parameter has to arrive as an item property of the input "
				+ "collection; it is the name a dotted per-item mapping addresses");
		string itemValue = input.ItemProperties.Single(item => item.Name == "ItemName").Value;
		itemValue.Should().NotBeNullOrWhiteSpace(
			because: "the dotted per-item mapping has to survive the save-and-read round trip at all");
		itemValue.Should().ContainEquivalentOf(ElementSegment(readData),
			because: "the source metapath has to name the READ DATA element it comes from. This is the "
				+ "ContainerUId failure, and a non-empty check cannot see it: when the source item property "
				+ "carries no ContainerUId, GetMetaPath still writes a NON-EMPTY path - it just drops the "
				+ "[Element:{...}] segment - and that path resolves at design time and binds to NOTHING at run "
				+ "time, silently");
	}

	[Test]
	[Description("Over the real MCP path, a mapping whose target is inside the OUTPUT collection is refused - on the collection ITSELF as well as on one of its items, because the first version of this refusal covered the items only and let the collection through. The platform derives those values and clears them on every synchronization, so the write is erased with no error at any layer - the caller would see a successful build and an element that quietly delivers nothing. The refusal is the only thing that tells them, and it has to say what to do instead: map FROM the collection.")]
	[AllureTag(ToolName)]
	[AllureName("create-business-process refuses a mapping into the output collection")]
	[TestCase("OutputRecordCollection.Echoed", "ResultCompositeObjectList.Name",
		TestName = "CreateBusinessProcess_Should_RefuseAMappingIntoTheOutputCollection(item)")]
	[TestCase("OutputRecordCollection", "ResultCompositeObjectList",
		TestName = "CreateBusinessProcess_Should_RefuseAMappingIntoTheOutputCollection(collection)")]
	public async Task CreateBusinessProcess_Should_RefuseAMappingIntoTheOutputCollection(string target,
			string source) {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync();
		string calleeName = $"UsrClioBpMiOutCallee{Guid.NewGuid():N}";
		string callerName = $"UsrClioBpMiOutCaller{Guid.NewGuid():N}";
		await ArrangeProcessAsync(context, BuildCalleeDescriptor(calleeName), "called process");

		// Act
		CallToolResult callResult = await CreateAsync(context,
			BuildOutputCollectionTargetDescriptor(callerName, calleeName, target, source));

		// Assert
		JsonSerializer.Serialize(callResult).Should().NotContain("created (UId:",
			because: "the build has to be refused rather than saved - a process saved with that mapping looks "
				+ "healthy and delivers nothing, which is the state the refusal exists to prevent. For the "
				+ "COLLECTION case the source is deliberately type-compatible, so without the refusal this build "
				+ "would SUCCEED rather than fail on a type mismatch");
		string log = LogText(callResult);
		log.Should().Contain($"'{target}' is inside the output collection",
			because: "the refusal has to name the exact path it refused AND be the output-collection refusal - "
				+ "a direction guard, a type mismatch or a 'no such parameter' would all quote the name back, so "
				+ "the name alone does not identify which refusal spoke");
		log.Should().Contain("map FROM it",
			because: "a refusal that only says no leaves the caller without the one move that works: the values "
				+ "in that collection are READ, by naming it as the SOURCE of a mapping");

		int callerDescribeExitCode = await DescribeExitCodeAsync(context, callerName);
		int calleeDescribeExitCode = await DescribeExitCodeAsync(context, calleeName);
		callerDescribeExitCode.Should().NotBe(0,
			because: "nothing may have been saved - a refused build that still left a process behind is the "
				+ "half-applied state the refusal is supposed to make impossible");
		calleeDescribeExitCode.Should().Be(0,
			because: "the positive control: the same session describes the CALLEE successfully, so the failure "
				+ "above is about the caller's absence and not about a describe path that fails for everything");
	}

	[Test]
	[Description("Over the real MCP path, modify-business-process de-converts a multi-instance element back to a single call, and describe reads the restored shape. The DESTRUCTIVE direction is the half no unit test can show: every multi-instance fixture substitutes ISubProcessApplier, so the platform's own re-derivation never runs, and this is the only place it does. Two things are asserted because two things were claimed and one of them was wrong: the callee's parameters come back to the ROOT, and a value mapped per item comes back WITH them - the guidance said it did not survive the round trip while the server's own notice said it did.")]
	[AllureTag(ModifyToolName)]
	[AllureName("modify-business-process de-converts a multi-instance sub-process and describe reads it back")]
	public async Task ModifyBusinessProcess_Should_DeconvertAMultiInstanceSubProcess_AndRestoreTheCalleeContract() {
		// Arrange - the SAME graph the happy path builds, so anything that differs below is the de-conversion.
		await using ArrangeContext context = await ArrangeAsync();
		string calleeName = $"UsrClioBpMiDeCallee{Guid.NewGuid():N}";
		string callerName = $"UsrClioBpMiDeCaller{Guid.NewGuid():N}";
		await ArrangeProcessAsync(context, BuildCalleeDescriptor(calleeName), "called process");
		await ArrangeProcessAsync(context, BuildMultiInstanceCallerDescriptor(callerName, calleeName),
			"multi-instance caller");
		// AND PROVE IT CONVERTED, because four of the five assertions below are satisfied by an element that
		// never was multi-instance: multiInstance:false, a null options block, no InputRecordCollection at the
		// root and ItemName present at it are ALL true of a plain sub-process element. The Act would pass too -
		// `enabled: false` on a single-instance element is not refused, it answers AlreadyInRequestedState - so
		// the whole case would go green having exercised no de-conversion at all. That is exactly the
		// silently-discarded-block failure this fixture's own version gate exists for.
		DescribedElement converted = (await DescribeAsync(context, callerName)).Elements
			.Single(candidate => candidate.Name == "SubProcess1");
		converted.SubProcess.Should().NotBeNull(
			because: "a null block would make the conversion check below unassertable - and a null-conditional "
				+ "in its place short-circuits the WHOLE assertion, so the guard it is meant to be cannot fail");
		converted.SubProcess!.MultiInstance.Should().Be(true,
			because: "the arrange is only meaningful while the element IS multi-instance - if the server "
				+ "discarded the block and answered success, everything below is about a plain element");
		converted.Parameters.Select(parameter => parameter.Name).Should().BeEquivalentTo(ServiceParameterNames,
			because: "the five service parameters are what the de-conversion below has to remove, so all five "
				+ "have to be there first");
		string mappedItemValue = ItemValue(converted, "InputRecordCollection", "ItemName");
		mappedItemValue.Should().NotBeNullOrWhiteSpace(
			because: "the per-item value is what the de-conversion is claimed to bring back, so it has to exist "
				+ "before the de-conversion for the comparison below to mean anything");

		// Act
		CallToolResult callResult = await CallToolAsync(context, ModifyToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = callerName,
			["operations"] = """
				[
				  { "op": "setElement", "elementName": "SubProcess1",
				    "elementUpdate": { "subProcess": { "multiInstanceOptions": { "enabled": false } } } }
				]
				"""
		});

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a de-conversion is an ordinary edit and must not fail at the transport");
		// THE EXIT CODE, not a string. `"success":false` can never appear here: the command deserializes the
		// server envelope and THROWS on failure, so the wire field is never re-serialized into the tool result,
		// and the envelope the parser reads carries exit-code and execution-log-messages instead. A guard
		// written against a string that cannot occur is a guard that cannot fail - and this file already says
		// so one method down, where ArrangeProcessAsync explains why IsError is not enough either.
		ShouldHaveLanded(callResult,
			because: "the edit has to LAND - a refused de-conversion would leave the element multi-instance and "
				+ "every assertion below would then be about the arrange rather than about this operation");

		DescribedElement element = (await DescribeAsync(context, callerName)).Elements
			.Single(candidate => candidate.Name == "SubProcess1");
		element.SubProcess.Should().NotBeNull(
			because: "the element still calls the same process - only how many times changed");
		element.SubProcess!.MultiInstance.Should().Be(false,
			because: "THE acceptance criterion for this direction. Asserted against FALSE rather than falsy so "
				+ "that a server which stopped reporting the field at all fails here");
		element.SubProcess.MultiInstanceOptions.Should().BeNull(
			because: "the options block is emitted on a multi-instance element only, so a block beside "
				+ "multiInstance:false is a read describing an element that no longer exists in that shape");

		IReadOnlyCollection<string> parameterNames = element.Parameters.Select(parameter => parameter.Name).ToList();
		parameterNames.Should().Contain("ItemName",
			because: "the callee's contract comes back to the ROOT - that is what de-conversion means, and it "
				+ "is where a mapping addresses it again");
		foreach (string serviceParameter in ServiceParameterNames) {
			parameterNames.Should().NotContain(serviceParameter,
				because: $"'{serviceParameter}' is one of the five service parameters, and all five go away with "
					+ "the mode. Any one left behind is the half-finished de-conversion this case exists to catch - "
					+ "and the arm that restores an element after a failed re-synchronization once left the "
					+ "opposite residue, the callee's parameters beside the five, which saved and was invisible to "
					+ "every shape assertion this suite had");
		}

		element.Parameters.Single(parameter => parameter.Name == "ItemName").Value
			.Should().Be(mappedItemValue,
				because: "the value mapped per item comes back WITH the parameter, UNCHANGED - the mapping row "
					+ "pairs source and target by UId and the de-conversion clones the item properties back out "
					+ "with theirs. The shipped guidance asserted the opposite while the server's own notice "
					+ "asserted this, and only a real save-and-read can say which. Compared with the value read "
					+ "before, because a non-empty check also passes a value re-derived from somewhere else");
	}

	[Test]
	[Description("Over the real MCP path, a MODE-ONLY change on an element that is already multi-instance changes how it iterates and nothing else: the five service parameters keep their UIds, the per-item value is untouched and the omitted mode field is left as it was. The platform's rebuild re-mints the iteration counters and drops the item properties' stored values, so a mode change that went through it would still report success and read back as multi-instance - only the UIds and the value can tell the two apart, and only against a real saved schema.")]
	[AllureTag(ModifyToolName)]
	[AllureName("modify-business-process changes only the execution mode of a multi-instance sub-process")]
	public async Task ModifyBusinessProcess_Should_ChangeOnlyTheExecutionMode_AndKeepTheFiveParameterUIds() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync();
		string calleeName = $"UsrClioBpMiModeCallee{Guid.NewGuid():N}";
		string callerName = $"UsrClioBpMiModeCaller{Guid.NewGuid():N}";
		await ArrangeProcessAsync(context, BuildCalleeDescriptor(calleeName), "called process");
		await ArrangeProcessAsync(context, BuildMultiInstanceCallerDescriptor(callerName, calleeName),
			"multi-instance caller");
		DescribedElement before = await DescribeMultiInstanceElementAsync(context, callerName, "SubProcess1");
		IReadOnlyDictionary<string, string> uidsBefore = ServiceParameterUIds(before);
		string itemValueBefore = ItemValue(before, "InputRecordCollection", "ItemName");

		// Act
		CallToolResult callResult = await ModifyAsync(context, callerName, """
			[
			  { "op": "setElement", "elementName": "SubProcess1",
			    "elementUpdate": { "subProcess": { "multiInstanceOptions": { "executionMode": "Parallel" } } } }
			]
			""");

		// Assert
		ShouldHaveLanded(callResult,
			because: "a mode change on an already-converted element is an ordinary edit and has to land");
		LogText(callResult).Should().Contain("only how it iterates was changed",
			because: "the in-place arm is the one that must answer - the server says which arm ran, and a "
				+ "rebuild would answer with a different notice while reading back just as multi-instance");
		DescribedElement after = await DescribeMultiInstanceElementAsync(context, callerName, "SubProcess1");
		after.SubProcess!.MultiInstanceOptions!.ExecutionMode.Should().Be("Parallel",
			because: "the one field the request named has to change");
		after.SubProcess.MultiInstanceOptions.IgnoreErrors.Should().Be(false,
			because: "an OMITTED mode field is left as it is - an update naming one field must not reset the "
				+ "other to anything, default or not");
		ServiceParameterUIds(after).Should().Equal(uidsBefore,
			because: "a mode change must NOT rebuild the element. The rebuild re-mints the iteration counters, "
				+ "so a caller holding their UIds - every reader of a counter binds by UId - would be left "
				+ "holding stale identifiers behind a success answer");
		ItemValue(after, "InputRecordCollection", "ItemName").Should().Be(itemValueBefore,
			because: "the rebuild drops the item properties' stored values, so an unchanged per-item value is "
				+ "the second, independent sign that the element was not rebuilt");
	}

	[Test]
	[Description("Over the real MCP path, resync:true re-synchronizes a multi-instance element against a callee that gained a parameter. calleeInSync reports the drift first - read under the documented recipe, describe once BEFORE the callee changes - and after the resync the new parameter is one level down in the input collection, calleeInSync is true again, and the five service parameters kept their UIds. The resync de-converts, re-synchronizes and re-converts around the SAME five parameter objects; only a real platform rebuild can show that the UIds really survive it, and every unit test substitutes that rebuild.")]
	[AllureTag(ModifyToolName)]
	[AllureName("modify-business-process re-synchronizes a multi-instance sub-process after the callee changed")]
	public async Task ModifyBusinessProcess_Should_ResyncAMultiInstanceSubProcess_AndKeepTheFiveParameterUIds() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync();
		string calleeName = $"UsrClioBpMiResyncCallee{Guid.NewGuid():N}";
		string callerName = $"UsrClioBpMiResyncCaller{Guid.NewGuid():N}";
		await ArrangeProcessAsync(context, BuildCalleeDescriptor(calleeName), "called process");
		await ArrangeProcessAsync(context, BuildMultiInstanceCallerDescriptor(callerName, calleeName),
			"multi-instance caller");
		// THE RECIPE: describe the caller ONCE before the callee changes. The middle read is what makes the
		// drift read below meaningful - see docs/knowledge/platform/subprocess-insync-depends-on-the-schema-instance.md.
		DescribedElement before = await DescribeMultiInstanceElementAsync(context, callerName, "SubProcess1");
		IReadOnlyDictionary<string, string> uidsBefore = ServiceParameterUIds(before);
		string itemValueBefore = ItemValue(before, "InputRecordCollection", "ItemName");
		before.SubProcess!.MultiInstanceOptions!.CalleeInSync.Should().Be(true,
			because: "the drift below is only evidence if the element started in sync");
		await ModifyAndExpectSuccessAsync(context, calleeName, """
			[ { "op": "addParameter", "parameter": { "name": "Extra", "type": "Text", "direction": "In" } } ]
			""", "the callee gaining a parameter");
		DescribedElement drifted = await DescribeMultiInstanceElementAsync(context, callerName, "SubProcess1");
		drifted.SubProcess!.MultiInstanceOptions!.CalleeInSync.Should().Be(false,
			because: "the callee now declares a parameter the element does not carry, and on a multi-instance "
				+ "element calleeInSync is the only field that can say so - inSync compares the ROOT, which holds "
				+ "the five service parameters and never the callee's. Without this read the resync below would "
				+ "be exercised against an element nobody showed was out of sync");

		// Act
		CallToolResult callResult = await ModifyAsync(context, callerName, """
			[ { "op": "setElement", "elementName": "SubProcess1",
			    "elementUpdate": { "subProcess": { "resync": true } } } ]
			""");

		// Assert
		ShouldHaveLanded(callResult,
			because: "a re-synchronization of a multi-instance element is available and has to land");
		DescribedElement after = await DescribeMultiInstanceElementAsync(context, callerName, "SubProcess1");
		ServiceParameterUIds(after).Should().Equal(uidsBefore,
			because: "THE assertion only a real platform rebuild can answer. The resync de-converts, lets the "
				+ "platform re-derive the element - whose rebuild calls Parameters.Clear() unconditionally - and "
				+ "re-converts around the SAME five objects. A re-minted UId strands every reader of the "
				+ "collections and the counters, and a mapping into InputRecordCollection with them");
		after.Parameters.Single(parameter => parameter.Name == "InputRecordCollection").ItemProperties!
			.Select(item => item.Name).Should().Contain("Extra",
				because: "the parameter the callee gained has to arrive one level down, as an item property of "
					+ "the input collection. READ IT HONESTLY: the platform converges the element on a design-time "
					+ "read, so presence shows the pair is consistent, not by itself that the resync persisted it - "
					+ "the unchanged UIds above are the part a read cannot fake");
		ItemValue(after, "InputRecordCollection", "ItemName").Should().Be(itemValueBefore,
			because: "a per-item mapping onto a parameter the callee still declares survives the resync");
		after.SubProcess!.MultiInstanceOptions!.CalleeInSync.Should().Be(true,
			because: "the element carries the callee's whole contract again");
	}

	[Test]
	[Description("Over the real MCP path, retargeting a multi-instance element at a DIFFERENT called process keeps the five service parameters and their UIds and replaces the contract one level down: the new callee's parameters are the input collection's item properties and the old callee's are gone. A retarget goes through the platform's rebuild exactly as a resync does, so the UId-preservation claim is the same one and only a real saved schema can confirm it.")]
	[AllureTag(ModifyToolName)]
	[AllureName("modify-business-process retargets a multi-instance sub-process at another process")]
	public async Task ModifyBusinessProcess_Should_RetargetAMultiInstanceSubProcess_AndKeepTheFiveParameterUIds() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync();
		string calleeName = $"UsrClioBpMiRetargetCallee{Guid.NewGuid():N}";
		string otherCalleeName = $"UsrClioBpMiRetargetOther{Guid.NewGuid():N}";
		string callerName = $"UsrClioBpMiRetargetCaller{Guid.NewGuid():N}";
		await ArrangeProcessAsync(context, BuildCalleeDescriptor(calleeName), "called process");
		await ArrangeProcessAsync(context, BuildOtherCalleeDescriptor(otherCalleeName), "second called process");
		await ArrangeProcessAsync(context, BuildMultiInstanceCallerDescriptor(callerName, calleeName),
			"multi-instance caller");
		DescribedElement before = await DescribeMultiInstanceElementAsync(context, callerName, "SubProcess1");
		IReadOnlyDictionary<string, string> uidsBefore = ServiceParameterUIds(before);

		// Act
		CallToolResult callResult = await ModifyAsync(context, callerName, $$"""
			[ { "op": "setElement", "elementName": "SubProcess1",
			    "elementUpdate": { "subProcess": { "processName": "{{otherCalleeName}}" } } } ]
			""");

		// Assert
		ShouldHaveLanded(callResult,
			because: "a retarget of a multi-instance element is available and has to land");
		DescribedElement after = await DescribeMultiInstanceElementAsync(context, callerName, "SubProcess1");
		after.SubProcess!.Process.Should().Be(otherCalleeName,
			because: "the element has to call the process the request named");
		ServiceParameterUIds(after).Should().Equal(uidsBefore,
			because: "the retarget is re-converted around the SAME five parameter objects, so their UIds survive "
				+ "it - a mapping into InputRecordCollection and every reader of the counters keep binding");
		IReadOnlyCollection<string> itemNames = after.Parameters
			.Single(parameter => parameter.Name == "InputRecordCollection").ItemProperties!
			.Select(item => item.Name).ToList();
		itemNames.Should().Contain("OtherName",
			because: "the NEW callee's contract is what the input collection's item properties carry now");
		itemNames.Should().NotContain("ItemName",
			because: "the old callee's contract goes with the old selection - an item left behind would be a "
				+ "per-item value aimed at a name the new callee does not declare, which the run time skips "
				+ "with no error");
		after.SubProcess.MultiInstanceOptions!.CalleeInSync.Should().Be(true,
			because: "the element carries the new callee's whole contract");
	}

	[Test]
	[Description("Over the real MCP path, the READERS of a multi-instance element's service parameters keep binding across a re-synchronization, and are not reported as dangling. A downstream element iterating OutputRecordCollection and a process parameter reading TotalIterationsCount are the only ways to consume the iteration results; the resync scans for dangling references while the element is temporarily de-converted, and an earlier cut reported exactly these readers as reading 'a parameter this element no longer carries'. Every earlier graph in this fixture had no such reader, so that scan had never run against one on a real schema.")]
	[AllureTag(ModifyToolName)]
	[AllureName("modify-business-process keeps the readers of the service parameters bound across a resync")]
	public async Task ModifyBusinessProcess_Should_KeepTheReadersOfTheServiceParametersBound_WhenResynchronizing() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync();
		string calleeName = $"UsrClioBpMiReadersCallee{Guid.NewGuid():N}";
		string callerName = $"UsrClioBpMiReadersCaller{Guid.NewGuid():N}";
		await ArrangeProcessAsync(context, BuildCalleeDescriptor(calleeName), "called process");
		await ArrangeProcessAsync(context, BuildReadersCallerDescriptor(callerName, calleeName),
			"multi-instance caller with readers");
		ReaderSites before = await DescribeReaderSitesAsync(context, callerName);

		// Act
		CallToolResult callResult = await ModifyAsync(context, callerName, """
			[ { "op": "setElement", "elementName": "SubProcess1",
			    "elementUpdate": { "subProcess": { "resync": true } } } ]
			""");

		// Assert
		ShouldHaveLanded(callResult,
			because: "a resync with readers downstream is an ordinary edit and has to land. Landing is itself "
				+ "evidence here: the save runs the platform's pre-save process validation, which REFUSES a process "
				+ "carrying a reader of a parameter the element does not have - measured on this stand by "
				+ "de-converting this same graph, which is refused naming SubProcess2's input or IterationsTotal");
		LogText(callResult).Should().NotContain("no longer carries",
			because: "nothing was broken - the five are re-attached with their UIds - so a notice telling the "
				+ "caller to re-point or remove a working reader is advice that destroys a correct process");
		ReaderSites after = await DescribeReaderSitesAsync(context, callerName);
		after.ServiceParameterUIds.Should().Equal(before.ServiceParameterUIds,
			because: "the readers bind by UId, so the UIds surviving is what keeps them bound");
		after.CollectionReaderValue.Should().Be(before.CollectionReaderValue,
			because: "the downstream element's binding onto OutputRecordCollection has to come through untouched");
		after.CounterReaderValue.Should().Be(before.CounterReaderValue,
			because: "the process parameter's binding onto TotalIterationsCount has to come through untouched");
	}

	#endregion

	#region Methods: Descriptors

	// The callee: one In parameter, which becomes the input collection's single item property after the
	// conversion, and one Out parameter, which becomes the output collection's.
	private static string BuildCalleeDescriptor(string processName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP multi-instance callee E2E",
		  "packageName": "Custom",
		  "parameters": [
		    { "name": "ItemName", "type": "Text", "direction": "In" },
		    { "name": "Echoed", "type": "Text", "direction": "Out" }
		  ],
		  "elements": [
		    { "name": "StartEvent1", "type": "startEvent" },
		    { "name": "EndEvent1", "type": "endEvent" }
		  ],
		  "flows": [ { "source": "StartEvent1", "target": "EndEvent1" } ]
		}
		""";

	// A Read data element in COLLECTION mode supplies the collection to iterate. Its ResultCompositeObjectList
	// is the output whose data value type matches InputRecordCollection - ResultEntityCollection does NOT, and
	// binding that one is refused by the type check.
	//
	// Two mappings, and they are different in kind: the first binds the COLLECTION itself through the ordinary
	// addMapping route (no new operation exists for it), the second addresses ONE PER-ITEM value through a
	// dotted name on both sides.
	private static string BuildMultiInstanceCallerDescriptor(string processName, string calleeName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP multi-instance caller E2E",
		  "packageName": "Custom",
		  "elements": [
		    { "name": "StartEvent1", "type": "startEvent" },
		    { "name": "ReadData1", "type": "readData", "caption": "Read contacts",
		      "readData": { "source": "Contact", "mode": "collection", "columns": ["Name"], "numberOfRecords": 3 } },
		    { "name": "SubProcess1", "type": "subProcess", "caption": "Call the callee once per contact",
		      "subProcess": {
		        "processName": "{{calleeName}}",
		        "multiInstanceOptions": { "enabled": true, "executionMode": "Sequential", "ignoreErrors": false }
		      } },
		    { "name": "EndEvent1", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "StartEvent1", "target": "ReadData1" },
		    { "source": "ReadData1", "target": "SubProcess1" },
		    { "source": "SubProcess1", "target": "EndEvent1" }
		  ],
		  "mappings": [
		    { "elementName": "SubProcess1", "elementParameter": "InputRecordCollection",
		      "sourceElement": "ReadData1", "sourceElementParameter": "ResultCompositeObjectList" },
		    { "elementName": "SubProcess1", "elementParameter": "InputRecordCollection.ItemName",
		      "sourceElement": "ReadData1", "sourceElementParameter": "ResultCompositeObjectList.Name" }
		  ]
		}
		""";

	// The same graph with the second mapping aimed at the OUTPUT collection instead - at one of its items or at
	// the collection itself, depending on the case. Everything else is identical, so a failure here is about the
	// target and nothing else.
	private static string BuildOutputCollectionTargetDescriptor(string processName, string calleeName,
			string target, string source) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP multi-instance output-target E2E",
		  "packageName": "Custom",
		  "elements": [
		    { "name": "StartEvent1", "type": "startEvent" },
		    { "name": "ReadData1", "type": "readData", "caption": "Read contacts",
		      "readData": { "source": "Contact", "mode": "collection", "columns": ["Name"], "numberOfRecords": 3 } },
		    { "name": "SubProcess1", "type": "subProcess", "caption": "Call the callee once per contact",
		      "subProcess": {
		        "processName": "{{calleeName}}",
		        "multiInstanceOptions": { "enabled": true }
		      } },
		    { "name": "EndEvent1", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "StartEvent1", "target": "ReadData1" },
		    { "source": "ReadData1", "target": "SubProcess1" },
		    { "source": "SubProcess1", "target": "EndEvent1" }
		  ],
		  "mappings": [
		    { "elementName": "SubProcess1", "elementParameter": "InputRecordCollection",
		      "sourceElement": "ReadData1", "sourceElementParameter": "ResultCompositeObjectList" },
		    { "elementName": "SubProcess1", "elementParameter": "{{target}}",
		      "sourceElement": "ReadData1", "sourceElementParameter": "{{source}}" }
		  ]
		}
		""";

	// A second callee with a DIFFERENT contract, so a retarget onto it is visible one level down: none of its
	// parameter names is shared with the first callee's.
	private static string BuildOtherCalleeDescriptor(string processName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP multi-instance second callee E2E",
		  "packageName": "Custom",
		  "parameters": [
		    { "name": "OtherName", "type": "Text", "direction": "In" },
		    { "name": "OtherEchoed", "type": "Text", "direction": "Out" }
		  ],
		  "elements": [
		    { "name": "StartEvent1", "type": "startEvent" },
		    { "name": "EndEvent1", "type": "endEvent" }
		  ],
		  "flows": [ { "source": "StartEvent1", "target": "EndEvent1" } ]
		}
		""";

	// The happy-path graph plus the two ways to CONSUME the iteration results, which is what makes the five
	// service parameters reference sites: a second multi-instance element that iterates SubProcess1's OUTPUT
	// collection (the shape the stand verification ran), and a process parameter that reads one of its
	// COUNTERS. Both read the ROOT of SubProcess1, never one of its items.
	private static string BuildReadersCallerDescriptor(string processName, string calleeName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP multi-instance readers E2E",
		  "packageName": "Custom",
		  "parameters": [
		    { "name": "IterationsTotal", "type": "Integer", "direction": "Out" }
		  ],
		  "elements": [
		    { "name": "StartEvent1", "type": "startEvent" },
		    { "name": "ReadData1", "type": "readData", "caption": "Read contacts",
		      "readData": { "source": "Contact", "mode": "collection", "columns": ["Name"], "numberOfRecords": 3 } },
		    { "name": "SubProcess1", "type": "subProcess", "caption": "Call the callee once per contact",
		      "subProcess": {
		        "processName": "{{calleeName}}",
		        "multiInstanceOptions": { "enabled": true, "executionMode": "Sequential", "ignoreErrors": false }
		      } },
		    { "name": "SubProcess2", "type": "subProcess", "caption": "Call the callee once per result",
		      "subProcess": {
		        "processName": "{{calleeName}}",
		        "multiInstanceOptions": { "enabled": true }
		      } },
		    { "name": "EndEvent1", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "StartEvent1", "target": "ReadData1" },
		    { "source": "ReadData1", "target": "SubProcess1" },
		    { "source": "SubProcess1", "target": "SubProcess2" },
		    { "source": "SubProcess2", "target": "EndEvent1" }
		  ],
		  "mappings": [
		    { "elementName": "SubProcess1", "elementParameter": "InputRecordCollection",
		      "sourceElement": "ReadData1", "sourceElementParameter": "ResultCompositeObjectList" },
		    { "elementName": "SubProcess1", "elementParameter": "InputRecordCollection.ItemName",
		      "sourceElement": "ReadData1", "sourceElementParameter": "ResultCompositeObjectList.Name" },
		    { "elementName": "SubProcess2", "elementParameter": "InputRecordCollection",
		      "sourceElement": "SubProcess1", "sourceElementParameter": "OutputRecordCollection" },
		    { "targetProcessParameter": "IterationsTotal",
		      "sourceElement": "SubProcess1", "sourceElementParameter": "TotalIterationsCount" }
		  ]
		}
		""";

	#endregion

	#region Methods: Arrange

	/// <summary>
	/// Builds a process for an ARRANGE step and fails the test on the spot if it did not build.
	/// <para>It asserts the SUCCESS LINE, not <c>IsError</c>: a refused or failed build comes back as an
	/// ordinary result carrying a non-zero exit code, and nothing on the MCP path turns that into
	/// <c>IsError</c>. An arrange guard that checked only the flag would pass the exact failure it exists to
	/// catch, and the callee's absence would then read as a defect in the conversion.</para>
	/// </summary>
	private static async Task<CallToolResult> ArrangeProcessAsync(ArrangeContext context, string descriptor,
			string what) {
		CallToolResult result = await CreateAsync(context, descriptor);
		result.IsError.Should().NotBeTrue(
			because: $"the {what} is an arrange step - if it did not build, every assertion below is about the "
				+ "wrong failure");
		JsonSerializer.Serialize(result).Should().Contain("created (UId:",
			because: $"only a genuinely successful build logs the created-schema line, and the {what} has to "
				+ "exist before the case below means anything");
		return result;
	}

	private static async Task<CallToolResult> CreateAsync(ArrangeContext context, string descriptor) =>
		await CallToolAsync(context, ToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = descriptor
		});

	private static async Task<DescribeProcessResult> DescribeAsync(ArrangeContext context, string processName) =>
		ParseDescribeGraph(await CallToolAsync(context, DescribeToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = processName
		}));

	/// <summary>
	/// Describes the process and returns one element, failing on the spot unless it is multi-instance - every
	/// assertion made on such an element is about the arrange otherwise.
	/// </summary>
	private static async Task<DescribedElement> DescribeMultiInstanceElementAsync(ArrangeContext context,
			string processName, string elementName) {
		DescribedElement element = (await DescribeAsync(context, processName)).Elements
			.Single(candidate => candidate.Name == elementName);
		element.SubProcess.Should().NotBeNull(
			because: $"'{elementName}' is a sub-process element, and a null block is the silent-drop signature");
		element.SubProcess!.MultiInstance.Should().Be(true,
			because: $"'{elementName}' has to BE multi-instance for anything asserted about it here to mean anything");
		element.SubProcess.MultiInstanceOptions.Should().NotBeNull(
			because: "the options block is emitted on every multi-instance element");
		return element;
	}

	/// <summary>
	/// Reads the two reader sites the readers graph builds, and fails on the spot unless both address the
	/// SubProcess1 service parameter they are supposed to read - by element AND by parameter UId.
	/// </summary>
	private static async Task<ReaderSites> DescribeReaderSitesAsync(ArrangeContext context, string processName) {
		DescribeProcessResult graph = await DescribeAsync(context, processName);
		DescribedElement source = graph.Elements.Single(candidate => candidate.Name == "SubProcess1");
		DescribedElement collectionReader = graph.Elements.Single(candidate => candidate.Name == "SubProcess2");
		Dictionary<string, string> uids = ServiceParameterUIds(source);
		string collectionReaderValue = collectionReader.Parameters
			.Single(parameter => parameter.Name == "InputRecordCollection").Value;
		string counterReaderValue = graph.Parameters
			.Single(parameter => parameter.Name == "IterationsTotal").Value;
		collectionReaderValue.Should().ContainEquivalentOf(ElementSegment(source),
			because: "SubProcess2 iterates SubProcess1's results, so its input has to name SubProcess1");
		collectionReaderValue.Should().ContainEquivalentOf(ParameterSegment(uids["OutputRecordCollection"]),
			because: "and it has to read the OUTPUT collection by its UId - which is what makes that UId a "
				+ "reference site");
		counterReaderValue.Should().ContainEquivalentOf(ElementSegment(source),
			because: "the process parameter reads a counter of SubProcess1, so it has to name SubProcess1");
		counterReaderValue.Should().ContainEquivalentOf(ParameterSegment(uids["TotalIterationsCount"]),
			because: "and it has to read TotalIterationsCount by its UId");
		return new ReaderSites(uids, collectionReaderValue, counterReaderValue);
	}

	/// <summary>The name-to-UId map of the five service parameters, failing unless exactly those five are present.</summary>
	private static Dictionary<string, string> ServiceParameterUIds(DescribedElement element) {
		element.Parameters.Select(parameter => parameter.Name).Should().BeEquivalentTo(ServiceParameterNames,
			because: $"'{element.Name}' is multi-instance, so it carries exactly the five service parameters");
		return ServiceParameterNames.ToDictionary(name => name,
			name => element.Parameters.Single(parameter => parameter.Name == name).UId);
	}

	/// <summary>The stored value of one item property of one collection, failing unless the item exists.</summary>
	private static string ItemValue(DescribedElement element, string collectionName, string itemName) {
		DescribedParameter? collection = element.Parameters
			.SingleOrDefault(parameter => parameter.Name == collectionName);
		collection.Should().NotBeNull(because: $"'{collectionName}' has to be on '{element.Name}'");
		DescribedParameter? item = collection!.ItemProperties?
			.SingleOrDefault(candidate => candidate.Name == itemName);
		item.Should().NotBeNull(
			because: $"'{itemName}' has to be an item property of '{collectionName}' - the callee's contract "
				+ "lives there on a multi-instance element");
		return item!.Value;
	}

	/// <summary>The metapath segment that names <paramref name="element"/>.</summary>
	private static string ElementSegment(DescribedElement element) =>
		"[Element:{" + Guid.Parse(element.Uid).ToString("D") + "}]";

	/// <summary>The metapath segment that names the parameter with <paramref name="parameterUId"/>.</summary>
	private static string ParameterSegment(string parameterUId) =>
		"[Parameter:{" + Guid.Parse(parameterUId).ToString("D") + "}]";

	/// <summary>Every execution-log message of a tool result, one per line - the text a notice or refusal is in.</summary>
	private static string LogText(CallToolResult result) =>
		string.Join("\n", (McpCommandExecutionParser.Extract(result).Output ?? [])
			.Select(message => message.Value ?? string.Empty));

	private static async Task<CallToolResult> ModifyAsync(ArrangeContext context, string processName,
			string operations) =>
		await CallToolAsync(context, ModifyToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = processName,
			["operations"] = operations
		});

	/// <summary>An ARRANGE edit, failing the test on the spot unless it landed.</summary>
	private static async Task ModifyAndExpectSuccessAsync(ArrangeContext context, string processName,
			string operations, string what) {
		CallToolResult result = await ModifyAsync(context, processName, operations);
		ShouldHaveLanded(result,
			because: $"{what} is an arrange step - if it did not land, every assertion below is about the wrong "
				+ "failure");
	}

	/// <summary>
	/// Asserts a write LANDED - exit code 0, not merely no transport error - and carries the server's own log
	/// into the failure message, because a bare "expected 0, found 1" names no reason and the reason is the
	/// whole diagnosis on a live stand.
	/// </summary>
	private static void ShouldHaveLanded(CallToolResult result, string because) =>
		McpCommandExecutionParser.Extract(result).ExitCode.Should().Be(0,
			because.Replace("{", "{{", StringComparison.Ordinal).Replace("}", "}}", StringComparison.Ordinal)
				+ ". The server said: {0}",
			LogText(result));

	/// <summary>The exit code of a describe call, for the cases that expect it to FAIL.</summary>
	private static async Task<int> DescribeExitCodeAsync(ArrangeContext context, string processName) =>
		McpCommandExecutionParser.Extract(await CallToolAsync(context, DescribeToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["process-name"] = processName
			})).ExitCode;

	private static DescribeProcessResult ParseDescribeGraph(CallToolResult describeResult) {
		CommandExecutionEnvelope envelope = McpCommandExecutionParser.Extract(describeResult);
		string graphJson = envelope.Output!
			.Select(message => message.Value)
			.First(value => !string.IsNullOrWhiteSpace(value)
				&& value!.TrimStart().StartsWith("{", StringComparison.Ordinal))!;
		return JsonSerializer.Deserialize<DescribeProcessResult>(graphJson,
			new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
	}

	private static async Task<CallToolResult> CallToolAsync(ArrangeContext context, string toolName,
			Dictionary<string, object?> args) {
		IReadOnlyCollection<string> toolNames =
			await context.Session.ListReachableToolNamesAsync(context.CancellationTokenSource.Token);
		toolNames.Should().Contain(toolName,
			because: "the tool must be discoverable before the end-to-end call");
		return await context.Session.CallToolAsync(
			toolName, new Dictionary<string, object?> { ["args"] = args }, context.CancellationTokenSource.Token);
	}

	private static async Task<ArrangeContext> ArrangeAsync() {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string? environmentName = settings.Sandbox.EnvironmentName;
		if (string.IsNullOrWhiteSpace(environmentName)) {
			Assert.Ignore(
				"Configure McpE2E:Sandbox:EnvironmentName (with a CrtProcessBuilder of at least 1.6.6.13) to run "
				+ "the multi-instance Sub-process MCP E2E tests.");
		}

		if (!await ClioCliCommandRunner.IsEnvironmentReachableAsync(settings, environmentName!)) {
			Assert.Ignore(
				$"Multi-instance Sub-process MCP E2E requires a reachable configured sandbox environment. '{environmentName}' was not reachable.");
		}

		CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(5));
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		return new ArrangeContext(session, cancellationTokenSource, environmentName);
	}

	/// <summary>What the readers graph's two reader sites hold, and the UIds they are supposed to hold.</summary>
	private sealed record ReaderSites(
		Dictionary<string, string> ServiceParameterUIds,
		string CollectionReaderValue,
		string CounterReaderValue);

	private sealed record ArrangeContext(
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource,
		string EnvironmentName) : IAsyncDisposable {

		public async ValueTask DisposeAsync() {
			await Session.DisposeAsync();
			CancellationTokenSource.Dispose();
		}
	}

	#endregion

}
