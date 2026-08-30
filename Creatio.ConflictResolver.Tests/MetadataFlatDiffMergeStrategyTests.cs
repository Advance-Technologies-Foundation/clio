using Creatio.ConflictResolver.Tests.TestSupport;
using System.Text.Json.Nodes;

namespace Creatio.ConflictResolver.Tests;

[TestFixture, Category("Unit")]
public class MetadataFlatDiffMergeStrategyTests
{
	[Test]
	public void MetadataMerge_FlatFixture_ResolvesAndContainsBothBranchAdditions()
	{
		var baseContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("MetadataCase1", "metadatabase1.json"));
		var localContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("MetadataCase1", "metadatalocal1.json"));
		var remoteContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("MetadataCase1", "metadataremote1.json"));

        var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
            global::Creatio.ConflictResolver.ConflictFileType.MetadataJson,
            baseContent,
            localContent,
            remoteContent));

        Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.Resolved), result.ErrorMessage);
		Assert.That(result.MergedContent, Does.Contain("e312ae6d-1dec-f83b-2cb3-00e848193bab"));
		Assert.That(result.MergedContent, Does.Contain("efb74a9c-5527-8600-0930-5b085d15075e"));
	}

	[Test]
	public void MetadataMerge_FlatFixture_MatchesExpectedMergedResult()
	{
		var baseContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("MetadataCase1", "metadatabase1.json"));
		var localContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("MetadataCase1", "metadatalocal1.json"));
		var remoteContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("MetadataCase1", "metadataremote1.json"));
		var expected = File.ReadAllText(ResolverTestSupport.GetFixturePath("MetadataCase1", "metadatamergedResult1.json"));

		var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
			global::Creatio.ConflictResolver.ConflictFileType.MetadataJson,
			baseContent,
			localContent,
			remoteContent));

		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.Resolved), result.ErrorMessage);
		Assert.That(
			result.MergedContent!,
			Is.EqualTo(expected));
	}
	
	[Test]
	public void MetadataMerge_FlatFixture2_MatchesExpectedMergedResult()
	{
		var baseContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("MetadataCase2", "metadatabase.json"));
		var localContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("MetadataCase2", "metadatalocal.json"));
		var remoteContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("MetadataCase2", "metadataremote.json"));
		var expected = File.ReadAllText(ResolverTestSupport.GetFixturePath("MetadataCase2", "metadatamerged.json"));

		var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
			global::Creatio.ConflictResolver.ConflictFileType.MetadataJson,
			baseContent,
			localContent,
			remoteContent));

		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.Resolved), result.ErrorMessage);
		Assert.That(
			result.MergedContent!,
			Is.EqualTo(expected));
	}
	
	[Test]
	public void MetadataMerge_FlatFixture2_MatchesExpectedMergedResultLocalAndRemoteChanged()
	{
		var baseContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("MetadataCase2", "metadatabase.json"));
		var localContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("MetadataCase2", "metadatalocal.json"));
		var remoteContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("MetadataCase2", "metadataremote.json"));
		var expected = File.ReadAllText(ResolverTestSupport.GetFixturePath("MetadataCase2", "metadatamerged.json"));

		var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
			global::Creatio.ConflictResolver.ConflictFileType.MetadataJson,
			baseContent,
			localContent,
			remoteContent));

		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.Resolved), result.ErrorMessage);
		Assert.That(
			result.MergedContent!,
			Is.EqualTo(expected));
	}
	
	[Test]
	public void MetadataMerge_FlatFixture3_WithDeletions()
	{
		var baseContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("MetadataCase3", "base.json"));
		var localContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("MetadataCase3", "local.json"));
		var remoteContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("MetadataCase3", "remote.json"));
		var expected = File.ReadAllText(ResolverTestSupport.GetFixturePath("MetadataCase3", "resolved.json"));

		var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
			global::Creatio.ConflictResolver.ConflictFileType.MetadataJson,
			baseContent,
			localContent,
			remoteContent));

		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.Resolved), result.ErrorMessage);
		Assert.That(
			result.MergedContent!,
			Is.EqualTo(expected));
	}

	[Test]
	public void MetadataMerge_FlatFixture4_LogicalConflict()
	{
		var baseContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("MetaDataCase4", "base.json"));
		var localContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("MetaDataCase4", "local.json"));
		var remoteContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("MetaDataCase4", "remote.json"));
		var expected = File.ReadAllText(ResolverTestSupport.GetFixturePath("MetaDataCase4", "resolved.json"));

		var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
			global::Creatio.ConflictResolver.ConflictFileType.MetadataJson,
			baseContent,
			localContent,
			remoteContent));

		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.Resolved), result.ErrorMessage);
		Assert.That(result.Report.WinnerPolicy, Is.EqualTo("LOCAL"));
		Assert.That(result.Report.LocalAdditions, Is.Empty);
		Assert.That(result.Report.RemoteAdditions, Is.Empty);
		Assert.That(result.Report.LocalDeletions, Is.Empty);
		Assert.That(result.Report.RemoteDeletions, Is.Empty);
		Assert.That(
			result.MergedContent!,
			Is.EqualTo(expected));
	}

	[Test]
	public void MetadataMerge_FlatMetadataClientUnit1_WithDeletions()
	{
		var baseContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("metadataClientUnit1", "base.json"));
		var localContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("metadataClientUnit1", "local.json"));
		var remoteContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("metadataClientUnit1", "remote.json"));
		var expected = File.ReadAllText(ResolverTestSupport.GetFixturePath("metadataClientUnit1", "resolved.json"));

		var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
			global::Creatio.ConflictResolver.ConflictFileType.MetadataJson,
			baseContent,
			localContent,
			remoteContent));

		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.Resolved), result.ErrorMessage);
		Assert.That(result.Report.WinnerPolicy, Is.EqualTo("LOCAL"));
		Assert.That(result.Report.LocalAdditions, Is.Empty);
		Assert.That(result.Report.RemoteAdditions, Is.EqualTo(Array.Empty<string>()));
		Assert.That(result.Report.LocalDeletions, Is.EqualTo(Array.Empty<string>()));
		Assert.That(result.Report.RemoteDeletions, Is.EqualTo(Array.Empty<string>()));
		Assert.That(
			result.MergedContent!,
			Is.EqualTo(expected));
	}

	[Test]
	public void MetadataMerge_FlatMetadataClientUnit2_WithLocalAdditionAndArrayConflict()
	{
		var baseContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("metadataClientUnit2", "base.json"));
		var localContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("metadataClientUnit2", "local.json"));
		var remoteContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("metadataClientUnit2", "remote.json"));
		var expected = File.ReadAllText(ResolverTestSupport.GetFixturePath("metadataClientUnit2", "resolved.json"));

		var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
			global::Creatio.ConflictResolver.ConflictFileType.MetadataJson,
			baseContent,
			localContent,
			remoteContent));

		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.Resolved), result.ErrorMessage);
		Assert.That(result.Report.WinnerPolicy, Is.EqualTo("LOCAL"));
		Assert.That(result.Report.LocalAdditions, Is.EqualTo(Array.Empty<string>()));
		Assert.That(result.Report.RemoteAdditions, Is.Empty);
		Assert.That(result.Report.LocalDeletions, Is.Empty);
		Assert.That(result.Report.RemoteDeletions, Is.Empty);
		Assert.That(result.Report.TrueConflicts, Is.EqualTo(Array.Empty<string>()));
		Assert.That(
			result.MergedContent!,
			Is.EqualTo(expected));
	}

	[Test]
	public void MetadataMerge_FlatJsonReordererCase_MatchesExpectedOrder()
	{
		var transplier = new global::Creatio.ConflictResolver.FlatDiffTranspiler();
		var baseTransformed = File.ReadAllText(ResolverTestSupport.GetFixturePath("TestCases\\JsonReordererCase1", "base.json"));
		var localTransformed = File.ReadAllText(ResolverTestSupport.GetFixturePath("TestCases\\JsonReordererCase1", "local.json"));
		var remoteTransformed = File.ReadAllText(ResolverTestSupport.GetFixturePath("TestCases\\JsonReordererCase1", "remote.json"));
		var expectedTransformed = File.ReadAllText(ResolverTestSupport.GetFixturePath("TestCases\\JsonReordererCase1", "mergedOrderedExample.json"));
		var baseContent = transplier.Restore(baseTransformed);
		var localContent = transplier.Restore(localTransformed);
		var remoteContent = transplier.Restore(remoteTransformed);

		var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
			global::Creatio.ConflictResolver.ConflictFileType.MetadataJson,
			baseContent,
			localContent,
			remoteContent));

		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.Resolved), result.ErrorMessage);
		var actualTransformed = transplier.Transform(result.MergedContent!);
		Assert.That(JsonReordererTests.ReadItemUids(actualTransformed),
			Is.EqualTo(JsonReordererTests.ReadItemUids(expectedTransformed)));
	}

	[Test]
	public void MetadataMerge_FlatLogicalConflict_AutomergeMode_EmitsConflictMarkers()
	{
		const string baseContent = """
		+ MetaData.Schema.B2 {
		  "UId": "uid-1",
		  "A2": "Base",
		  "A3": "const"
		}
		~ MetaData.Schema.B2 [
		  "uid-1"
		]
		""";
		const string localContent = """
		+ MetaData.Schema.B2 {
		  "UId": "uid-1",
		  "A2": "Local",
		  "A3": "const"
		}
		+ MetaData.Schema.B2 {
		  "UId": "uid-local",
		  "A2": "LocalOnly"
		}
		~ MetaData.Schema.B2 [
		  "uid-1",
		  "uid-local"
		]
		""";
		const string remoteContent = """
		+ MetaData.Schema.B2 {
		  "UId": "uid-1",
		  "A2": "Remote",
		  "A3": "const"
		}
		+ MetaData.Schema.B2 {
		  "UId": "uid-remote",
		  "A2": "RemoteOnly"
		}
		~ MetaData.Schema.B2 [
		  "uid-1",
		  "uid-remote"
		]
		""";

		var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
			global::Creatio.ConflictResolver.ConflictFileType.MetadataJson,
			baseContent,
			localContent,
			remoteContent,
			null,
			global::Creatio.ConflictResolver.MergeMode.Automerge));
		var normalized = result.MergedContent!.ReplaceLineEndings("\n");

		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.AutoResolvedWithConflicts), result.ErrorMessage);
		Assert.That(result.Report.WinnerPolicy, Is.EqualTo("CONFLICT_MARKERS"));
		Assert.That(normalized, Does.Contain("\"UId\": \"uid-1\",\n<<<<<<< Local\n  \"A2\": \"Local\",\n=======\n  \"A2\": \"Remote\",\n>>>>>>> Remote\n  \"A3\": \"const\""));
		Assert.That(normalized, Does.Not.Contain("\n  <<<<<<< Local\n"));
		Assert.That(normalized, Does.Not.Contain("\n  =======\n"));
		Assert.That(normalized, Does.Not.Contain("\n  >>>>>>> Remote\n"));
		Assert.That(normalized, Does.Contain("\"UId\": \"uid-local\""));
		Assert.That(normalized, Does.Contain("\"UId\": \"uid-remote\""));
	}

	[Test]
	[Description("Emits selectable whole-item markers when local deletes a flat metadata item and remote modifies it.")]
	public void MetadataMerge_DeleteVersusModify_AutomergeMode_PreservesIndependentAdditionsInBothChoices()
	{
		// Arrange
		const string baseContent = """
		+ MetaData.Schema.D2 {
		  "UId": "shared-uid",
		  "A2": "Shared"
		}
		~ MetaData.Schema.D2 [
		  "shared-uid"
		]
		""";
		const string localContent = """
		+ MetaData.Schema.D2 {
		  "UId": "local-added-uid",
		  "A2": "LocalAdded"
		}
		~ MetaData.Schema.D2 [
		  "local-added-uid"
		]
		""";
		const string remoteContent = """
		+ MetaData.Schema.D2 {
		  "UId": "shared-uid",
		  "A2": "RemoteRenamed"
		}
		+ MetaData.Schema.D2 {
		  "UId": "remote-added-uid",
		  "A2": "RemoteAdded"
		}
		~ MetaData.Schema.D2 [
		  "shared-uid",
		  "remote-added-uid"
		]
		""";

		// Act
		var result = ResolverTestSupport.Resolver.Resolve(new MergeRequest(
			ConflictFileType.MetadataJson,
			baseContent,
			localContent,
			remoteContent,
			null,
			MergeMode.Automerge));
		var normalized = result.MergedContent?.ReplaceLineEndings("\n");
		var localSelected = SelectAllConflictAlternatives(normalized!, "Local");
		var remoteSelected = SelectAllConflictAlternatives(normalized!, "Remote");
		var transpiler = new FlatDiffTranspiler();

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(MergeStatus.AutoResolvedWithConflicts),
				"because delete versus modify requires an explicit user choice");
			Assert.That(result.Report.VerificationPassed, Is.True,
				"because valid flat metadata inputs must retain resolver verification");
			Assert.That(normalized, Does.Contain("LocalAdded"),
				"because the local independent addition must survive");
			Assert.That(normalized, Does.Contain("RemoteAdded"),
				"because the remote independent addition must survive");
			Assert.That(normalized, Does.Contain("<<<<<<< Local\n=======\n+ MetaData.Schema.D2 {\n  \"UId\": \"shared-uid\",\n  \"A2\": \"RemoteRenamed\"\n}\n>>>>>>> Remote"),
				"because the deleted or renamed whole item must be selectable without inventing null metadata");
			Assert.That(normalized, Does.Contain("\"local-added-uid\""),
				"because the local addition must remain in the collection choice");
			Assert.That(normalized, Does.Contain("\"remote-added-uid\""),
				"because the remote addition must remain in the collection choice");
			Assert.That(() => transpiler.Transform(localSelected), Throws.Nothing,
				"because selecting Local in every coordinated block must leave valid flat metadata");
			Assert.That(() => transpiler.Transform(remoteSelected), Throws.Nothing,
				"because selecting Remote in every coordinated block must leave valid flat metadata");
			Assert.That(CountOccurrences(localSelected, "local-added-uid"), Is.EqualTo(2),
				"because Local selection must retain the local item and its collection membership");
			Assert.That(CountOccurrences(localSelected, "remote-added-uid"), Is.EqualTo(2),
				"because Local selection must also retain the remote independent addition");
			Assert.That(localSelected, Does.Not.Contain("shared-uid"),
				"because Local selection represents Developer A's deletion");
			Assert.That(CountOccurrences(remoteSelected, "local-added-uid"), Is.EqualTo(2),
				"because Remote selection must retain the local independent addition");
			Assert.That(CountOccurrences(remoteSelected, "remote-added-uid"), Is.EqualTo(2),
				"because Remote selection must retain the remote item and its collection membership");
			Assert.That(CountOccurrences(remoteSelected, "shared-uid"), Is.EqualTo(2),
				"because Remote selection must retain the renamed item and its collection membership");
			Assert.That(remoteSelected, Does.Contain("RemoteRenamed"),
				"because Remote selection represents Developer B's rename");
			Assert.That(normalized!.Split('\n').Where(line => line.Contains("<<<<<<< Local", StringComparison.Ordinal)),
				Is.All.EqualTo("<<<<<<< Local"),
				"because conflict markers must begin at column zero for standard merge tooling");
		});
	}
	
	[Test]
	[Description("Returns a whole-stream conflict when repeated flat operations would otherwise overwrite one another.")]
	public void MetadataMerge_RepeatedFlatOperationWithConcurrentAdditions_PreservesBothStreams()
	{
		// Arrange
		const string baseContent = """
		= MetaData.Schema.UId "base-a"
		= MetaData.Schema.UId "base-b"
		""";
		const string localContent = """
		= MetaData.Schema.UId "base-a"
		= MetaData.Schema.UId "base-b"
		= MetaData.Schema.A2 "local-addition"
		""";
		const string remoteContent = """
		= MetaData.Schema.UId "base-a"
		= MetaData.Schema.UId "base-b"
		= MetaData.Schema.A3 "remote-addition"
		""";

		// Act
		var result = ResolverTestSupport.Resolver.Resolve(new MergeRequest(
			ConflictFileType.MetadataJson,
			baseContent,
			localContent,
			remoteContent,
			null,
			MergeMode.Automerge));

		// Assert
		Assert.That(result.Status, Is.EqualTo(MergeStatus.UnresolvedConflict),
			"ambiguous repeated operation keys must require a whole-stream choice instead of using last-write-wins");
		Assert.That(result.MergedContent, Does.Contain("local-addition"),
			"the complete local stream must remain available to the agent");
		Assert.That(result.MergedContent, Does.Contain("remote-addition"),
			"the complete remote stream must remain available to the agent");
	}

	[Test]
	[Description("Rejects flat metadata before transpilation when its operation count exceeds the safe budget.")]
	public void MetadataMerge_ExcessiveFlatOperations_ReturnsInvalidInput()
	{
		// Arrange
		string content = string.Join("\n", Enumerable.Range(0, 2_501).Select(index =>
			$"= MetaData.Schema.P{index} \"value\""));

		// Act
		var result = ResolverTestSupport.Resolver.Resolve(new MergeRequest(
			ConflictFileType.MetadataJson,
			content,
			content,
			content));

		// Assert
		Assert.That(result.Status, Is.EqualTo(MergeStatus.InvalidInput),
			"the operation budget must stop high-allocation transpilation before it starts");
		Assert.That(result.ErrorCode, Is.EqualTo("FlatOperationLimitExceeded"),
			"the caller must receive the stable flat-operation limit code");
	}

	[Test]
	public void MetadataMerge_AutomergeMode_EmitsConflictMarkersForClientUnitMetadata()
	{
		var baseContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("NewMode\\clientUnitMetadata", "base.json"));
		var localContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("NewMode\\clientUnitMetadata", "local.json"));
		var remoteContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("NewMode\\clientUnitMetadata", "remote.json"));
		var expected = File.ReadAllText(ResolverTestSupport.GetFixturePath("NewMode\\clientUnitMetadata", "resolved.json"));

		var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
			global::Creatio.ConflictResolver.ConflictFileType.MetadataJson,
			baseContent,
			localContent,
			remoteContent,
			null,
			global::Creatio.ConflictResolver.MergeMode.Automerge));
		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.AutoResolvedWithConflicts), result.ErrorMessage);
		Assert.That(result.MergedContent!, Is.EqualTo(expected));

	}
	
	[Test]
	public void MetadataMerge_AutomergeMode_SergeyTest()
	{
		var baseContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("NewMode\\SergeyTest", "base.json"));
		var localContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("NewMode\\SergeyTest", "local.json"));
		var remoteContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("NewMode\\SergeyTest", "remote.json"));
		var expected = File.ReadAllText(ResolverTestSupport.GetFixturePath("NewMode\\SergeyTest", "resolved.json"));

		var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
			global::Creatio.ConflictResolver.ConflictFileType.MetadataJson,
			baseContent,
			localContent,
			remoteContent,
			null,
			global::Creatio.ConflictResolver.MergeMode.Automerge));
		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.Resolved), result.ErrorMessage);
		Assert.That(result.MergedContent!, Is.EqualTo(expected));

	}

	[Test]
	public void MetadataMerge_FlatFixture_AutomergeMode_EmitsConflictMarkersForInlineStringBody()
	{
		var baseContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("MetaDataCase5", "metadatabase5.json"));
		var localContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("MetaDataCase5", "metadatalocal5.json"));
		var remoteContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("MetaDataCase5", "metadataremote5.json"));

		var result = ResolverTestSupport.Resolver.Resolve(new MergeRequest(
			ConflictFileType.MetadataJson,
			baseContent,
			localContent,
			remoteContent,
			null,
			MergeMode.Automerge));
		var normalized = result.MergedContent!.ReplaceLineEndings("\n");

		Assert.That(result.Status, Is.EqualTo(MergeStatus.AutoResolvedWithConflicts), result.ErrorMessage);
		Assert.That(result.Report.WinnerPolicy, Is.EqualTo("CONFLICT_MARKERS"));
		Assert.That(normalized, Does.Contain("<<<<<<< Local\n\"define('{0}Structure'"));
		Assert.That(normalized, Does.Contain("schemaVersion:1"));
		Assert.That(normalized, Does.Contain("=======\n\"define('{0}Structure'"));
		Assert.That(normalized, Does.Contain("schemaVersion:0"));
		Assert.That(normalized, Does.Contain(">>>>>>> Remote"));
	}

	private static string SelectAllConflictAlternatives(string content, string side)
	{
		var lines = content.ReplaceLineEndings("\n").Split('\n').ToList();
		while (true)
		{
			var start = lines.FindIndex(line => line == "<<<<<<< Local");
			if (start < 0)
			{
				return string.Join('\n', lines);
			}
			var separator = lines.FindIndex(start + 1, line => line == "=======");
			var end = lines.FindIndex(separator + 1, line => line == ">>>>>>> Remote");
			if (separator < 0 || end < 0)
			{
				throw new InvalidOperationException("Incomplete Local/Remote conflict markers.");
			}
			var selected = side switch
			{
				"Local" => lines.GetRange(start + 1, separator - start - 1),
				"Remote" => lines.GetRange(separator + 1, end - separator - 1),
				_ => throw new ArgumentOutOfRangeException(nameof(side), side, "Unknown conflict side.")
			};
			lines.RemoveRange(start, end - start + 1);
			lines.InsertRange(start, selected);
		}
	}

	private static int CountOccurrences(string content, string value)
	{
		var count = 0;
		var offset = 0;
		while ((offset = content.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
		{
			count++;
			offset += value.Length;
		}
		return count;
	}
}
