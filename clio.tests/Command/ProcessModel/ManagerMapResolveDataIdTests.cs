using Clio.Command.ProcessModel;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.ProcessModel;

/// <summary>
/// Unit tests for <see cref="ManagerMap.ResolveDataId"/> and <see cref="ManagerMap.ResolveRole"/>
/// — the single source of truth mapping designer <c>data-id</c> strings to the element taxonomy.
/// </summary>
[TestFixture]
[Property("Module", "ProcessModel")]
[Category("Unit")]
public sealed class ManagerMapResolveDataIdTests {

	[Test]
	[Category("Unit")]
	[Description("ResolveDataId maps every start-event data-id to its matching start EventType.")]
	[TestCase("startEvent", ManagerMap.EventType.StartEvent)]
	[TestCase("startEventSignal", ManagerMap.EventType.StartSignalEvent)]
	[TestCase("startEventTimer", ManagerMap.EventType.StartTimer)]
	[TestCase("startEventMessage", ManagerMap.EventType.StartMessageEvent)]
	public void ResolveDataId_ShouldReturnStartEventType_WhenStartDataId(string dataId, ManagerMap.EventType expected) {
		// Act
		ManagerMap.EventType actual = ManagerMap.ResolveDataId(dataId);

		// Assert
		actual.Should().Be(expected,
			because: "start-event data-ids must classify as their concrete start EventType for rule R1");
	}

	[Test]
	[Category("Unit")]
	[Description("ResolveDataId maps the shared endEvent data-id to EventType.EndEvent (Simple end and Terminate share it).")]
	public void ResolveDataId_ShouldReturnEndEvent_WhenEndDataId() {
		// Act
		ManagerMap.EventType actual = ManagerMap.ResolveDataId("endEvent");

		// Assert
		actual.Should().Be(ManagerMap.EventType.EndEvent,
			because: "Simple end and Terminate share the endEvent data-id and must classify as EndEvent");
	}

	[Test]
	[Category("Unit")]
	[Description("ResolveDataId maps activity data-ids (data ops, user tasks, formula, script, web service, sub-process) to activity EventTypes.")]
	[TestCase("readDataUserTask", ManagerMap.EventType.UserTask)]
	[TestCase("addDataUserTask", ManagerMap.EventType.UserTask)]
	[TestCase("changeDataUserTask", ManagerMap.EventType.UserTask)]
	[TestCase("deleteDataUserTask", ManagerMap.EventType.UserTask)]
	[TestCase("activityUserTask", ManagerMap.EventType.UserTask)]
	[TestCase("userTask", ManagerMap.EventType.UserTask)]
	[TestCase("emailTemplateUserTask", ManagerMap.EventType.UserTask)]
	[TestCase("formulaTask", ManagerMap.EventType.FormulaTask)]
	[TestCase("scriptTask", ManagerMap.EventType.ScriptTask)]
	[TestCase("webService", ManagerMap.EventType.WebServiceTask)]
	[TestCase("callActivity", ManagerMap.EventType.SubProcess)]
	[TestCase("eventSubProcessExpanded", ManagerMap.EventType.EventSubProcess)]
	public void ResolveDataId_ShouldReturnActivityEventType_WhenActivityDataId(string dataId, ManagerMap.EventType expected) {
		// Act
		ManagerMap.EventType actual = ManagerMap.ResolveDataId(dataId);

		// Assert
		actual.Should().Be(expected,
			because: "activity data-ids must classify as their activity EventType so the validator treats them as tasks");
	}

	[Test]
	[Category("Unit")]
	[Description("ResolveDataId maps every gateway data-id to its matching gateway EventType.")]
	[TestCase("exclusiveGateway", ManagerMap.EventType.ExclusiveGateway)]
	[TestCase("parallelGateway", ManagerMap.EventType.ParallelGateway)]
	[TestCase("inclusiveGateway", ManagerMap.EventType.InclusiveGateway)]
	[TestCase("eventBasedGateway", ManagerMap.EventType.EventBasedGateway)]
	public void ResolveDataId_ShouldReturnGatewayEventType_WhenGatewayDataId(string dataId, ManagerMap.EventType expected) {
		// Act
		ManagerMap.EventType actual = ManagerMap.ResolveDataId(dataId);

		// Assert
		actual.Should().Be(expected,
			because: "gateway data-ids must classify as their gateway EventType for the split/merge rules R7–R11");
	}

	[Test]
	[Category("Unit")]
	[Description("ResolveDataId maps intermediate catch/throw event prefixes to the corresponding intermediate EventType.")]
	[TestCase("intermediateCatchEventSignal", ManagerMap.EventType.IntermediateCatchSignalEvent)]
	[TestCase("intermediateCatchEventTimer", ManagerMap.EventType.IntermediateCatchSignalEvent)]
	[TestCase("intermediateThrowEvent", ManagerMap.EventType.IntermediateThrowSignalEvent)]
	[TestCase("intermediateThrowEventMessage", ManagerMap.EventType.IntermediateThrowSignalEvent)]
	public void ResolveDataId_ShouldReturnIntermediateEventType_WhenIntermediatePrefix(string dataId, ManagerMap.EventType expected) {
		// Act
		ManagerMap.EventType actual = ManagerMap.ResolveDataId(dataId);

		// Assert
		actual.Should().Be(expected,
			because: "intermediate catch/throw prefixes must classify as an intermediate EventType for rule R10");
	}

