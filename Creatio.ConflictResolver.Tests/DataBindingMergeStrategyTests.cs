using System.Text.Json.Nodes;
using Creatio.ConflictResolver.Tests.TestSupport;

namespace Creatio.ConflictResolver.Tests;

[TestFixture, Category("Unit")]
public class DataBindingMergeStrategyTests
{
	[Test]
	public void DataBindingMerge_PreservesUnicodeCharactersInMergedContent()
	{
		const string descriptor = """
		{
		  "Descriptor": {
		    "Columns": [
		      { "ColumnUId": "id-col", "IsKey": true },
		      { "ColumnUId": "name-col", "IsKey": false }
		    ]
		  }
		}
		""";
		const string baseContent = """{ "PackageData": [] }""";
		const string localContent = """{ "PackageData": [] }""";
		const string remoteContent = """
		{
		  "PackageData": [
		    { "Row": [
		      { "SchemaColumnUId": "id-col", "Value": "row-1" },
		      { "SchemaColumnUId": "name-col", "ColumnName": "Name", "Value": "Àäåëà¿äà" }
		    ]}
		  ]
		}
		""";

		using var temp = new TempDataFolder(descriptor);
		var result = Resolve(baseContent, localContent, remoteContent, temp.DataFilePath);

		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.Resolved));
		Assert.That(result.MergedContent, Does.Contain("Àäåëà¿äà"));
		Assert.That(result.MergedContent, Does.Not.Contain("\\u0410\\u0434\\u0435\\u043b\\u0430\\u0457\\u0434\\u0430"));
	}

	[Test]
	public void DataBindingMerge_LocalizedData_DescriptorResolvedFromParentFolder()
	{
		const string descriptor = """
		{
		  "Descriptor": {
		    "Columns": [
		      { "ColumnUId": "id-col", "IsKey": true },
		      { "ColumnUId": "name-col", "IsKey": false }
		    ]
		  }
		}
		""";
		const string baseContent = """{ "PackageData": [] }""";
		const string remoteContent = """{ "PackageData": [] }""";
		const string localContent = """
		{
		  "PackageData": [
		    { "Row": [
		      { "SchemaColumnUId": "id-col", "Value": "row-1" },
		      { "SchemaColumnUId": "name-col", "Value": "localized" }
		    ]}
		  ]
		}
		""";

		using var temp = new TempDataFolder(descriptor, Path.Combine("Localization", "data.json"));
		var result = Resolve(baseContent, localContent, remoteContent, temp.DataFilePath);

		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.Resolved));
		Assert.That(result.Report.TrueConflicts, Is.Empty);
		Assert.That(GetColumnValueAsString(GetSingleRow(result.MergedContent!), "name-col"), Is.EqualTo("localized"));
	}

	[Test]
	public void DataBindingMerge_LocalizedDataFile_UsesIdColumnAsRowKey()
	{
		const string descriptor = """
		{
		  "Descriptor": {
		    "Columns": []
		  }
		}
		""";
		const string baseContent = """{ "PackageData": [] }""";
		const string remoteContent = """{ "PackageData": [] }""";
		const string localContent = """
		{
		  "PackageData": [
		    { "Row": [
		      { "SchemaColumnUId": "entry-id", "ColumnName": "Id", "Value": "row-1" },
		      { "SchemaColumnUId": "caption-col", "ColumnName": "Caption", "Value": "localized" }
		    ]}
		  ]
		}
		""";

		using var temp = new TempDataFolder(descriptor, Path.Combine("Localization", "data.en-US.json"));
		var result = Resolve(baseContent, localContent, remoteContent, temp.DataFilePath);

		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.Resolved));
		Assert.That(result.Report.TrueConflicts, Is.Empty);
		Assert.That(GetPackageData(result.MergedContent!).Count, Is.EqualTo(1));
	}

	[Test]
	public void DataBindingMerge_LocalizedDataFile_UsesSingleGuidValueAsRowKey_WhenIdColumnMissing()
	{
		const string descriptor = """
		{
		  "Descriptor": {
		    "Columns": []
		  }
		}
		""";
		const string baseContent = """{ "PackageData": [] }""";
		const string remoteContent = """{ "PackageData": [] }""";
		const string localContent = """
		{
		  "PackageData": [
		    { "Row": [
		      { "SchemaColumnUId": "culture-col", "ColumnName": "Culture", "Value": "en-US" },
		      { "SchemaColumnUId": "entry-id", "ColumnName": "RecordUId", "Value": "5f5f1d4c-7e13-4d4c-8bf6-16bd6cb1d7d1" },
		      { "SchemaColumnUId": "caption-col", "ColumnName": "Caption", "Value": "localized" }
		    ]}
		  ]
		}
		""";

		using var temp = new TempDataFolder(descriptor, Path.Combine("Localization", "data.en-US.json"));
		var result = Resolve(baseContent, localContent, remoteContent, temp.DataFilePath);

		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.Resolved));
		Assert.That(result.Report.TrueConflicts, Is.Empty);
		Assert.That(GetPackageData(result.MergedContent!).Count, Is.EqualTo(1));
	}

	[Test]
	public void DataBindingMerge_LocalizedDataFile_MultipleGuidValues_ReturnsUnresolvedConflict()
	{
		const string descriptor = """
		{
		  "Descriptor": {
		    "Columns": []
		  }
		}
		""";
		const string baseContent = """{ "PackageData": [] }""";
		const string remoteContent = """{ "PackageData": [] }""";
		const string localContent = """
		{
		  "PackageData": [
		    { "Row": [
		      { "SchemaColumnUId": "entry-id-1", "ColumnName": "RecordUId", "Value": "5f5f1d4c-7e13-4d4c-8bf6-16bd6cb1d7d1" },
		      { "SchemaColumnUId": "entry-id-2", "ColumnName": "SysCultureId", "Value": "0ce1d49f-5336-4c3a-b6b7-c3fb3e1a45fd" },
		      { "SchemaColumnUId": "caption-col", "ColumnName": "Caption", "Value": "localized" }
		    ]}
		  ]
		}
		""";

		using var temp = new TempDataFolder(descriptor, Path.Combine("Localization", "data.en-US.json"));
		var result = Resolve(baseContent, localContent, remoteContent, temp.DataFilePath);

		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.UnresolvedConflict));
		Assert.That(result.ErrorCode, Is.EqualTo("DataBindingKeyResolutionFailed"));
		Assert.That(result.ErrorMessage, Does.Contain("ambiguous or missing key"));
	}

	[Test]
	public void DataBindingMerge_LocalizedDataFile_SameColumnChangedInBothBranches_ReturnsUnresolvedConflict()
	{
		const string descriptor = """
		{
		  "Descriptor": {
		    "Columns": []
		  }
		}
		""";
		const string baseContent = """
		{
		  "PackageData": [
		    { "Row": [
		      { "SchemaColumnUId": "entry-id", "ColumnName": "Id", "Value": "row-1" },
		      { "SchemaColumnUId": "caption-col", "ColumnName": "Name", "Value": "United States of America" }
		    ]}
		  ]
		}
		""";
		var localContent = baseContent.Replace("\"United States of America\"", "\"United States for America\"", StringComparison.Ordinal);
		var remoteContent = baseContent.Replace("\"United States of America\"", "\"United States of the Americas\"", StringComparison.Ordinal);

		using var temp = new TempDataFolder(descriptor, Path.Combine("Localization", "data.en-US.json"));
		var result = Resolve(baseContent, localContent, remoteContent, temp.DataFilePath);

		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.UnresolvedConflict));
		Assert.That(result.ErrorCode, Is.EqualTo("DataBindingLogicalConflict"));
		Assert.That(result.Report.TrueConflicts.Any(x => x.Contains("caption-col", StringComparison.Ordinal)), Is.True);
		Assert.That(GetColumnValueAsString(GetSingleRow(result.MergedContent!), "caption-col"), Is.EqualTo("United States for America"));
	}

	[Test]
	public void DataBindingMerge_DescriptorTooLarge_ReturnsInvalidInput()
	{
		var descriptor = "{\"Descriptor\":{\"Columns\":[]}}" + new string(' ', 1_048_577);
		const string content = """{ "PackageData": [] }""";

		using var temp = new TempDataFolder(descriptor);
		var result = Resolve(content, content, content, temp.DataFilePath);

		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.InvalidInput));
		Assert.That(result.ErrorCode, Is.EqualTo("InvalidDataDescriptor"));
		Assert.That(result.ErrorMessage, Does.Contain("maximum allowed size"));
	}

	[Test]
	public void DataBindingMerge_RowAddedInRemote_RowAppearsInResult()
	{
		const string descriptor = """
		{
		  "Descriptor": {
		    "Columns": [
		      { "ColumnUId": "id-col", "IsKey": true },
		      { "ColumnUId": "name-col", "IsKey": false }
		    ]
		  }
		}
		""";
		const string baseContent = """{ "PackageData": [] }""";
		const string localContent = """{ "PackageData": [] }""";
		const string remoteContent = """
		{
		  "PackageData": [
		    { "Row": [
		      { "SchemaColumnUId": "id-col", "Value": "row-1" },
		      { "SchemaColumnUId": "name-col", "Value": "remote" }
		    ]}
		  ]
		}
		""";

		using var temp = new TempDataFolder(descriptor);
		var result = Resolve(baseContent, localContent, remoteContent, temp.DataFilePath);

		Assert.That(result.Report.TrueConflicts, Is.Empty);
		Assert.That(GetPackageData(result.MergedContent!).Count, Is.EqualTo(1));
	}

	[Test]
	public void DataBindingMerge_RowAddedInLocal_RowAppearsInResult()
	{
		const string descriptor = """
		{
		  "Descriptor": {
		    "Columns": [
		      { "ColumnUId": "id-col", "IsKey": true },
		      { "ColumnUId": "name-col", "IsKey": false }
		    ]
		  }
		}
		""";
		const string baseContent = """{ "PackageData": [] }""";
		const string remoteContent = """{ "PackageData": [] }""";
		const string localContent = """
		{
		  "PackageData": [
		    { "Row": [
		      { "SchemaColumnUId": "id-col", "Value": "row-1" },
		      { "SchemaColumnUId": "name-col", "Value": "local" }
		    ]}
		  ]
		}
		""";

		using var temp = new TempDataFolder(descriptor);
		var result = Resolve(baseContent, localContent, remoteContent, temp.DataFilePath);

		Assert.That(result.Report.TrueConflicts, Is.Empty);
		Assert.That(GetPackageData(result.MergedContent!).Count, Is.EqualTo(1));
	}

	[Test]
	public void DataBindingMerge_RowDeletedInRemote_RowDeleted()
	{
		const string descriptor = """
		{
		  "Descriptor": {
		    "Columns": [
		      { "ColumnUId": "id-col", "IsKey": true },
		      { "ColumnUId": "name-col", "IsKey": false }
		    ]
		  }
		}
		""";
		const string baseContent = """
		{
		  "PackageData": [
		    { "Row": [
		      { "SchemaColumnUId": "id-col", "Value": "row-1" },
		      { "SchemaColumnUId": "name-col", "Value": "base" }
		    ]}
		  ]
		}
		""";
		const string localContent = baseContent;
		const string remoteContent = """{ "PackageData": [] }""";

		using var temp = new TempDataFolder(descriptor);
		var result = Resolve(baseContent, localContent, remoteContent, temp.DataFilePath);

		Assert.That(result.Report.TrueConflicts, Is.Empty);
		Assert.That(GetPackageData(result.MergedContent!).Count, Is.EqualTo(0));
	}

	[Test]
	public void DataBindingMerge_RowDeletedInLocal_RowDeleted_WithConflict()
	{
		const string descriptor = """
		{
		  "Descriptor": {
		    "Columns": [
		      { "ColumnUId": "id-col", "IsKey": true },
		      { "ColumnUId": "name-col", "IsKey": false }
		    ]
		  }
		}
		""";
		const string baseContent = """
		{
		  "PackageData": [
		    { "Row": [
		      { "SchemaColumnUId": "id-col", "Value": "row-1" },
		      { "SchemaColumnUId": "name-col", "Value": "base" }
		    ]}
		  ]
		}
		""";
		const string localContent = """{ "PackageData": [] }""";
		const string remoteContent = baseContent;

		using var temp = new TempDataFolder(descriptor);
		var result = Resolve(baseContent, localContent, remoteContent, temp.DataFilePath);

		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.UnresolvedConflict));
		Assert.That(result.Report.TrueConflicts.Any(x => x.Contains("row-1", StringComparison.Ordinal)), Is.True);
		Assert.That(GetPackageData(result.MergedContent!).Count, Is.EqualTo(0));
	}

	[Test]
	public void DataBindingMerge_LocalDeletedRemoteChanged_RowCopiedFromRemote()
	{
		const string descriptor = """
		{
		  "Descriptor": {
		    "Columns": [
		      { "ColumnUId": "id-col", "IsKey": true },
		      { "ColumnUId": "name-col", "IsKey": false }
		    ]
		  }
		}
		""";
		const string baseContent = """
		{
		  "PackageData": [
		    { "Row": [
		      { "SchemaColumnUId": "id-col", "Value": "row-1" },
		      { "SchemaColumnUId": "name-col", "Value": "base" }
		    ]}
		  ]
		}
		""";
		const string localContent = """{ "PackageData": [] }""";
		var remoteContent = baseContent.Replace("\"base\"", "\"remote\"", StringComparison.Ordinal);

		using var temp = new TempDataFolder(descriptor);
		var result = Resolve(baseContent, localContent, remoteContent, temp.DataFilePath);

		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.UnresolvedConflict));
		Assert.That(result.Report.TrueConflicts.Any(x => x.Contains("row-1", StringComparison.Ordinal)), Is.True);
		Assert.That(GetColumnValueAsString(GetSingleRow(result.MergedContent!), "name-col"), Is.EqualTo("remote"));
	}

	[Test]
	public void DataBindingMerge_RemoteDeletedLocalChanged_RowCopiedFromLocal_WithConflict()
	{
		const string descriptor = """
		{
		  "Descriptor": {
		    "Columns": [
		      { "ColumnUId": "id-col", "IsKey": true },
		      { "ColumnUId": "name-col", "IsKey": false }
		    ]
		  }
		}
		""";
		const string baseContent = """
		{
		  "PackageData": [
		    { "Row": [
		      { "SchemaColumnUId": "id-col", "Value": "row-1" },
		      { "SchemaColumnUId": "name-col", "Value": "base" }
		    ]}
		  ]
		}
		""";
		const string remoteContent = """{ "PackageData": [] }""";
		var localContent = baseContent.Replace("\"base\"", "\"local\"", StringComparison.Ordinal);

		using var temp = new TempDataFolder(descriptor);
		var result = Resolve(baseContent, localContent, remoteContent, temp.DataFilePath);

		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.UnresolvedConflict));
		Assert.That(result.Report.TrueConflicts.Any(x => x.Contains("row-1", StringComparison.Ordinal)), Is.True);
		Assert.That(GetColumnValueAsString(GetSingleRow(result.MergedContent!), "name-col"), Is.EqualTo("local"));
	}

	[Test]
	public void DataBindingMerge_RowChangedInRemote_RemoteValuesApplied()
	{
		const string descriptor = """
		{
		  "Descriptor": {
		    "Columns": [
		      { "ColumnUId": "id-col", "IsKey": true },
		      { "ColumnUId": "name-col", "IsKey": false }
		    ]
		  }
		}
		""";
		const string baseContent = """
		{
		  "PackageData": [
		    { "Row": [
		      { "SchemaColumnUId": "id-col", "Value": "row-1" },
		      { "SchemaColumnUId": "name-col", "Value": "base" }
		    ]}
		  ]
		}
		""";
		const string localContent = baseContent;
		var remoteContent = baseContent.Replace("\"base\"", "\"remote\"", StringComparison.Ordinal);

		using var temp = new TempDataFolder(descriptor);
		var result = Resolve(baseContent, localContent, remoteContent, temp.DataFilePath);

		Assert.That(result.Report.TrueConflicts, Is.Empty);
		Assert.That(GetColumnValueAsString(GetSingleRow(result.MergedContent!), "name-col"), Is.EqualTo("remote"));
	}

	[Test]
	public void DataBindingMerge_RowChangedInLocal_LocalValuesApplied()
	{
		const string descriptor = """
		{
		  "Descriptor": {
		    "Columns": [
		      { "ColumnUId": "id-col", "IsKey": true },
		      { "ColumnUId": "name-col", "IsKey": false }
		    ]
		  }
		}
		""";
		const string baseContent = """
		{
		  "PackageData": [
		    { "Row": [
		      { "SchemaColumnUId": "id-col", "Value": "row-1" },
		      { "SchemaColumnUId": "name-col", "Value": "base" }
		    ]}
		  ]
		}
		""";
		var localContent = baseContent.Replace("\"base\"", "\"local\"", StringComparison.Ordinal);
		const string remoteContent = baseContent;

		using var temp = new TempDataFolder(descriptor);
		var result = Resolve(baseContent, localContent, remoteContent, temp.DataFilePath);

		Assert.That(result.Report.TrueConflicts, Is.Empty);
		Assert.That(GetColumnValueAsString(GetSingleRow(result.MergedContent!), "name-col"), Is.EqualTo("local"));
	}

	[Test]
	public void DataBindingMerge_RowChangedInLocalAndRemote_DifferentColumns_CompositeKey_ChangesCombined()
	{
		const string descriptor = """
		{
		  "Descriptor": {
		    "Columns": [
		      { "ColumnUId": "id-col-1", "IsKey": true },
		      { "ColumnUId": "id-col-2", "IsKey": true },
		      { "ColumnUId": "name-col", "IsKey": false },
		      { "ColumnUId": "description-col", "IsKey": false }
		    ]
		  }
		}
		""";
		const string baseContent = """
		{
		  "PackageData": [
		    { "Row": [
		      { "SchemaColumnUId": "id-col-1", "Value": "row" },
		      { "SchemaColumnUId": "id-col-2", "Value": "1" },
		      { "SchemaColumnUId": "name-col", "Value": "base-name" },
		      { "SchemaColumnUId": "description-col", "Value": "base-description" }
		    ]}
		  ]
		}
		""";
		var localContent = baseContent.Replace("\"base-name\"", "\"local-name\"", StringComparison.Ordinal);
		var remoteContent = baseContent.Replace("\"base-description\"", "\"remote-description\"", StringComparison.Ordinal);

		using var temp = new TempDataFolder(descriptor);
		var result = Resolve(baseContent, localContent, remoteContent, temp.DataFilePath);

		var row = GetSingleRow(result.MergedContent!);
		Assert.That(result.Report.TrueConflicts, Is.Empty);
		Assert.That(GetColumnValueAsString(row, "name-col"), Is.EqualTo("local-name"));
		Assert.That(GetColumnValueAsString(row, "description-col"), Is.EqualTo("remote-description"));
	}

	[Test]
	public void DataBindingMerge_RowChangedInLocalAndRemote_SameColumn_CompositeKey_LocalWinsWithConflict()
	{
		const string descriptor = """
		{
		  "Descriptor": {
		    "Columns": [
		      { "ColumnUId": "id-col-1", "IsKey": true },
		      { "ColumnUId": "id-col-2", "IsKey": true },
		      { "ColumnUId": "name-col", "IsKey": false }
		    ]
		  }
		}
		""";
		const string baseContent = """
		{
		  "PackageData": [
		    { "Row": [
		      { "SchemaColumnUId": "id-col-1", "Value": "row" },
		      { "SchemaColumnUId": "id-col-2", "Value": "1" },
		      { "SchemaColumnUId": "name-col", "Value": "base" }
		    ]}
		  ]
		}
		""";
		var localContent = baseContent.Replace("\"base\"", "\"local\"", StringComparison.Ordinal);
		var remoteContent = baseContent.Replace("\"base\"", "\"remote\"", StringComparison.Ordinal);

		using var temp = new TempDataFolder(descriptor);
		var result = Resolve(baseContent, localContent, remoteContent, temp.DataFilePath);

		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.UnresolvedConflict));
		Assert.That(result.ErrorCode, Is.EqualTo("DataBindingLogicalConflict"));
		Assert.That(result.Report.TrueConflicts.Any(x => x.Contains("name-col", StringComparison.Ordinal)), Is.True);
		Assert.That(GetColumnValueAsString(GetSingleRow(result.MergedContent!), "name-col"), Is.EqualTo("local"));
	}

	[Test]
	public void DataBindingMerge_AutomergeMode_EmitsConflictMarkersForChangedColumn()
	{
		const string descriptor = """
		{
		  "Descriptor": {
		    "Columns": [
		      { "ColumnUId": "id-col-1", "IsKey": true },
		      { "ColumnUId": "id-col-2", "IsKey": true },
		      { "ColumnUId": "name-col", "IsKey": false }
		    ]
		  }
		}
		""";
		const string baseContent = """
		{
		  "PackageData": [
		    { "Row": [
		      { "SchemaColumnUId": "id-col-1", "Value": "row" },
		      { "SchemaColumnUId": "id-col-2", "Value": "1" },
		      { "SchemaColumnUId": "name-col", "Value": "base" }
		    ]}
		  ]
		}
		""";
		var localContent = baseContent.Replace("\"base\"", "\"local\"", StringComparison.Ordinal);
		var remoteContent = baseContent.Replace("\"base\"", "\"remote\"", StringComparison.Ordinal);

		using var temp = new TempDataFolder(descriptor);
		var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
			global::Creatio.ConflictResolver.ConflictFileType.DataBinding,
			baseContent,
			localContent,
			remoteContent,
			temp.DataFilePath,
			global::Creatio.ConflictResolver.MergeMode.Automerge));
		var normalized = ResolverTestSupport.NormalizeLineEndings(result.MergedContent!);

		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.AutoResolvedWithConflicts), result.ErrorMessage);
		Assert.That(result.Report.WinnerPolicy, Is.EqualTo("CONFLICT_MARKERS"));
		Assert.That(normalized, Does.Contain("\"SchemaColumnUId\": \"name-col\",\n<<<<<<< Local\n          \"Value\": \"local\"\n=======\n          \"Value\": \"remote\"\n>>>>>>> Remote"));
		Assert.That(normalized, Does.Not.Contain("\n        <<<<<<< Local\n"));
	}

	[Test]
	public void DataBindingMerge_AutomergeMode_EmitsConflictMarkersFromFixture()
	{
		var descriptorContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("NewMode\\dataBinding", "descriptor.json"));
		var baseContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("NewMode\\dataBinding", "base.json"));
		var localContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("NewMode\\dataBinding", "local.json"));
		var remoteContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("NewMode\\dataBinding", "remote.json"));
		var expected = File.ReadAllText(ResolverTestSupport.GetFixturePath("NewMode\\dataBinding", "resolved.json"));

		using var temp = new TempDataFolder(descriptorContent);
		var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
			global::Creatio.ConflictResolver.ConflictFileType.DataBinding,
			baseContent,
			localContent,
			remoteContent,
			temp.DataFilePath,
			global::Creatio.ConflictResolver.MergeMode.Automerge));
		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.AutoResolvedWithConflicts), result.ErrorMessage);

		Assert.That(result.MergedContent!, Is.EqualTo(expected));
	}

	[Test]
	public void DataBindingMerge_ReordersRowsToMatchBaseAndBranchInsertions()
	{
		const string descriptor = """
		{
		  "Descriptor": {
		    "Columns": [
		      { "ColumnUId": "id-col", "IsKey": true },
		      { "ColumnUId": "name-col", "IsKey": false }
		    ]
		  }
		}
		""";
		var baseContent = CreatePackageDataJson("A", "Ö", "C", "Ì");
		var localContent = CreatePackageDataJson("A", "NNNNNN", "Ö", "C", "Ì");
		var remoteContent = CreatePackageDataJson("A", "Ö", "C", "WWWWWW", "Ì");

		using var temp = new TempDataFolder(descriptor);
		var result = Resolve(baseContent, localContent, remoteContent, temp.DataFilePath);

		Assert.That(ReadRowKeys(result.MergedContent!), Is.EqualTo(new[] { "A", "NNNNNN", "Ö", "C", "WWWWWW", "Ì" }));
	}

	[Test]
	public void DataBindingMerge_ReordersConcurrentInsertionsInSameGap_ByKey()
	{
		const string descriptor = """
		{
		  "Descriptor": {
		    "Columns": [
		      { "ColumnUId": "id-col", "IsKey": true },
		      { "ColumnUId": "name-col", "IsKey": false }
		    ]
		  }
		}
		""";
		var baseContent = CreatePackageDataJson("A", "C");
		var localContent = CreatePackageDataJson("A", "Z", "C");
		var remoteContent = CreatePackageDataJson("A", "B", "C");

		using var temp = new TempDataFolder(descriptor);
		var result = Resolve(baseContent, localContent, remoteContent, temp.DataFilePath);

		Assert.That(ReadRowKeys(result.MergedContent!), Is.EqualTo(new[] { "A", "B", "Z", "C" }));
	}

	private static IReadOnlyList<string> ReadRowKeys(string mergedContent)
	{
		return GetPackageData(mergedContent)
			.OfType<JsonObject>()
			.Select(static row => GetColumnValueAsString(row, "id-col"))
			.ToArray();
	}

	private static string CreatePackageDataJson(params string[] rowKeys)
	{
		var rows = new JsonArray();
		foreach (var rowKey in rowKeys)
		{
			rows.Add(new JsonObject
			{
				["Row"] = new JsonArray
				{
					new JsonObject
					{
						["SchemaColumnUId"] = "id-col",
						["Value"] = rowKey
					},
					new JsonObject
					{
						["SchemaColumnUId"] = "name-col",
						["Value"] = $"name-{rowKey}"
					}
				}
			});
		}

		return new JsonObject
		{
			["PackageData"] = rows
		}.ToJsonString();
	}

	private static global::Creatio.ConflictResolver.MergeResult Resolve(string @base, string local, string remote, string path)
	{
		return ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(@base, local, remote, path));
	}

	private static JsonArray GetPackageData(string mergedContent)
	{
		return JsonNode.Parse(mergedContent)!["PackageData"]!.AsArray();
	}

	private static JsonObject GetSingleRow(string mergedContent)
	{
		var rows = GetPackageData(mergedContent);
		Assert.That(rows.Count, Is.EqualTo(1));
		return rows[0]!.AsObject();
	}

	private static string GetColumnValueAsString(JsonObject rowContainer, string schemaColumnUid)
	{
		return GetColumn(rowContainer, schemaColumnUid)["Value"]!.GetValue<string>();
	}

	private static JsonObject GetColumn(JsonObject rowContainer, string schemaColumnUid)
	{
		var row = rowContainer["Row"]!.AsArray();
		return row.OfType<JsonObject>().First(x => x["SchemaColumnUId"]!.GetValue<string>() == schemaColumnUid);
	}

	private sealed class TempDataFolder : IDisposable
	{
		private readonly string _path;

		public TempDataFolder(string descriptorContent, string dataFileName = "data.json")
		{
			_path = Path.Combine(Path.GetTempPath(), "CreatioConflictResolverDataTests", Guid.NewGuid().ToString("N"));
			try
			{
				Directory.CreateDirectory(_path);
				File.WriteAllText(Path.Combine(_path, "descriptor.json"), descriptorContent);
				DataFilePath = Path.Combine(_path, dataFileName);
				var dataDirectory = Path.GetDirectoryName(DataFilePath);
				if (!string.IsNullOrWhiteSpace(dataDirectory))
				{
					Directory.CreateDirectory(dataDirectory);
				}
			}
			catch
			{
				try
				{
					if (Directory.Exists(_path))
					{
						Directory.Delete(_path, true);
					}
				}
				catch
				{
				}

				throw;
			}
		}

		public string DataFilePath { get; }

		public void Dispose()
		{
			if (Directory.Exists(_path))
			{
				Directory.Delete(_path, true);
			}
		}
	}
}

