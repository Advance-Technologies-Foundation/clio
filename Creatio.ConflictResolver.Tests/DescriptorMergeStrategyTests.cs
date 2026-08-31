using System.Text.Json.Nodes;
using Creatio.ConflictResolver.Tests.TestSupport;

namespace Creatio.ConflictResolver.Tests;

[TestFixture, Category("Unit")]
public class DescriptorMergeStrategyTests
{
	[Test]
	public void DescriptorMerge_FixtureCase1_MatchesExpectedResolvedDescriptor()
	{
		var baseContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("DescriptorCase1", "baseDescriptor1.json"));
		var localContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("DescriptorCase1", "localDescriptor1.json"));
		var remoteContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("DescriptorCase1", "remoteDescriptor1.json"));
		var expected = File.ReadAllText(ResolverTestSupport.GetFixturePath("DescriptorCase1", "resolvedDescriptor1.json"));

		var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
			global::Creatio.ConflictResolver.ConflictFileType.DescriptorJson,
			baseContent,
			localContent,
			remoteContent));

		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.Resolved), result.ErrorMessage);
		Assert.That(result.Report.WinnerPolicy, Is.EqualTo("REMOTE"));
		Assert.That(
			ResolverTestSupport.NormalizeLineEndings(result.MergedContent!),
			Is.EqualTo(ResolverTestSupport.NormalizeLineEndings(expected)));
	}
	
	[Test]
	public void DescriptorMerge_FixtureCase2_MatchesExpectedResolvedDescriptor()
	{
		var baseContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("DescriptorCase2", "baseDescriptor1.json"));
		var localContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("DescriptorCase2", "localDescriptor1.json"));
		var remoteContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("DescriptorCase2", "remoteDescriptor1.json"));
		var expected = File.ReadAllText(ResolverTestSupport.GetFixturePath("DescriptorCase2", "resolvedDescriptor1.json"));

		var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
			global::Creatio.ConflictResolver.ConflictFileType.DescriptorJson,
			baseContent,
			localContent,
			remoteContent));

		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.Resolved), result.ErrorMessage);
		Assert.That(result.Report.WinnerPolicy, Is.EqualTo("REMOTE"));
		Assert.That(
			ResolverTestSupport.NormalizeLineEndings(result.MergedContent!),
			Is.EqualTo(ResolverTestSupport.NormalizeLineEndings(expected)));
	}

	[Test]
	public void DescriptorMerge_NewerTimestampWins()
	{
		var baseContent = ResolverTestSupport.BuildDescriptor(100);
		var localContent = ResolverTestSupport.BuildDescriptor(200);
		var remoteContent = ResolverTestSupport.BuildDescriptor(150);

		var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
			global::Creatio.ConflictResolver.ConflictFileType.DescriptorJson,
			baseContent,
			localContent,
			remoteContent));

		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.Resolved));
		Assert.That(result.MergedContent, Is.EqualTo(localContent));
		Assert.That(result.Report.WinnerPolicy, Is.EqualTo("LOCAL"));
	}

	[Test]
	public void DescriptorMerge_InvalidTimestamp_ReturnsInvalidInput()
	{
		var baseContent = ResolverTestSupport.BuildDescriptor(100);
		var localContent = ResolverTestSupport.BuildDescriptor(200);
		var remoteContent = ResolverTestSupport.BuildDescriptor("not-a-timestamp");

		var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
			global::Creatio.ConflictResolver.ConflictFileType.DescriptorJson,
			baseContent,
			localContent,
			remoteContent));

		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.InvalidInput));
	}

	[Test]
	public void DescriptorMerge_DataBindingDescriptor_DifferentSchemaUid_ReturnsUnresolvedConflict()
	{
		const string baseContent = """
		{
		  "Descriptor": {
		    "UId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
		    "Name": "SysAdminOperation_CanReset2FA",
		    "ModifiedOnUtc": "/Date(1000)/",
		    "ManagerName": "EntitySchemaManager",
		    "Schema": {
		      "UId": "schema-1",
		      "Name": "SysAdminOperation"
		    },
		    "Columns": [
		      {
		        "ColumnUId": "col-1",
		        "IsKey": true,
		        "DataTypeValueUId": "dt-1"
		      }
		    ]
		  }
		}
		""";

		const string localContent = """
		{
		  "Descriptor": {
		    "UId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
		    "Name": "SysAdminOperation_CanReset2FA",
		    "ModifiedOnUtc": "/Date(2000)/",
		    "ManagerName": "EntitySchemaManager",
		    "Schema": {
		      "UId": "schema-local",
		      "Name": "SysAdminOperation"
		    },
		    "Columns": [
		      {
		        "ColumnUId": "col-1",
		        "IsKey": true,
		        "DataTypeValueUId": "dt-1"
		      }
		    ]
		  }
		}
		""";

		const string remoteContent = """
		{
		  "Descriptor": {
		    "UId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
		    "Name": "SysAdminOperation_CanReset2FA",
		    "ModifiedOnUtc": "/Date(1500)/",
		    "ManagerName": "EntitySchemaManager",
		    "Schema": {
		      "UId": "schema-remote",
		      "Name": "SysAdminOperation"
		    },
		    "Columns": [
		      {
		        "ColumnUId": "col-1",
		        "IsKey": true,
		        "DataTypeValueUId": "dt-1"
		      }
		    ]
		  }
		}
		""";

		var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
			global::Creatio.ConflictResolver.ConflictFileType.DescriptorJson,
			baseContent,
			localContent,
			remoteContent,
			"Pkg/Data/descriptor.json"));

		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.UnresolvedConflict));
		Assert.That(result.ErrorCode, Is.EqualTo("DataBindingDescriptorConflict"));
		Assert.That(result.Report.TrueConflicts, Does.Contain("Schema.UId"));
	}

	[Test]
	public void DescriptorMerge_DataBindingDescriptor_DifferentColumnsShape_ReturnsUnresolvedConflict()
	{
		const string baseContent = """
		{
		  "Descriptor": {
		    "UId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
		    "Name": "SysAdminOperation_CanReset2FA",
		    "ModifiedOnUtc": "/Date(1000)/",
		    "ManagerName": "EntitySchemaManager",
		    "Schema": {
		      "UId": "schema-1",
		      "Name": "SysAdminOperation"
		    },
		    "Columns": [
		      {
		        "ColumnUId": "col-1",
		        "IsKey": true,
		        "DataTypeValueUId": "dt-1"
		      }
		    ]
		  }
		}
		""";

		const string localContent = """
		{
		  "Descriptor": {
		    "UId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
		    "Name": "SysAdminOperation_CanReset2FA",
		    "ModifiedOnUtc": "/Date(2000)/",
		    "ManagerName": "EntitySchemaManager",
		    "Schema": {
		      "UId": "schema-1",
		      "Name": "SysAdminOperation"
		    },
		    "Columns": [
		      {
		        "ColumnUId": "col-1",
		        "IsKey": false,
		        "DataTypeValueUId": "dt-1"
		      },
		      {
		        "ColumnUId": "col-2",
		        "IsKey": false,
		        "DataTypeValueUId": "dt-2"
		      }
		    ]
		  }
		}
		""";

		const string remoteContent = """
		{
		  "Descriptor": {
		    "UId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
		    "Name": "SysAdminOperation_CanReset2FA",
		    "ModifiedOnUtc": "/Date(1500)/",
		    "ManagerName": "EntitySchemaManager",
		    "Schema": {
		      "UId": "schema-1",
		      "Name": "SysAdminOperation"
		    },
		    "Columns": [
		      {
		        "ColumnUId": "col-1",
		        "IsKey": true,
		        "DataTypeValueUId": "dt-9"
		      }
		    ]
		  }
		}
		""";

		var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
			global::Creatio.ConflictResolver.ConflictFileType.DescriptorJson,
			baseContent,
			localContent,
			remoteContent,
			"Pkg/Data/descriptor.json"));

		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.UnresolvedConflict));
		Assert.That(result.ErrorCode, Is.EqualTo("DataBindingDescriptorConflict"));
		Assert.That(result.Report.TrueConflicts, Does.Contain("Columns.Count"));
		Assert.That(result.Report.TrueConflicts, Does.Contain("Columns.UId"));
		Assert.That(result.Report.TrueConflicts.Any(x => x.Contains("IsKey", StringComparison.Ordinal)), Is.True);
		Assert.That(result.Report.TrueConflicts.Any(x => x.Contains("DataTypeValueUId", StringComparison.Ordinal)), Is.True);
	}

	[Test]
	public void DescriptorMerge_InstallTypeChanged_NewerTimestampWins()
	{
		const string baseContent = """
		{
		  "Descriptor": {
		    "UId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
		    "Name": "TestSchema",
		    "ModifiedOnUtc": "/Date(1000)/",
		    "ManagerName": "EntitySchemaManager",
		    "InstallType": 0,
		    "Caption": "Test"
		  }
		}
		""";

		const string localContent = """
		{
		  "Descriptor": {
		    "UId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
		    "Name": "TestSchema",
		    "ModifiedOnUtc": "/Date(2000)/",
		    "ManagerName": "EntitySchemaManager",
		    "InstallType": 1,
		    "Caption": "Test"
		  }
		}
		""";

		const string remoteContent = """
		{
		  "Descriptor": {
		    "UId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
		    "Name": "TestSchema",
		    "ModifiedOnUtc": "/Date(3000)/",
		    "ManagerName": "EntitySchemaManager",
		    "InstallType": 2,
		    "Caption": "Test"
		  }
		}
		""";

		var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
			global::Creatio.ConflictResolver.ConflictFileType.DescriptorJson,
			baseContent,
			localContent,
			remoteContent));

		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.Resolved), result.ErrorMessage);
		Assert.That(result.Report.WinnerPolicy, Is.EqualTo("REMOTE"));
		Assert.That(result.MergedContent, Is.EqualTo(remoteContent));
	}

	[Test]
	public void DescriptorMerge_DataBindingDescriptor_MultiColumnChanges_AppliesBothChanges()
	{
		const string baseContent = """
		{
		  "Descriptor": {
		    "UId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
		    "Name": "SysAdminOperation_CanReset2FA",
		    "ModifiedOnUtc": "/Date(1000)/",
		    "ManagerName": "EntitySchemaManager",
		    "Schema": {
		      "UId": "schema-1",
		      "Name": "SysAdminOperation"
		    },
		    "Columns": [
		      {
		        "ColumnUId": "col-1",
		        "IsForceUpdate": false,
		        "IsKey": true,
		        "ColumnName": "Code",
		        "DataTypeValueUId": "dt-1"
		      },
		      {
		        "ColumnUId": "col-2",
		        "IsForceUpdate": false,
		        "IsKey": false,
		        "ColumnName": "Name",
		        "DataTypeValueUId": "dt-2"
		      }
		    ]
		  }
		}
		""";

		const string localContent = """
		{
		  "Descriptor": {
		    "UId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
		    "Name": "SysAdminOperation_CanReset2FA",
		    "ModifiedOnUtc": "/Date(2000)/",
		    "ManagerName": "EntitySchemaManager",
		    "Schema": {
		      "UId": "schema-1",
		      "Name": "SysAdminOperation"
		    },
		    "Columns": [
		      {
		        "ColumnUId": "col-1",
		        "IsForceUpdate": true,
		        "IsKey": true,
		        "ColumnName": "Code",
		        "DataTypeValueUId": "dt-1"
		      },
		      {
		        "ColumnUId": "col-2",
		        "IsForceUpdate": false,
		        "IsKey": false,
		        "ColumnName": "Name",
		        "DataTypeValueUId": "dt-2"
		      }
		    ]
		  }
		}
		""";

		const string remoteContent = """
		{
		  "Descriptor": {
		    "UId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
		    "Name": "SysAdminOperation_CanReset2FA",
		    "ModifiedOnUtc": "/Date(3000)/",
		    "ManagerName": "EntitySchemaManager",
		    "Schema": {
		      "UId": "schema-1",
		      "Name": "SysAdminOperation"
		    },
		    "Columns": [
		      {
		        "ColumnUId": "col-1",
		        "IsForceUpdate": false,
		        "IsKey": true,
		        "ColumnName": "Code",
		        "DataTypeValueUId": "dt-1"
		      },
		      {
		        "ColumnUId": "col-2",
		        "IsForceUpdate": false,
		        "IsKey": false,
		        "ColumnName": "DisplayName",
		        "DataTypeValueUId": "dt-2"
		      }
		    ]
		  }
		}
		""";

		var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
			global::Creatio.ConflictResolver.ConflictFileType.DescriptorJson,
			baseContent,
			localContent,
			remoteContent,
			"Pkg/Data/descriptor.json"));

		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.Resolved), result.ErrorMessage);

		var root = JsonNode.Parse(result.MergedContent!)!.AsObject();
		var columns = root["Descriptor"]!["Columns"]!.AsArray();
		var col1 = columns.First(x => x!["ColumnUId"]!.GetValue<string>() == "col-1")!.AsObject();
		var col2 = columns.First(x => x!["ColumnUId"]!.GetValue<string>() == "col-2")!.AsObject();

		Assert.That(col1["IsForceUpdate"]!.GetValue<bool>(), Is.True);
		Assert.That(col2["ColumnName"]!.GetValue<string>(), Is.EqualTo("DisplayName"));
	}

	[Test]
	public void DescriptorMerge_DataBindingDescriptor_SameColumnDifferentAllowedProperties_AppliesBothChanges()
	{
		const string baseContent = """
		{
		  "Descriptor": {
		    "UId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
		    "Name": "SysAdminOperation_CanReset2FA",
		    "ModifiedOnUtc": "/Date(1000)/",
		    "ManagerName": "EntitySchemaManager",
		    "Schema": {
		      "UId": "schema-1",
		      "Name": "SysAdminOperation"
		    },
		    "Columns": [
		      {
		        "ColumnUId": "col-1",
		        "IsForceUpdate": false,
		        "IsKey": true,
		        "ColumnName": "Code",
		        "DataTypeValueUId": "dt-1"
		      },
		      {
		        "ColumnUId": "col-2",
		        "IsForceUpdate": false,
		        "IsKey": false,
		        "ColumnName": "Name",
		        "DataTypeValueUId": "dt-2"
		      }
		    ]
		  }
		}
		""";

		const string localContent = """
		{
		  "Descriptor": {
		    "UId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
		    "Name": "SysAdminOperation_CanReset2FA",
		    "ModifiedOnUtc": "/Date(2000)/",
		    "ManagerName": "EntitySchemaManager",
		    "Schema": {
		      "UId": "schema-1",
		      "Name": "SysAdminOperation"
		    },
		    "Columns": [
		      {
		        "ColumnUId": "col-1",
		        "IsForceUpdate": true,
		        "IsKey": true,
		        "ColumnName": "Code",
		        "DataTypeValueUId": "dt-1"
		      },
		      {
		        "ColumnUId": "col-2",
		        "IsForceUpdate": false,
		        "IsKey": false,
		        "ColumnName": "Name",
		        "DataTypeValueUId": "dt-2"
		      }
		    ]
		  }
		}
		""";

		const string remoteContent = """
		{
		  "Descriptor": {
		    "UId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
		    "Name": "SysAdminOperation_CanReset2FA",
		    "ModifiedOnUtc": "/Date(3000)/",
		    "ManagerName": "EntitySchemaManager",
		    "Schema": {
		      "UId": "schema-1",
		      "Name": "SysAdminOperation"
		    },
		    "Columns": [
		      {
		        "ColumnUId": "col-1",
		        "IsForceUpdate": false,
		        "IsKey": true,
		        "ColumnName": "Code",
		        "DataTypeValueUId": "dt-1"
		      },
		      {
		        "ColumnUId": "col-2",
		        "IsForceUpdate": false,
		        "IsKey": false,
		        "ColumnName": "DisplayName",
		        "DataTypeValueUId": "dt-2"
		      }
		    ]
		  }
		}
		""";

		var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
			global::Creatio.ConflictResolver.ConflictFileType.DescriptorJson,
			baseContent,
			localContent,
			remoteContent,
			"Pkg/Data/descriptor.json"));

		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.Resolved), result.ErrorMessage);

		var root = JsonNode.Parse(result.MergedContent!)!.AsObject();
		var columns = root["Descriptor"]!["Columns"]!.AsArray();
		var col1 = columns.First(x => x!["ColumnUId"]!.GetValue<string>() == "col-1")!.AsObject();
		var col2 = columns.First(x => x!["ColumnUId"]!.GetValue<string>() == "col-2")!.AsObject();

		Assert.That(col1["IsForceUpdate"]!.GetValue<bool>(), Is.True);
		Assert.That(col2["ColumnName"]!.GetValue<string>(), Is.EqualTo("DisplayName"));
	}

	[Test]
	public void DescriptorMerge_DataBindingDescriptor_SameColumnNameChangedInBoth_LaterLocalWins()
	{
		const string baseContent = """
		{
		  "Descriptor": {
		    "UId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
		    "Name": "SysAdminOperation_CanReset2FA",
		    "ModifiedOnUtc": "/Date(1000)/",
		    "ManagerName": "EntitySchemaManager",
		    "Schema": {
		      "UId": "schema-1",
		      "Name": "SysAdminOperation"
		    },
		    "Columns": [
		      {
		        "ColumnUId": "col-1",
		        "IsKey": true,
		        "ColumnName": "Code",
		        "DataTypeValueUId": "dt-1"
		      }
		    ]
		  }
		}
		""";

		const string localContent = """
		{
		  "Descriptor": {
		    "UId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
		    "Name": "SysAdminOperation_CanReset2FA",
		    "ModifiedOnUtc": "/Date(3000)/",
		    "ManagerName": "EntitySchemaManager",
		    "Schema": {
		      "UId": "schema-1",
		      "Name": "SysAdminOperation"
		    },
		    "Columns": [
		      {
		        "ColumnUId": "col-1",
		        "IsKey": true,
		        "ColumnName": "LocalCode",
		        "DataTypeValueUId": "dt-1"
		      }
		    ]
		  }
		}
		""";

		const string remoteContent = """
		{
		  "Descriptor": {
		    "UId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
		    "Name": "SysAdminOperation_CanReset2FA",
		    "ModifiedOnUtc": "/Date(2000)/",
		    "ManagerName": "EntitySchemaManager",
		    "Schema": {
		      "UId": "schema-1",
		      "Name": "SysAdminOperation"
		    },
		    "Columns": [
		      {
		        "ColumnUId": "col-1",
		        "IsKey": true,
		        "ColumnName": "RemoteCode",
		        "DataTypeValueUId": "dt-1"
		      }
		    ]
		  }
		}
		""";

		var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
			global::Creatio.ConflictResolver.ConflictFileType.DescriptorJson,
			baseContent,
			localContent,
			remoteContent,
			"Pkg/Data/descriptor.json"));

		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.Resolved), result.ErrorMessage);
		Assert.That(result.Report.WinnerPolicy, Is.EqualTo("LOCAL"));

		var root = JsonNode.Parse(result.MergedContent!)!.AsObject();
		var columns = root["Descriptor"]!["Columns"]!.AsArray();
		var col1 = columns.First(x => x!["ColumnUId"]!.GetValue<string>() == "col-1")!.AsObject();
		Assert.That(col1["ColumnName"]!.GetValue<string>(), Is.EqualTo("LocalCode"));
	}

	[Test]
	public void DescriptorMerge_DataBindingDescriptor_SameColumnNameChangedInBoth_LaterRemoteWins()
	{
		const string baseContent = """
		{
		  "Descriptor": {
		    "UId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
		    "Name": "SysAdminOperation_CanReset2FA",
		    "ModifiedOnUtc": "/Date(1000)/",
		    "ManagerName": "EntitySchemaManager",
		    "Schema": {
		      "UId": "schema-1",
		      "Name": "SysAdminOperation"
		    },
		    "Columns": [
		      {
		        "ColumnUId": "col-1",
		        "IsKey": true,
		        "ColumnName": "Code",
		        "DataTypeValueUId": "dt-1"
		      }
		    ]
		  }
		}
		""";

		const string localContent = """
		{
		  "Descriptor": {
		    "UId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
		    "Name": "SysAdminOperation_CanReset2FA",
		    "ModifiedOnUtc": "/Date(2000)/",
		    "ManagerName": "EntitySchemaManager",
		    "Schema": {
		      "UId": "schema-1",
		      "Name": "SysAdminOperation"
		    },
		    "Columns": [
		      {
		        "ColumnUId": "col-1",
		        "IsKey": true,
		        "ColumnName": "LocalCode",
		        "DataTypeValueUId": "dt-1"
		      }
		    ]
		  }
		}
		""";

		const string remoteContent = """
		{
		  "Descriptor": {
		    "UId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
		    "Name": "SysAdminOperation_CanReset2FA",
		    "ModifiedOnUtc": "/Date(3000)/",
		    "ManagerName": "EntitySchemaManager",
		    "Schema": {
		      "UId": "schema-1",
		      "Name": "SysAdminOperation"
		    },
		    "Columns": [
		      {
		        "ColumnUId": "col-1",
		        "IsKey": true,
		        "ColumnName": "RemoteCode",
		        "DataTypeValueUId": "dt-1"
		      }
		    ]
		  }
		}
		""";

		var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
			global::Creatio.ConflictResolver.ConflictFileType.DescriptorJson,
			baseContent,
			localContent,
			remoteContent,
			"Pkg/Data/descriptor.json"));

		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.Resolved), result.ErrorMessage);
		Assert.That(result.Report.WinnerPolicy, Is.EqualTo("REMOTE"));

		var root = JsonNode.Parse(result.MergedContent!)!.AsObject();
		var columns = root["Descriptor"]!["Columns"]!.AsArray();
		var col1 = columns.First(x => x!["ColumnUId"]!.GetValue<string>() == "col-1")!.AsObject();
		Assert.That(col1["ColumnName"]!.GetValue<string>(), Is.EqualTo("RemoteCode"));
	}

	[Test]
	public void DescriptorMerge_DataBindingDescriptor_WindowsPathSeparators_AppliesDataBindingValidation()
	{
		const string baseContent = """
		{
		  "Descriptor": {
		    "UId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
		    "Name": "SysAdminOperation_CanReset2FA",
		    "ModifiedOnUtc": "/Date(1000)/",
		    "ManagerName": "EntitySchemaManager",
		    "Schema": {
		      "UId": "schema-1",
		      "Name": "SysAdminOperation"
		    },
		    "Columns": [
		      {
		        "ColumnUId": "col-1",
		        "IsKey": true,
		        "DataTypeValueUId": "dt-1"
		      }
		    ]
		  }
		}
		""";

		const string localContent = """
		{
		  "Descriptor": {
		    "UId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
		    "Name": "SysAdminOperation_CanReset2FA",
		    "ModifiedOnUtc": "/Date(2000)/",
		    "ManagerName": "EntitySchemaManager",
		    "Schema": {
		      "UId": "schema-local",
		      "Name": "SysAdminOperation"
		    },
		    "Columns": [
		      {
		        "ColumnUId": "col-1",
		        "IsKey": true,
		        "DataTypeValueUId": "dt-1"
		      }
		    ]
		  }
		}
		""";

		const string remoteContent = """
		{
		  "Descriptor": {
		    "UId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
		    "Name": "SysAdminOperation_CanReset2FA",
		    "ModifiedOnUtc": "/Date(1500)/",
		    "ManagerName": "EntitySchemaManager",
		    "Schema": {
		      "UId": "schema-remote",
		      "Name": "SysAdminOperation"
		    },
		    "Columns": [
		      {
		        "ColumnUId": "col-1",
		        "IsKey": true,
		        "DataTypeValueUId": "dt-1"
		      }
		    ]
		  }
		}
		""";

		var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
			global::Creatio.ConflictResolver.ConflictFileType.DescriptorJson,
			baseContent,
			localContent,
			remoteContent,
			@"Pkg\Data\descriptor.json"));

		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.UnresolvedConflict));
		Assert.That(result.ErrorCode, Is.EqualTo("DataBindingDescriptorConflict"));
		Assert.That(result.Report.TrueConflicts, Does.Contain("Schema.UId"));
	}

	[Test]
	public void DescriptorMerge_AutomergeMode_EmitsConflictMarkersForDataBindingDescriptorConflict()
	{
		const string baseContent = """
		{
		  "Descriptor": {
		    "UId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
		    "Name": "SysAdminOperation_CanReset2FA",
		    "ModifiedOnUtc": "/Date(1000)/",
		    "ManagerName": "EntitySchemaManager",
		    "Schema": {
		      "UId": "schema-base",
		      "Name": "SysAdminOperation"
		    },
		    "Columns": [
		      {
		        "ColumnUId": "col-1",
		        "IsKey": true,
		        "DataTypeValueUId": "dt-1"
		      }
		    ]
		  }
		}
		""";

		const string localContent = """
		{
		  "Descriptor": {
		    "UId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
		    "Name": "SysAdminOperation_CanReset2FA",
		    "ModifiedOnUtc": "/Date(2000)/",
		    "ManagerName": "EntitySchemaManager",
		    "Schema": {
		      "UId": "schema-local",
		      "Name": "SysAdminOperation"
		    },
		    "Columns": [
		      {
		        "ColumnUId": "col-1",
		        "IsKey": true,
		        "DataTypeValueUId": "dt-1"
		      }
		    ]
		  }
		}
		""";

		const string remoteContent = """
		{
		  "Descriptor": {
		    "UId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
		    "Name": "SysAdminOperation_CanReset2FA",
		    "ModifiedOnUtc": "/Date(1500)/",
		    "ManagerName": "EntitySchemaManager",
		    "Schema": {
		      "UId": "schema-remote",
		      "Name": "SysAdminOperation"
		    },
		    "Columns": [
		      {
		        "ColumnUId": "col-1",
		        "IsKey": true,
		        "DataTypeValueUId": "dt-1"
		      }
		    ]
		  }
		}
		""";

		var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
			global::Creatio.ConflictResolver.ConflictFileType.DescriptorJson,
			baseContent,
			localContent,
			remoteContent,
			@"Pkg\Data\descriptor.json",
			global::Creatio.ConflictResolver.MergeMode.Automerge));
		var normalized = ResolverTestSupport.NormalizeLineEndings(result.MergedContent!);

		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.AutoResolvedWithConflicts), result.ErrorMessage);
		Assert.That(result.Report.WinnerPolicy, Is.EqualTo("CONFLICT_MARKERS"));
		Assert.That(normalized, Does.Contain("<<<<<<< Local\n      \"UId\": \"schema-local\",\n=======\n      \"UId\": \"schema-remote\",\n>>>>>>> Remote"));
		Assert.That(normalized, Does.Not.Contain("\n      <<<<<<< Local\n"));
	}

	[Test]
	public void DescriptorMerge_DataBindingDescriptor_PreservesPlusSignInSerializedJson()
	{
		const string baseContent = """
		{
		  "Descriptor": {
		    "UId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
		    "Name": "SysAdminOperation_CanReset2FA",
		    "ModifiedOnUtc": "/Date(1000)/",
		    "ManagerName": "EntitySchemaManager",
		    "Caption": "Adelaide (GMT+09:30)",
		    "Schema": {
		      "UId": "schema-1",
		      "Name": "SysAdminOperation"
		    },
		    "Columns": [
		      {
		        "ColumnUId": "col-1",
		        "IsKey": true,
		        "DataTypeValueUId": "dt-1",
		        "ColumnName": "Code"
		      }
		    ]
		  }
		}
		""";

		const string localContent = """
		{
		  "Descriptor": {
		    "UId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
		    "Name": "SysAdminOperation_CanReset2FA",
		    "ModifiedOnUtc": "/Date(2000)/",
		    "ManagerName": "EntitySchemaManager",
		    "Caption": "Adelaide (GMT+09:30)",
		    "Schema": {
		      "UId": "schema-1",
		      "Name": "SysAdminOperation"
		    },
		    "Columns": [
		      {
		        "ColumnUId": "col-1",
		        "IsKey": true,
		        "DataTypeValueUId": "dt-1",
		        "ColumnName": "LocalCode"
		      }
		    ]
		  }
		}
		""";

		const string remoteContent = """
		{
		  "Descriptor": {
		    "UId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
		    "Name": "SysAdminOperation_CanReset2FA",
		    "ModifiedOnUtc": "/Date(1500)/",
		    "ManagerName": "EntitySchemaManager",
		    "Caption": "Adelaide (GMT+09:30)",
		    "Schema": {
		      "UId": "schema-1",
		      "Name": "SysAdminOperation"
		    },
		    "Columns": [
		      {
		        "ColumnUId": "col-1",
		        "IsKey": true,
		        "DataTypeValueUId": "dt-1",
		        "ColumnName": "Code"
		      }
		    ]
		  }
		}
		""";

		var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
			global::Creatio.ConflictResolver.ConflictFileType.DescriptorJson,
			baseContent,
			localContent,
			remoteContent,
			"Pkg/Data/descriptor.json"));

		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.Resolved), result.ErrorMessage);
		Assert.That(result.MergedContent, Does.Contain("\"Caption\": \"Adelaide (GMT+09:30)\""));
		Assert.That(result.MergedContent, Does.Not.Contain("GMT\\u002B09:30"));
	}

	[Test]
	public void DescriptorMerge_SqlScriptDescriptor_DependsOnDescriptorMerge()
	{
		string testCaseName = "DescriptorSqlScriptCase1";
		var baseContent = File.ReadAllText(ResolverTestSupport.GetFixturePath(testCaseName, "base.json"));
		var localContent = File.ReadAllText(ResolverTestSupport.GetFixturePath(testCaseName, "local.json"));
		var remoteContent = File.ReadAllText(ResolverTestSupport.GetFixturePath(testCaseName, "remote.json"));
		var expected = File.ReadAllText(ResolverTestSupport.GetFixturePath(testCaseName, "resolved.json"));
		var result = ResolverTestSupport.Resolver.Resolve(
			new MergeRequest(ConflictFileType.DescriptorJson, baseContent, localContent, remoteContent));
		Assert.That(result.Status, Is.EqualTo(MergeStatus.Resolved), result.ErrorMessage);
		Assert.That(JsonNode.Parse(result.MergedContent!)!.ToJsonString(), Is.EqualTo(JsonNode.Parse(expected)!.ToJsonString()));
	}

}