	[Test]
	[Category("Unit")]
	[Description("ResolveDataId returns EventType.Unknown for unrecognized or empty data-ids and never throws.")]
	[TestCase("totallyUnknownThing")]
	[TestCase("")]
	[TestCase(null)]
	public void ResolveDataId_ShouldReturnUnknown_WhenDataIdUnrecognized(string dataId) {
		// Act
		ManagerMap.EventType actual = ManagerMap.ResolveDataId(dataId);

		// Assert
		actual.Should().Be(ManagerMap.EventType.Unknown,
			because: "an unrecognized data-id must resolve to Unknown so the validator surfaces a finding rather than crashing");
	}

	[Test]
	[Category("Unit")]
	[Description("ResolveDataId is case-insensitive and accepts the lowercase build/describe tokens (signalstart/usertask/endevent/startevent), not just the camelCase canvas data-ids, so the validate <-> create-business-process <-> describe-business-process loop round-trips (PR #715 vocabulary reconciliation). These cases all resolved to Unknown before the fix.")]
	[TestCase("startevent", ManagerMap.EventType.StartEvent)]          // build/describe lowercase token
	[TestCase("signalstart", ManagerMap.EventType.StartSignalEvent)]  // build/describe token for the run-on-save start
	[TestCase("signalStart", ManagerMap.EventType.StartSignalEvent)]  // create-business-process descriptor `type`
	[TestCase("endevent", ManagerMap.EventType.EndEvent)]             // build/describe lowercase token
	[TestCase("usertask", ManagerMap.EventType.UserTask)]             // build/describe lowercase token
	[TestCase("performtask", ManagerMap.EventType.UserTask)]          // build token the guidance example uses; the server builds it (alias -> Perform task)
		[TestCase("performTask", ManagerMap.EventType.UserTask)]          // create-business-process descriptor `type`
		[TestCase("StartEvent", ManagerMap.EventType.StartEvent)]         // case-insensitive vs the canvas data-id
	[TestCase("ENDEVENT", ManagerMap.EventType.EndEvent)]             // case-insensitive
	[TestCase("ReadDataUserTask", ManagerMap.EventType.UserTask)]     // *UserTask suffix, mixed case
	public void ResolveDataId_ShouldAcceptBuildAndDescribeTokensCaseInsensitively_WhenVocabularyOrCaseDrifts(
			string token, ManagerMap.EventType expected) {
		// Act
		ManagerMap.EventType actual = ManagerMap.ResolveDataId(token);

		// Assert
		actual.Should().Be(expected,
			because: "the validator must accept the build/describe tokens and any casing so the three surfaces share one vocabulary — otherwise a valid graph (or a describe read-back) degrades to all-Unknown");
	}

	[Test]
	[Category("Unit")]
	[Description("ResolveRole collapses each EventType into the coarse role (Start/End/Activity/Gateway/Intermediate/Other) the rules need.")]
	[TestCase(ManagerMap.EventType.StartSignalEvent, ManagerMap.ProcessElementRole.Start)]
	[TestCase(ManagerMap.EventType.StartTimer, ManagerMap.ProcessElementRole.Start)]
	[TestCase(ManagerMap.EventType.EndEvent, ManagerMap.ProcessElementRole.End)]
	[TestCase(ManagerMap.EventType.TerminateEvent, ManagerMap.ProcessElementRole.End)]
	[TestCase(ManagerMap.EventType.UserTask, ManagerMap.ProcessElementRole.Activity)]
	[TestCase(ManagerMap.EventType.FormulaTask, ManagerMap.ProcessElementRole.Activity)]
	[TestCase(ManagerMap.EventType.SubProcess, ManagerMap.ProcessElementRole.Activity)]
	[TestCase(ManagerMap.EventType.ExclusiveGateway, ManagerMap.ProcessElementRole.Gateway)]
	[TestCase(ManagerMap.EventType.ParallelGateway, ManagerMap.ProcessElementRole.Gateway)]
	[TestCase(ManagerMap.EventType.IntermediateCatchSignalEvent, ManagerMap.ProcessElementRole.Intermediate)]
	[TestCase(ManagerMap.EventType.SequenceFlow, ManagerMap.ProcessElementRole.Other)]
	[TestCase(ManagerMap.EventType.Unknown, ManagerMap.ProcessElementRole.Other)]
	public void ResolveRole_ShouldCollapseToCoarseRole_WhenEventTypeGiven(ManagerMap.EventType eventType, ManagerMap.ProcessElementRole expected) {
		// Act
		ManagerMap.ProcessElementRole actual = ManagerMap.ResolveRole(eventType);

		// Assert
		actual.Should().Be(expected,
			because: "the validator operates on coarse roles, so each EventType must map to exactly one role");
	}
}
