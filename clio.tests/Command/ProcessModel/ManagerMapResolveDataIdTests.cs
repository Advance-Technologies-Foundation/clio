using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Clio.Command.ProcessModel;
using Clio.Common;
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
	[TestCase("sendEmail", ManagerMap.EventType.UserTask)]
	[TestCase("approvalUserTask", ManagerMap.EventType.UserTask)]
	[TestCase("approval", ManagerMap.EventType.UserTask)]
	[TestCase("openEditPageUserTask", ManagerMap.EventType.UserTask)]
	// The dedicated build token, which does NOT end with the "usertask" suffix the fallback arm matches on — so a
	// missing explicit entry would resolve a VALID graph to Unknown and validate-process-graph would reject it.
	[TestCase("openEditPage", ManagerMap.EventType.UserTask)]
	[TestCase("formulaTask", ManagerMap.EventType.FormulaTask)]
	[TestCase("scriptTask", ManagerMap.EventType.ScriptTask)]
	[TestCase("webService", ManagerMap.EventType.WebServiceTask)]
	[TestCase("callActivity", ManagerMap.EventType.SubProcess)]
	// The BUILD token for the same element, and the same trap as openEditPage above: "subprocess" does not end in
	// the "usertask" suffix, so without an explicit arm a graph carrying an element create-business-process builds
	// correctly resolves to Unknown and validate-process-graph reports a hard Error on it.
	[TestCase("subProcess", ManagerMap.EventType.SubProcess)]
	[TestCase("subprocess", ManagerMap.EventType.SubProcess)]
	// The Pre-configured page build token, for the same reason: its data-id PreconfiguredPageUserTask resolves
	// through the suffix arm while the token itself did not, so a graph containing an element the server
	// builds happily was reported as UNKNOWN by validate-process-graph.
	[TestCase("preconfiguredpage", ManagerMap.EventType.UserTask)]
	[TestCase("preconfiguredPage", ManagerMap.EventType.UserTask)]
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
	[TestCase("readData", ManagerMap.EventType.UserTask)]            // build token: the data-id ends in UserTask, the token does not
	[TestCase("changeData", ManagerMap.EventType.UserTask)]          // build token for the Modify data element
	[TestCase("changeAccessRights", ManagerMap.EventType.UserTask)]  // build token for Change access rights (ENG-92717)
	[TestCase("changeaccessrights", ManagerMap.EventType.UserTask)]  // lowercase build/describe spelling
	[TestCase("deleteData", ManagerMap.EventType.UserTask)]          // build token for Delete data - Unknown until ENG-95244
	[TestCase("deletedata", ManagerMap.EventType.UserTask)]          // lowercase build/describe spelling
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

	// The element type tokens the SERVER builds, copied from CrtProcessBuilder
	// Files/src/cs/ProcessDesignConstants.cs, class ElementTypes (the archive bundled in
	// clio/CrtProcessBuilder/CrtProcessBuilder.gz, 1.6.6.23). The server resolves a descriptor `type` through
	// the handlers' SupportedTypes, case-insensitively (ProcessElementFactory), so this list IS the build
	// vocabulary. A copy on its own would not notice a token the package ADDS, and that is how deletedata sat at
	// Unknown while earlier tokens were pinned one incident at a time - so
	// ServerBuildTokens_ShouldMatchTheBundledPackage compares the copy with the archive, and the rebundle that
	// brings a new token fails there, which is the moment someone has to add its ResolveDataId arm.
	private static readonly string[] ServerBuildTokens = [
		"startevent", "signalstart", "endevent", "usertask", "readdata", "changedata", "deletedata", "adddata",
		"changeaccessrights", "performtask", "formulatask", "sendemail", "approval", "openeditpage",
		"preconfiguredpage", "exclusivegateway", "parallelgateway", "subprocess"
	];

	[Test]
	[Description("EVERY element type token the server builds (ProcessDesignConstants.ElementTypes) resolves to a known EventType, and ManagerMap.IsBuildable accepts it. One missing arm is a hard UNKNOWN error from validate-process-graph on a graph the server builds correctly - the false red ENG-95244 found for deleteData after addData, preconfiguredPage and subProcess had each been found the same way.")]
	[TestCaseSource(nameof(ServerBuildTokens))]
	public void ResolveDataId_ShouldResolveToABuildableKind_WhenTheTokenIsOneTheServerBuilds(string serverToken) {
		// Arrange
		string spelledAsInAGuide = serverToken;

		// Act
		ManagerMap.EventType eventType = ManagerMap.ResolveDataId(spelledAsInAGuide);
		bool buildable = ManagerMap.IsBuildable(eventType);

		// Assert
		eventType.Should().NotBe(ManagerMap.EventType.Unknown,
			because: $"'{serverToken}' is a token CrtProcessBuilder builds, so the validator must recognize it "
				+ "or it reports a hard error on a correct graph");
		buildable.Should().BeTrue(
			because: $"'{serverToken}' is built by the server, so the UNBUILDABLE marker must never fire on it");
	}

	[Test]
	[Description("The ServerBuildTokens copy above equals the ElementTypes constants in the CrtProcessBuilder archive this clio bundles. Reads the bundled archive from the test output, as BundledProcessBuilderPackageTests does and for its reason: the file ships with the build, and a guard only in the integration lane would not guard the rebundle that breaks it.")]
	// Module=Common as well as the fixture's ProcessModel: this pins the bundled ARCHIVE, and the rebundle's own
	// validation runs Module=Common (beside BundledProcessBuilderPackageTests); a ProcessModel-only tag would
	// leave it out of exactly the run that brings the new token.
	[Property("Module", "Common")]
	public void ServerBuildTokens_ShouldMatchTheBundledPackage_WhenTheArchiveIsRebundled() {
		// Arrange
		string archive = ReadBundledProcessDesignConstants();
		Match elementTypes = Regex.Match(archive,
			@"internal static class ElementTypes\s*\{(?<body>.*?)\n\t\t\}", RegexOptions.Singleline);

		// Act
		List<string> archiveTokens = Regex.Matches(elementTypes.Groups["body"].Value,
				@"public const string \w+ = ""(?<token>[^""]+)"";")
			.Select(match => match.Groups["token"].Value)
			.ToList();

		// Assert
		elementTypes.Success.Should().BeTrue(
			because: "the archive ships SOURCE, and ProcessDesignConstants.ElementTypes is where the package "
				+ "declares its build tokens - if the anchor moved, this guard has to move with it");
		archiveTokens.Should().BeEquivalentTo(ServerBuildTokens,
			because: "a token the package adds must reach ServerBuildTokens, and through it the ResolveDataId "
				+ "and IsBuildable pins above, in the same change as the rebundle that ships it");
	}

	// Through the production container reader, not a text scan of the whole archive: the constant lives in ONE
	// file, and reading that file is what keeps a same-named class elsewhere in the archive out of the match.
	private static string ReadBundledProcessDesignConstants() {
		string archivePath = Path.Combine(AppContext.BaseDirectory, BundledPackages.ProcessBuilderPackageName,
			BundledPackages.ProcessBuilderArchiveFileName);
		IFileSystem fileSystem = new FileSystem(new System.IO.Abstractions.FileSystem());
		ICompressionUtilities compression = new CompressionUtilities(fileSystem, new ZipFileWrapper());
		compression.TryReadFileFromGZip(archivePath, "Files/src/cs/ProcessDesignConstants.cs", out byte[] content)
			.Should().BeTrue(because: "the package declares its build tokens in ProcessDesignConstants.cs");
		return Encoding.UTF8.GetString(content);
	}

	[Test]
	[Description("The other direction of the IsBuildable pin: every kind it accepts is produced by at least one token the server builds. A kind accepted here with no server token would stop the UNBUILDABLE marker on an element the server still refuses.")]
	public void IsBuildable_ShouldAcceptOnlyKindsAServerTokenProduces_WhenEveryEventTypeIsAsked() {
		// Arrange
		HashSet<ManagerMap.EventType> producedByServerTokens =
			ServerBuildTokens.Select(ManagerMap.ResolveDataId).ToHashSet();

		// Act
		List<ManagerMap.EventType> acceptedWithoutAToken = Enum.GetValues<ManagerMap.EventType>()
			.Where(ManagerMap.IsBuildable)
			.Where(kind => !producedByServerTokens.Contains(kind))
			.ToList();

		// Assert
		acceptedWithoutAToken.Should().BeEmpty(
			because: "IsBuildable must describe what the server builds - a kind with no server token behind it is a "
				+ "promise the build refuses");
	}

	[Test]
	[Description("IsBuildable refuses the element kinds the build has no handler for - inclusive and event-based gateways, timer and message starts, intermediate events, script and web-service tasks, the event sub-process - and Unknown. These are the kinds the UNBUILDABLE marker names.")]
	[TestCase(ManagerMap.EventType.InclusiveGateway)]
	[TestCase(ManagerMap.EventType.EventBasedGateway)]
	[TestCase(ManagerMap.EventType.StartTimer)]
	[TestCase(ManagerMap.EventType.StartMessageEvent)]
	[TestCase(ManagerMap.EventType.IntermediateCatchSignalEvent)]
	[TestCase(ManagerMap.EventType.IntermediateThrowSignalEvent)]
	[TestCase(ManagerMap.EventType.ScriptTask)]
	[TestCase(ManagerMap.EventType.WebServiceTask)]
	[TestCase(ManagerMap.EventType.EventSubProcess)]
	[TestCase(ManagerMap.EventType.Unknown)]
	public void IsBuildable_ShouldReturnFalse_WhenTheBuildHasNoHandlerForTheKind(ManagerMap.EventType eventType) {
		// Arrange
		ManagerMap.EventType kind = eventType;

		// Act
		bool buildable = ManagerMap.IsBuildable(kind);

		// Assert
		buildable.Should().BeFalse(
			because: $"create-business-process refuses a {kind} element as 'not supported yet', so calling it "
				+ "buildable would promise a build that fails");
	}
}
