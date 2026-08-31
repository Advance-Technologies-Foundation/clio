using System.Text.Json;
using System.Text.Json.Nodes;
using Creatio.ConflictResolver.Tests.TestSupport;

namespace Creatio.ConflictResolver.Tests;

[TestFixture, Category("Unit")]
public class MetadataJsonMergeStrategyTests
{
	[Test]
	public void MetadataMerge_JsonFixture_AppliesExpectedRemoteAndLocalChanges()
	{
		var (baseContent, localContent, remoteContent) = ReadGeneratedCase("JsonMetadataCase1");

		var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
			global::Creatio.ConflictResolver.ConflictFileType.MetadataJson,
			baseContent,
			localContent,
			remoteContent));

		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.Resolved), result.ErrorMessage);
		var normalizedMerged = ResolverTestSupport.NormalizeLineEndings(result.MergedContent!);
		Assert.That(normalizedMerged, Does.Contain("\"IR2\": {"));
		Assert.That(normalizedMerged, Does.Not.Contain("\"IR2\" : {"));
		Assert.That(normalizedMerged, Does.Contain("\"IW1\": [\n"));
		Assert.That(normalizedMerged, Does.Not.Contain("\"IW1\" : [ {"));

		var schema = GetSchema(result.MergedContent!);
		Assert.That(GetStringValue(schema, "A2"), Is.EqualTo("GeminiApiSearch2"));
		Assert.That(GetStringValue(schema, "IQ1"), Is.EqualTo("https://generativelanguage.googleapis.com/v2"));

		var iq4 = schema["IQ4"]!.AsArray();
		var endpoint = FindByUid(iq4, "d5976760-72ed-4133-9789-ac5fc989729f");
		var responseMap = endpoint["IR2"]!.AsObject()["IW1"]!.AsArray();
		Assert.That(FindByUidOrNull(responseMap, "b869a628-0e04-4262-ae91-3a1ab521235d"), Is.Not.Null);
		Assert.That(FindByUid(responseMap, "a715eb22-d6a8-4c6f-9670-20da16d53034")["A2"]!.GetValue<string>(), Is.EqualTo("UMTUPTCount2"));

		var candidates = FindByUid(responseMap, "239eccfc-895a-4a7c-b3f0-56e50b61c40e");
		var candidateNested = candidates["IU6"]!.AsArray();
		Assert.That(FindByUidOrNull(candidateNested, "98a19cf4-d29c-4ee9-91d2-96097488f384"), Is.Null);
		Assert.That(
			result.Report.TrueConflicts.Any(x => x.Contains("98a19cf4-d29c-4ee9-91d2-96097488f384", StringComparison.OrdinalIgnoreCase)),
			Is.True);
	}

	[Test]
	public void MetadataMerge_JsonObjectConflictWebService()
	{
		var baseContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("MetadataJsonFormat", "base.json"));
		var localContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("MetadataJsonFormat", "local.json"));
		var remoteContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("MetadataJsonFormat", "remote.json"));
		var expected = File.ReadAllText(ResolverTestSupport.GetFixturePath("MetadataJsonFormat", "resolved.json"));

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
	public void MetadataMerge_JsonObjectConflictWebService2()
	{
		var baseContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("metadataJsonCaseWebService", "base.json"));
		var localContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("metadataJsonCaseWebService", "local.json"));
		var remoteContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("metadataJsonCaseWebService", "remote.json"));
		var expected = File.ReadAllText(ResolverTestSupport.GetFixturePath("metadataJsonCaseWebService", "resolved.json"));

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
	public void MetadataMerge_JsonObjectConflictWebService3()
	{
		var baseContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("metadataJsonCaseWebService2", "base.json"));
		var localContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("metadataJsonCaseWebService2", "local.json"));
		var remoteContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("metadataJsonCaseWebService2", "remote.json"));
		var expected = File.ReadAllText(ResolverTestSupport.GetFixturePath("metadataJsonCaseWebService2", "resolved.json"));

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
	public void MetadataMerge_FlatJsonEntityCase4Base()
	{
		var baseContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("TestCases\\EntityCase4Base", "base.json"));
		var localContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("TestCases\\EntityCase4Base", "local.json"));
		var remoteContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("TestCases\\EntityCase4Base", "remote.json"));
		var expectedContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("TestCases\\EntityCase4Base", "results.json"));
		var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
			global::Creatio.ConflictResolver.ConflictFileType.MetadataJson,
			baseContent,
			localContent,
			remoteContent));
		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.Resolved), result.ErrorMessage);
		Assert.That(
			ResolverTestSupport.NormalizeLineEndings(result.MergedContent!),
			Is.EqualTo(ResolverTestSupport.NormalizeLineEndings(expectedContent)));
	}

	[Test]
	public void MetadataMerge_JsonPrimitiveConflict_LocalWins()
	{
		var (baseContent, localContent, remoteContent) = ReadGeneratedCase("PrimitiveConflict");

		var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
			global::Creatio.ConflictResolver.ConflictFileType.MetadataJson,
			baseContent,
			localContent,
			remoteContent));
		var schema = GetSchema(result.MergedContent!);
		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.Resolved), result.ErrorMessage);
		Assert.That(GetStringValue(schema, "A2"), Is.EqualTo("local"));
		Assert.That(result.Report.TrueConflicts, Does.Contain("$.MetaData.Schema.A2"));
	}

	[Test]
	public void MetadataMerge_JsonPrimitiveAddedInBothBranches_LocalWins()
	{
		const string baseContent = """
		{
		  "MetaData": {
		    "Schema": {}
		  }
		}
		""";
		const string localContent = """
		{
		  "MetaData": {
		    "Schema": {
		      "N1": 1
		    }
		  }
		}
		""";
		const string remoteContent = """
		{
		  "MetaData": {
		    "Schema": {
		      "N1": 2
		    }
		  }
		}
		""";

		var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
			global::Creatio.ConflictResolver.ConflictFileType.MetadataJson,
			baseContent,
			localContent,
			remoteContent));
		var schema = GetSchema(result.MergedContent!);
		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.Resolved), result.ErrorMessage);
		Assert.That(schema["N1"]!.GetValue<int>(), Is.EqualTo(1));
		Assert.That(result.Report.TrueConflicts, Does.Contain("$.MetaData.Schema.N1"));
	}

	[Test]
	public void MetadataMerge_JsonPrimitiveChangedToSameValueInBothBranches_DoesNotReportConflict()
	{
		const string baseContent = """
		{
		  "MetaData": {
		    "Schema": {
		      "A2": "base"
		    }
		  }
		}
		""";
		const string localContent = """
		{
		  "MetaData": {
		    "Schema": {
		      "A2": "same"
		    }
		  }
		}
		""";
		const string remoteContent = """
		{
		  "MetaData": {
		    "Schema": {
		      "A2": "same"
		    }
		  }
		}
		""";

		var result = ResolverTestSupport.Resolver.Resolve(new MergeRequest(
			ConflictFileType.MetadataJson,
			baseContent,
			localContent,
			remoteContent));
		var schema = GetSchema(result.MergedContent!);

		Assert.That(result.Status, Is.EqualTo(MergeStatus.Resolved), result.ErrorMessage);
		Assert.That(GetStringValue(schema, "A2"), Is.EqualTo("same"));
		Assert.That(result.Report.TrueConflicts, Does.Not.Contain("$.MetaData.Schema.A2"));
	}

	[Test]
	public void MetadataMerge_JsonPrimitiveAddedWithSameValueInBothBranches_DoesNotReportConflict()
	{
		const string baseContent = """
		{
		  "MetaData": {
		    "Schema": {}
		  }
		}
		""";
		const string localContent = """
		{
		  "MetaData": {
		    "Schema": {
		      "N1": 1
		    }
		  }
		}
		""";
		const string remoteContent = """
		{
		  "MetaData": {
		    "Schema": {
		      "N1": 1
		    }
		  }
		}
		""";

		var result = ResolverTestSupport.Resolver.Resolve(new MergeRequest(
			ConflictFileType.MetadataJson,
			baseContent,
			localContent,
			remoteContent));
		var schema = GetSchema(result.MergedContent!);

		Assert.That(result.Status, Is.EqualTo(MergeStatus.Resolved), result.ErrorMessage);
		Assert.That(schema["N1"]!.GetValue<int>(), Is.EqualTo(1));
		Assert.That(result.Report.TrueConflicts, Does.Not.Contain("$.MetaData.Schema.N1"));
	}

	[Test]
	public void MetadataMerge_JsonPrimitiveArray_DeletionWinsOverRemotePresence()
	{
		var (baseContent, localContent, remoteContent) = ReadGeneratedCase("PrimitiveArrayDelete");

		var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
			global::Creatio.ConflictResolver.ConflictFileType.MetadataJson,
			baseContent,
			localContent,
			remoteContent));
		var schema = GetSchema(result.MergedContent!);
		var array = schema["IQ7"]!.AsArray();
		var values = array.Select(x => x?.GetValue<int>()).Where(x => x.HasValue).Select(x => x!.Value).ToArray();

		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.Resolved), result.ErrorMessage);
		Assert.That(values, Is.EqualTo(new[] { 2, 3 }));
		Assert.That(result.Report.TrueConflicts, Does.Contain("$.MetaData.Schema.IQ7"));
	}

	[Test]
	public void MetadataMerge_JsonObjectArray_ByUidMergesRecursivelyAndKeepsLocalOnConflict()
	{
		var (baseContent, localContent, remoteContent) = ReadGeneratedCase("ObjectArrayUidConflict");

		var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
			global::Creatio.ConflictResolver.ConflictFileType.MetadataJson,
			baseContent,
			localContent,
			remoteContent));
		var schema = GetSchema(result.MergedContent!);
		var mergedItem = FindByUid(schema["IQ4"]!.AsArray(), "uid-1");

		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.Resolved), result.ErrorMessage);
		Assert.That(mergedItem["A2"]!.GetValue<string>(), Is.EqualTo("local"));
		Assert.That(mergedItem["X"]!.GetValue<int>(), Is.EqualTo(1));
		Assert.That(result.Report.TrueConflicts, Does.Contain("$.MetaData.Schema.IQ4[uid-1]"));
	}

	[Test]
	public void MetadataMerge_JsonObjectArray_ReportsMissingUid()
	{
		const string baseContent = """
		{
		  "MetaData": {
		    "Schema": {
		      "IQ4": []
		    }
		  }
		}
		""";
		const string localContent = """
		{
		  "MetaData": {
		    "Schema": {
		      "IQ4": [
		        {
		          "A2": "WithoutUid"
		        }
		      ]
		    }
		  }
		}
		""";
		const string remoteContent = """
		{
		  "MetaData": {
		    "Schema": {
		      "IQ4": []
		    }
		  }
		}
		""";
		var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
			global::Creatio.ConflictResolver.ConflictFileType.MetadataJson,
			baseContent,
			localContent,
			remoteContent));

		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.Resolved), result.ErrorMessage);
		Assert.That(result.Report.VerificationPassed, Is.False);
		Assert.That(result.Report.TrueConflicts.Any(x => x.StartsWith("MissingUId:", StringComparison.Ordinal)), Is.True);
	}

	[Test]
	public void MetadataMerge_JsonPrimitiveArray_SameValueAddedInBothBranches_DoesNotReportConflict()
	{
		const string baseContent = """
		{
		  "MetaData": {
		    "Schema": {
		      "IQ7": [1]
		    }
		  }
		}
		""";
		const string localContent = """
		{
		  "MetaData": {
		    "Schema": {
		      "IQ7": [1, 2]
		    }
		  }
		}
		""";
		const string remoteContent = """
		{
		  "MetaData": {
		    "Schema": {
		      "IQ7": [1, 2]
		    }
		  }
		}
		""";

		var result = ResolverTestSupport.Resolver.Resolve(new MergeRequest(
			ConflictFileType.MetadataJson,
			baseContent,
			localContent,
			remoteContent));
		var schema = GetSchema(result.MergedContent!);
		var values = schema["IQ7"]!.AsArray()
			.Select(x => x?.GetValue<int>())
			.Where(x => x.HasValue)
			.Select(x => x!.Value)
			.ToArray();

		Assert.That(result.Status, Is.EqualTo(MergeStatus.Resolved), result.ErrorMessage);
		Assert.That(values, Is.EqualTo(new[] { 1, 2 }));
		Assert.That(result.Report.TrueConflicts, Does.Not.Contain("$.MetaData.Schema.IQ7"));
	}

	[Test]
	public void MetadataMerge_JsonObjectAddedWithSameContentInBothBranches_DoesNotReportConflict()
	{
		const string baseContent = """
		{
		  "MetaData": {
		    "Schema": {}
		  }
		}
		""";
		const string localContent = """
		{
		  "MetaData": {
		    "Schema": {
		      "IQ5": {
		        "A2": "same",
		        "X": 1
		      }
		    }
		  }
		}
		""";
		const string remoteContent = """
		{
		  "MetaData": {
		    "Schema": {
		      "IQ5": {
		        "A2": "same",
		        "X": 1
		      }
		    }
		  }
		}
		""";

		var result = ResolverTestSupport.Resolver.Resolve(new MergeRequest(
			ConflictFileType.MetadataJson,
			baseContent,
			localContent,
			remoteContent));
		var schema = GetSchema(result.MergedContent!);

		Assert.That(result.Status, Is.EqualTo(MergeStatus.Resolved), result.ErrorMessage);
		Assert.That(GetStringValue(schema["IQ5"]!.AsObject(), "A2"), Is.EqualTo("same"));
		Assert.That(result.Report.TrueConflicts, Does.Not.Contain("$.MetaData.Schema.IQ5"));
	}

	[Test]
	public void MetadataMerge_JsonObjectArray_ItemAddedWithSameContentInBothBranches_DoesNotReportConflict()
	{
		const string baseContent = """
		{
		  "MetaData": {
		    "Schema": {
		      "IQ4": []
		    }
		  }
		}
		""";
		const string localContent = """
		{
		  "MetaData": {
		    "Schema": {
		      "IQ4": [
		        {
		          "UId": "uid-1",
		          "A2": "same",
		          "X": 1
		        }
		      ]
		    }
		  }
		}
		""";
		const string remoteContent = """
		{
		  "MetaData": {
		    "Schema": {
		      "IQ4": [
		        {
		          "UId": "uid-1",
		          "A2": "same",
		          "X": 1
		        }
		      ]
		    }
		  }
		}
		""";

		var result = ResolverTestSupport.Resolver.Resolve(new MergeRequest(
			ConflictFileType.MetadataJson,
			baseContent,
			localContent,
			remoteContent));
		var schema = GetSchema(result.MergedContent!);
		var mergedItem = FindByUid(schema["IQ4"]!.AsArray(), "uid-1");

		Assert.That(result.Status, Is.EqualTo(MergeStatus.Resolved), result.ErrorMessage);
		Assert.That(mergedItem["A2"]!.GetValue<string>(), Is.EqualTo("same"));
		Assert.That(mergedItem["X"]!.GetValue<int>(), Is.EqualTo(1));
		Assert.That(result.Report.TrueConflicts, Does.Not.Contain("$.MetaData.Schema.IQ4[uid-1]"));
	}

	[Test]
	public void MetadataMerge_JsonObjectArray_UsesConfiguredObjectArrayKeyProperty()
	{
		const string baseContent = """
		{
		  "MetaData": {
		    "Schema": {
		      "IQ4": [
		        {
		          "Id": "item-1",
		          "A2": "base",
		          "X": 0
		        }
		      ]
		    }
		  }
		}
		""";
		const string localContent = """
		{
		  "MetaData": {
		    "Schema": {
		      "IQ4": [
		        {
		          "Id": "item-1",
		          "A2": "local",
		          "X": 0
		        }
		      ]
		    }
		  }
		}
		""";
		const string remoteContent = """
		{
		  "MetaData": {
		    "Schema": {
		      "IQ4": [
		        {
		          "Id": "item-1",
		          "A2": "remote",
		          "X": 1
		        }
		      ]
		    }
		  }
		}
		""";

		var resolver = new global::Creatio.ConflictResolver.ConflictResolver(
			[
				new global::Creatio.ConflictResolver.Strategies.MetadataMergeStrategy("Id")
			]);
		var result = resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
			global::Creatio.ConflictResolver.ConflictFileType.MetadataJson,
			baseContent,
			localContent,
			remoteContent));
		var schema = GetSchema(result.MergedContent!);
		var mergedItem = FindByKey(schema["IQ4"]!.AsArray(), "Id", "item-1");

		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.Resolved), result.ErrorMessage);
		Assert.That(mergedItem["A2"]!.GetValue<string>(), Is.EqualTo("local"));
		Assert.That(mergedItem["X"]!.GetValue<int>(), Is.EqualTo(1));
		var expectedPattern =
			result.Report.TrueConflicts.Any(x =>
				x.StartsWith("$.MetaData.Schema.IQ4[item-1]", StringComparison.Ordinal));
		Assert.That(
			expectedPattern,
			Is.True);
	}

	[Test]
	public void MetadataMerge_AndrewCase_RestService1Remote_LocalWinsTrueConflicts()
	{
		var baseContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("TestCases\\RestService_1_Remote", "base.json"));
		var localContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("TestCases\\RestService_1_Remote", "local.json"));
		var remoteContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("TestCases\\RestService_1_Remote", "remote.json"));
		var expected = File.ReadAllText(ResolverTestSupport.GetFixturePath("TestCases\\RestService_1_Remote", "resolved.json"));
		var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
			global::Creatio.ConflictResolver.ConflictFileType.MetadataJson,
			baseContent,
			localContent,
			remoteContent));
		Assert.That(
			result.MergedContent!,
			Is.EqualTo(expected));

		var schema = GetSchema(result.MergedContent!);
		var methods = schema["IQ4"]!.AsArray();
		var method = FindByUid(methods, "86c12308-91f7-4476-9c55-80ec6fe43a05");
		var requestParameters = method["IR1"]!.AsObject()["IW1"]!.AsArray();
		var requestParameter = FindByUid(requestParameters, "b26d67d6-661c-4791-9564-57e289d5336c");
		var iq5 = schema["IQ5"]!.AsObject();

		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.Resolved), result.ErrorMessage);
		Assert.That(result.Report.VerificationPassed, Is.True);
		Assert.That(result.Report.WinnerPolicy, Is.EqualTo("LOCAL"));
		Assert.That(GetStringValue(schema, "IQ1"), Is.EqualTo("http://localhost/rest1RemoteLocal"));
		Assert.That(schema["IQ3"]!.GetValue<int>(), Is.EqualTo(7));
		Assert.That(GetStringValue(method, "A2"), Is.EqualTo("RestMethod1Local"));
		Assert.That(method["IR3"]!.GetValue<int>(), Is.EqualTo(200));
		Assert.That(method["IR4"]!.GetValue<bool>(), Is.True);
		Assert.That(method["IR1"]!.AsObject()["IT1"]!.GetValue<string>(), Is.EqualTo("/address1Local/"));
		Assert.That(GetStringValue(requestParameter, "A2"), Is.EqualTo("preq1l"));
		Assert.That(iq5["IZ1"], Is.Not.Null);
		Assert.That(iq5["IZ2"], Is.Not.Null);
		Assert.That(result.Report.TrueConflicts, Does.Contain("$.MetaData.Schema.IQ1"));
		Assert.That(result.Report.TrueConflicts, Does.Contain("$.MetaData.Schema.IQ4[86c12308-91f7-4476-9c55-80ec6fe43a05].A2"));
		Assert.That(result.Report.TrueConflicts, Does.Contain("$.MetaData.Schema.IQ5"));
	}

	[Test]
	public void MetadataMerge_AndrewCase_RestService3Base_KeepLocalWhenRemoteDeletesMethod()
	{
		var baseContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("TestCases\\RestService_3_Base", "base.json"));
		var localContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("TestCases\\RestService_3_Base", "local.json"));
		var remoteContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("TestCases\\RestService_3_Base", "remote.json"));
		var expected = File.ReadAllText(ResolverTestSupport.GetFixturePath("TestCases\\RestService_3_Base", "resolved.json"));
		var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
			global::Creatio.ConflictResolver.ConflictFileType.MetadataJson,
			baseContent,
			localContent,
			remoteContent));
		Assert.That(
			result.MergedContent!,
			Is.EqualTo(expected));
		var schema = GetSchema(result.MergedContent!);
		var methods = schema["IQ4"]!.AsArray();
		var method = FindByUid(methods, "1ebcd0ff-8e1b-4429-9f1b-bbc21dbba70f");
		var requestParameters = method["IR1"]!.AsObject()["IW1"]!.AsArray();
		var requestParameter = FindByUid(requestParameters, "a34ac30f-5cf4-4f7a-9efa-cf4d0424c27f");

		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.Resolved), result.ErrorMessage);
		Assert.That(result.Report.VerificationPassed, Is.True);
		Assert.That(methods.Count, Is.EqualTo(1));
		Assert.That(GetStringValue(method, "A2"), Is.EqualTo("RestMethod4"));
		Assert.That(method["IR1"]!.AsObject()["IT1"]!.GetValue<string>(), Is.EqualTo("/rest4local/"));
		Assert.That(GetStringValue(requestParameter, "A2"), Is.EqualTo("preq4local"));
		Assert.That(
			result.Report.TrueConflicts,
			Is.EqualTo(new[] { "$.MetaData.Schema.IQ4[1ebcd0ff-8e1b-4429-9f1b-bbc21dbba70f]" }));
	}

	[Test]
	public void MetadataMerge_AndrewCase_SOAPService2Remote_ResolvesRemoteAndLocalChanges()
	{
		var baseContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("TestCases\\SOAPService_2_Remote", "base.json"));
		var localContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("TestCases\\SOAPService_2_Remote", "local.json"));
		var remoteContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("TestCases\\SOAPService_2_Remote", "remote.json"));
		var expected = File.ReadAllText(ResolverTestSupport.GetFixturePath("TestCases\\SOAPService_2_Remote", "resolved.json"));
		var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
			global::Creatio.ConflictResolver.ConflictFileType.MetadataJson,
			baseContent,
			localContent,
			remoteContent));
		Assert.That(
			result.MergedContent!,
			Is.EqualTo(expected));
		var schema = GetSchema(result.MergedContent!);
		var methods = schema["IQ4"]!.AsArray();
		var method = FindByUid(methods, "43c7381a-53e1-404c-a9e2-2cddfc98fa76");
		var responseParameters = method["IR2"]!.AsObject()["IW1"]!.AsArray();
		var responseParameter = FindByUid(responseParameters, "22deb001-8b21-40b8-a9e7-2053fd52d097");
		var iq5 = schema["IQ5"]!.AsObject();

		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.Resolved), result.ErrorMessage);
		Assert.That(result.Report.VerificationPassed, Is.True);
		Assert.That(GetStringValue(schema, "IQ1"), Is.EqualTo("http://localhost/SOAPService2Local"));
		Assert.That(schema["IQ2"]!.GetValue<int>(), Is.EqualTo(3));
		Assert.That(schema["IQ3"]!.GetValue<int>(), Is.EqualTo(8));
		Assert.That(GetStringValue(method, "A2"), Is.EqualTo("SOAPMethod2Local"));
		Assert.That(method["IR3"]!.GetValue<int>(), Is.EqualTo(1200));
		Assert.That(method.ContainsKey("IR4"), Is.False);
		Assert.That(method["IR5"]!.GetValue<string>(), Is.EqualTo("SOAPMethod2Local"));
		Assert.That(method["IR1"]!.AsObject()["IT1"]!.GetValue<string>(), Is.EqualTo("SOAPMethod2Local"));
		Assert.That(responseParameter["IU2"]!.GetValue<string>(), Is.EqualTo("Date"));
		Assert.That(iq5["IY1"]!.GetValue<int>(), Is.EqualTo(3));
		Assert.That(iq5.ContainsKey("IZ1"), Is.False);
		Assert.That(iq5.ContainsKey("IZ2"), Is.False);
		Assert.That(result.Report.TrueConflicts, Does.Contain("$.MetaData.Schema.IQ1"));
		Assert.That(result.Report.TrueConflicts, Does.Contain("$.MetaData.Schema.IQ3"));
		Assert.That(result.Report.TrueConflicts, Does.Contain("$.MetaData.Schema.IQ4[43c7381a-53e1-404c-a9e2-2cddfc98fa76].A2"));
		Assert.That(result.Report.TrueConflicts, Does.Contain("$.MetaData.Schema.IQ5"));
	}

	[Test]
	public void MetadataMerge_AddonAppearanceSetting_MergesLocalPropertyUpdateAndRemotePropertyAddition() {
		string testCaseName = "AddonAppearanceSettingsCase1";
		var baseContent = File.ReadAllText(ResolverTestSupport.GetFixturePath(testCaseName, "base.json"));
		var localContent = File.ReadAllText(ResolverTestSupport.GetFixturePath(testCaseName, "local.json"));
		var remoteContent = File.ReadAllText(ResolverTestSupport.GetFixturePath(testCaseName, "remote.json"));
		var expected = File.ReadAllText(ResolverTestSupport.GetFixturePath(testCaseName, "resolved.json"));
		var result = ResolverTestSupport.Resolver.Resolve(
			new MergeRequest(ConflictFileType.MetadataJson, baseContent, localContent, remoteContent));
		Assert.That(result.Status, Is.EqualTo(MergeStatus.Resolved), result.ErrorMessage);
		Assert.That(result.MergedContent!, Is.EqualTo(expected));
	}

	[Test]
	public void MetadataMerge_AddonAppearanceSetting_MergesIndependentNestedModalSettingsChanges() {
		string testCaseName = "AddonAppearanceSettingsCase2";
		var baseContent = File.ReadAllText(ResolverTestSupport.GetFixturePath(testCaseName, "base.json"));
		var localContent = File.ReadAllText(ResolverTestSupport.GetFixturePath(testCaseName, "local.json"));
		var remoteContent = File.ReadAllText(ResolverTestSupport.GetFixturePath(testCaseName, "remote.json"));
		var expected = File.ReadAllText(ResolverTestSupport.GetFixturePath(testCaseName, "resolved.json"));
		var result = ResolverTestSupport.Resolver.Resolve(
			new MergeRequest(ConflictFileType.MetadataJson, baseContent, localContent, remoteContent));
		Assert.That(result.Status, Is.EqualTo(MergeStatus.Resolved), result.ErrorMessage);
		Assert.That(result.MergedContent!, Is.EqualTo(expected));
	}

	[Test]
	public void MetadataMerge_AddonAppearanceSetting_LocalPropertyValueWins() {
		string testCaseName = "AddonAppearanceSettingsCase3";
		var baseContent = File.ReadAllText(ResolverTestSupport.GetFixturePath(testCaseName, "base.json"));
		var localContent = File.ReadAllText(ResolverTestSupport.GetFixturePath(testCaseName, "local.json"));
		var remoteContent = File.ReadAllText(ResolverTestSupport.GetFixturePath(testCaseName, "remote.json"));
		var expected = File.ReadAllText(ResolverTestSupport.GetFixturePath(testCaseName, "resolved.json"));
		var result = ResolverTestSupport.Resolver.Resolve(
			new MergeRequest(ConflictFileType.MetadataJson, baseContent, localContent, remoteContent));
		Assert.That(result.Status, Is.EqualTo(MergeStatus.Resolved), result.ErrorMessage);
		Assert.That(result.MergedContent!, Is.EqualTo(expected));
	}

	[Test]
	public void MetadataMerge_AddonAppearanceSetting_LocalNullPropertyValueWins() {
		string testCaseName = "AddonAppearanceSettingsCase4";
		var baseContent = File.ReadAllText(ResolverTestSupport.GetFixturePath(testCaseName, "base.json"));
		var localContent = File.ReadAllText(ResolverTestSupport.GetFixturePath(testCaseName, "local.json"));
		var remoteContent = File.ReadAllText(ResolverTestSupport.GetFixturePath(testCaseName, "remote.json"));
		var expected = File.ReadAllText(ResolverTestSupport.GetFixturePath(testCaseName, "resolved.json"));
		var result = ResolverTestSupport.Resolver.Resolve(
			new MergeRequest(ConflictFileType.MetadataJson, baseContent, localContent, remoteContent));
		Assert.That(result.Status, Is.EqualTo(MergeStatus.Resolved), result.ErrorMessage);
		AssertJsonEqualIgnoringFormatting(result.MergedContent!, expected);
	}

	[Test]
	public void MetadataMerge_AddodRelatedPage_BaseObjectChangedPropertiesMerged() {
		string testCaseName = "AddonRelatedPageCase1";
		var baseContent = File.ReadAllText(ResolverTestSupport.GetFixturePath(testCaseName, "base.json"));
		var localContent = File.ReadAllText(ResolverTestSupport.GetFixturePath(testCaseName, "local.json"));
		var remoteContent = File.ReadAllText(ResolverTestSupport.GetFixturePath(testCaseName, "remote.json"));
		var expected = File.ReadAllText(ResolverTestSupport.GetFixturePath(testCaseName, "resolved.json"));
		var result = ResolverTestSupport.Resolver.Resolve(
			new MergeRequest(ConflictFileType.MetadataJson, baseContent, localContent, remoteContent));
		Assert.That(result.Status, Is.EqualTo(MergeStatus.Resolved), result.ErrorMessage);
		AssertJsonEqualIgnoringFormatting(result.MergedContent!, expected);
	}

	[Test]
	public void MetadataMerge_AddodRelatedPage_BaseObjectPropertiesCollectionsMerged() {
		string testCaseName = "AddonRelatedPageCase2";
		var baseContent = File.ReadAllText(ResolverTestSupport.GetFixturePath(testCaseName, "base.json"));
		var localContent = File.ReadAllText(ResolverTestSupport.GetFixturePath(testCaseName, "local.json"));
		var remoteContent = File.ReadAllText(ResolverTestSupport.GetFixturePath(testCaseName, "remote.json"));
		var expected = File.ReadAllText(ResolverTestSupport.GetFixturePath(testCaseName, "resolved.json"));
		var result = ResolverTestSupport.Resolver.Resolve(
			new MergeRequest(ConflictFileType.MetadataJson, baseContent, localContent, remoteContent));
		Assert.That(result.Status, Is.EqualTo(MergeStatus.Resolved), result.ErrorMessage);
		AssertJsonEqualIgnoringFormatting(result.MergedContent!, expected);
	}

	[Test]
	public void MetadataMerge_AddodRelatedPage_ChildObjectChangedPropertiesMerged() {
		string testCaseName = "AddonRelatedPageCase3";
		var baseContent = File.ReadAllText(ResolverTestSupport.GetFixturePath(testCaseName, "base.json"));
		var localContent = File.ReadAllText(ResolverTestSupport.GetFixturePath(testCaseName, "local.json"));
		var remoteContent = File.ReadAllText(ResolverTestSupport.GetFixturePath(testCaseName, "remote.json"));
		var expected = File.ReadAllText(ResolverTestSupport.GetFixturePath(testCaseName, "resolved.json"));
		var result = ResolverTestSupport.Resolver.Resolve(
			new MergeRequest(ConflictFileType.MetadataJson, baseContent, localContent, remoteContent));
		Assert.That(result.Status, Is.EqualTo(MergeStatus.Resolved), result.ErrorMessage);
		Assert.That(result.MergedContent!, Is.EqualTo(expected));
	}

	[Test]
	public void MetadataMerge_AddodRelatedPage_ChildObjectPropertiesCollectionsMerged() {
		string testCaseName = "AddonRelatedPageCase4";
		var baseContent = File.ReadAllText(ResolverTestSupport.GetFixturePath(testCaseName, "base.json"));
		var localContent = File.ReadAllText(ResolverTestSupport.GetFixturePath(testCaseName, "local.json"));
		var remoteContent = File.ReadAllText(ResolverTestSupport.GetFixturePath(testCaseName, "remote.json"));
		var expected = File.ReadAllText(ResolverTestSupport.GetFixturePath(testCaseName, "resolved.json"));
		var result = ResolverTestSupport.Resolver.Resolve(
			new MergeRequest(ConflictFileType.MetadataJson, baseContent, localContent, remoteContent));
		Assert.That(result.Status, Is.EqualTo(MergeStatus.Resolved), result.ErrorMessage);
		Assert.That(result.MergedContent!, Is.EqualTo(expected));
	}

	[Test]
	public void MetadataMerge_AutomergeMode_EmitsConflictMarkersForTrueJsonConflicts()
	{
		const string baseContent = """
		{
		  "MetaData": {
		    "Schema": {
		      "IQ4": [
		        {
		          "UId": "uid-1",
		          "A2": "Base",
		          "A3": "const"
		        }
		      ]
		    }
		  }
		}
		""";
		const string localContent = """
		{
		  "MetaData": {
		    "Schema": {
		      "IQ4": [
		        {
		          "UId": "uid-1",
		          "A2": "Local",
		          "A3": "const"
		        },
		        {
		          "UId": "uid-local",
		          "A2": "LocalOnly"
		        }
		      ]
		    }
		  }
		}
		""";
		const string remoteContent = """
		{
		  "MetaData": {
		    "Schema": {
		      "IQ4": [
		        {
		          "UId": "uid-1",
		          "A2": "Remote",
		          "A3": "const"
		        },
		        {
		          "UId": "uid-remote",
		          "A2": "RemoteOnly"
		        }
		      ]
		    }
		  }
		}
		""";

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
		Assert.That(result.Report.TrueConflicts, Does.Contain("$.MetaData.Schema.IQ4[uid-1].A2"));
		Assert.That(normalized, Does.Contain("\"UId\": \"uid-1\",\n<<<<<<< Local\n          \"A2\": \"Local\",\n=======\n          \"A2\": \"Remote\",\n>>>>>>> Remote\n          \"A3\": \"const\""));
		Assert.That(normalized, Does.Not.Contain("\n          <<<<<<< Local\n"));
		Assert.That(normalized, Does.Not.Contain("\n          =======\n"));
		Assert.That(normalized, Does.Not.Contain("\n          >>>>>>> Remote\n"));
		Assert.That(normalized, Does.Contain("\"UId\": \"uid-local\""));
		Assert.That(normalized, Does.Contain("\"UId\": \"uid-remote\""));
	}

	[Test]
	public void MetadataMerge_AutomergeMode_EmitsConflictMarkersFromRestServiceFixture()
	{
		var baseContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("NewMode\\RestService_1_Remote", "base.json"));
		var localContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("NewMode\\RestService_1_Remote", "local.json"));
		var remoteContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("NewMode\\RestService_1_Remote", "remote.json"));
		var expected = File.ReadAllText(ResolverTestSupport.GetFixturePath("NewMode\\RestService_1_Remote", "resolved.json"));

		var result = ResolverTestSupport.Resolver.Resolve(new MergeRequest(
			ConflictFileType.MetadataJson,
			baseContent,
			localContent,
			remoteContent,
			null,
			MergeMode.Automerge));

		Assert.That(result.Status, Is.EqualTo(MergeStatus.AutoResolvedWithConflicts), result.ErrorMessage);
		Assert.That(result.MergedContent!, Is.EqualTo(expected));
	}

	[Test]
	[Description("Rejects duplicate semantic keys in a regular metadata Items array.")]
	public void MetadataMerge_DuplicateRootItems_ReturnsInvalidInput()
	{
		// Arrange
		const string baseContent = """{"Items":[{"UId":"x","value":"base"}]}""";
		const string localContent = """{"Items":[{"UId":"x","value":"local"}]}""";
		const string remoteContent = """{"Items":[{"UId":"x","value":"hidden"},{"UId":"x","value":"base"}]}""";

		// Act
		var result = ResolverTestSupport.Resolver.Resolve(new MergeRequest(
			ConflictFileType.MetadataJson,
			baseContent,
			localContent,
			remoteContent));

		// Assert
		Assert.That(result.Status, Is.EqualTo(MergeStatus.InvalidInput),
			"a duplicate regular JSON semantic key must fail closed instead of dropping one branch entry");
		Assert.That(result.ErrorCode, Is.EqualTo("DuplicateRemoteSemanticKey"),
			"the caller must know which stage contains the ambiguous semantic key");
	}

	private static (string BaseContent, string LocalContent, string RemoteContent) ReadGeneratedCase(string caseName)
	{
		var baseContent = File.ReadAllText(ResolverTestSupport.GetFixturePath(Path.Combine("TestCases", caseName), "base.json"));
		var localContent = File.ReadAllText(ResolverTestSupport.GetFixturePath(Path.Combine("TestCases", caseName), "local.json"));
		var remoteContent = File.ReadAllText(ResolverTestSupport.GetFixturePath(Path.Combine("TestCases", caseName), "remote.json"));
		return (baseContent, localContent, remoteContent);
	}

	private static JsonObject GetSchema(string mergedContent)
	{
		var root = JsonNode.Parse(mergedContent)!.AsObject();
		return root["MetaData"]!.AsObject()["Schema"]!.AsObject();
	}

	private static string GetStringValue(JsonObject obj, string propertyName)
	{
		return obj[propertyName]!.GetValue<string>();
	}

	private static JsonObject FindByUid(JsonArray array, string uid)
	{
		var found = FindByUidOrNull(array, uid);
		if (found is null)
		{
			throw new AssertionException($"Item with UId '{uid}' was not found.");
		}

		return found;
	}

	private static JsonObject? FindByUidOrNull(JsonArray array, string uid)
	{
		return FindByKeyOrNull(array, "UId", uid);
	}

	private static JsonObject FindByKey(JsonArray array, string keyPropertyName, string keyValue)
	{
		var found = FindByKeyOrNull(array, keyPropertyName, keyValue);
		if (found is null)
		{
			throw new AssertionException($"Item with {keyPropertyName} '{keyValue}' was not found.");
		}

		return found;
	}

	private static JsonObject? FindByKeyOrNull(JsonArray array, string keyPropertyName, string keyValue)
	{
		return array
			.OfType<JsonObject>()
			.FirstOrDefault(x =>
				x.TryGetPropertyValue(keyPropertyName, out var uidNode) &&
				uidNode is JsonValue jsonValue &&
				jsonValue.TryGetValue<string>(out var currentUid) &&
				string.Equals(currentUid, keyValue, StringComparison.OrdinalIgnoreCase));
	}

	private static void AssertJsonEqualIgnoringFormatting(string actual, string expected)
	{
		Assert.That(NormalizeJson(actual), Is.EqualTo(NormalizeJson(expected)));
	}

	private static string NormalizeJson(string json)
	{
		return JsonNode.Parse(json)!.ToJsonString();
	}
}
