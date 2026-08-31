using Creatio.ConflictResolver.Tests.TestSupport;

namespace Creatio.ConflictResolver.Tests;

[TestFixture, Category("Unit")]
public class ResourceMergeStrategyTests
{
	[Test]
	[Description("Rejects duplicate resource names before indexing can discard an item.")]
	public void ResourceMerge_DuplicateInputKeys_ReturnsInvalidInput()
	{
		// Arrange
		const string content = """
		<?xml version="1.0" encoding="utf-8"?>
		<Resources><Items><Item Name="Caption" Value="First" /><Item Name="Caption" Value="Second" /></Items></Resources>
		""";

		// Act
		var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
			global::Creatio.ConflictResolver.ConflictFileType.ResourceXml,
			content,
			content,
			content));

		// Assert
		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.InvalidInput),
			"duplicate resource names are ambiguous and must not be silently collapsed");
		Assert.That(result.ErrorCode, Is.EqualTo("DuplicateBaseKeys"),
			"the diagnostic must identify which merge stage contains the duplicate");
	}

	[Test]
	[Description("Rejects DTD-bearing resource XML before entity expansion can consume resolver memory.")]
	public void ResourceMerge_DtdInput_ReturnsInvalidInput()
	{
		// Arrange
		const string content = """<!DOCTYPE Resources [<!ENTITY value "expanded">]><Resources><Items><Item Name="Caption" Value="&value;" /></Items></Resources>""";

		// Act
		var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
			global::Creatio.ConflictResolver.ConflictFileType.ResourceXml,
			content,
			content,
			content));

		// Assert
		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.InvalidInput),
			"resource parsing must prohibit DTD processing regardless of entity size");
	}

	[Test]
	public void ResourceMerge_FixtureCase1_MatchesExpectedResolvedResource()
	{
		var baseContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("ResourcesCase1", "resoursebase.xml"));
		var localContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("ResourcesCase1", "resourselocal.xml"));
		var remoteContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("ResourcesCase1", "resourseremote.xml"));
		var expected = File.ReadAllText(ResolverTestSupport.GetFixturePath("ResourcesCase1", "resourseresolved.xml"));

		var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
			global::Creatio.ConflictResolver.ConflictFileType.ResourceXml,
			baseContent,
			localContent,
			remoteContent));

		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.Resolved), result.ErrorMessage);
		Assert.That(result.Report.WinnerPolicy, Is.EqualTo("LOCAL"));
		Assert.That(result.Report.TrueConflicts, Is.Empty);
		Assert.That(result.Report.LocalAdditions, Is.EqualTo(new[]
		{
			"Columns.ColumnText2local.Caption"
		}));
		Assert.That(result.Report.RemoteAdditions, Is.EqualTo(new[]
		{
			"Columns.ColumnNumber222.Caption"
		}));
		Assert.That(result.Report.LocalDeletions, Is.EqualTo(new[]
		{
			"Columns.ColumnAutonumber2.Caption",
			"Columns.ColumnNumberRemote.Caption",
			"Columns.Columndemolocal.Caption",
			"Columns.Columnrightext2.Caption"
		}));
		Assert.That(result.Report.RemoteDeletions, Is.EqualTo(new[]
		{
			"Columns.ColumnCheckbox22.Caption",
			"Columns.ColumnDateTime2.Caption",
			"Columns.Columnphonenumber2.Caption"
		}));
		Assert.That(
			result.MergedContent!,
			Is.EqualTo(expected));
	}
	
	[Test]
	public void ResourceMerge_FixtureCase2_MatchesExpectedResolvedResource()
	{
		var baseContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("ResourcesCase2", "base.xml"));
		var localContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("ResourcesCase2", "local.xml"));
		var remoteContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("ResourcesCase2", "remote.xml"));
		var expected = File.ReadAllText(ResolverTestSupport.GetFixturePath("ResourcesCase2", "resolved.xml"));

		var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
			global::Creatio.ConflictResolver.ConflictFileType.ResourceXml,
			baseContent,
			localContent,
			remoteContent));

		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.Resolved), result.ErrorMessage);
		Assert.That(result.Report.WinnerPolicy, Is.EqualTo("LOCAL"));
		Assert.That(result.Report.TrueConflicts, Is.Empty);
		Assert.That(result.Report.LocalAdditions, Is.Empty);
		Assert.That(result.Report.RemoteAdditions, Is.EqualTo(new[]
		{
			"LocalizableStrings.IndicatorWidget_v46kjd5_title.Value"
		}));
		Assert.That(result.Report.LocalDeletions, Is.EqualTo(new[]
		{
			"LocalizableStrings.IndicatorWidget_0s5y5sk_title.Value",
			"LocalizableStrings.IndicatorWidget_zyshxld_title.Value"
		}));
		Assert.That(result.Report.RemoteDeletions, Is.EqualTo(new[]
		{
			"LocalizableStrings.Label_j55o3f2_caption.Value",
			"LocalizableStrings.TabContainer_0z71xnq_caption.Value"
		}));
		Assert.That(
			result.MergedContent!,
			Is.EqualTo(expected));
	}

	[Test]
	public void ResourceMerge_FixtureCase3_MatchesExpectedResolvedResource()
	{
		var baseContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("ResourcesCase3", "base.xml"));
		var localContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("ResourcesCase3", "local.xml"));
		var remoteContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("ResourcesCase3", "remote.xml"));
		var expected = File.ReadAllText(ResolverTestSupport.GetFixturePath("ResourcesCase3", "resolved.xml"));

		var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
			global::Creatio.ConflictResolver.ConflictFileType.ResourceXml,
			baseContent,
			localContent,
			remoteContent));

		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.Resolved), result.ErrorMessage);
		Assert.That(result.Report.WinnerPolicy, Is.EqualTo("LOCAL"));
		Assert.That(result.Report.LocalAdditions, Is.Empty);
		Assert.That(result.Report.RemoteAdditions, Is.Empty);
		Assert.That(result.Report.LocalDeletions, Is.Empty);
		Assert.That(result.Report.RemoteDeletions, Is.Empty);
		Assert.That(result.Report.TrueConflicts, Is.EqualTo(new[]
		{
			"Columns.ColumnLocalphone.Caption"
		}));
		Assert.That(
			result.MergedContent!,
			Is.EqualTo(expected));
	}

	[Test]
	public void ResourceMerge_UnionByName_LocalWinsSameKeyConflict()
	{
		var baseContent = ResolverTestSupport.BuildResource(("Caption", "Base"));
		var localContent = ResolverTestSupport.BuildResource(("Caption", "Local"), ("LocalOnly", "L"));
		var remoteContent = ResolverTestSupport.BuildResource(("Caption", "Remote"), ("RemoteOnly", "R"));

		var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
			global::Creatio.ConflictResolver.ConflictFileType.ResourceXml,
			baseContent,
			localContent,
			remoteContent));

		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.Resolved));
		var map = ResolverTestSupport.GetResourceMap(result.MergedContent!);
		Assert.That(map["Caption"], Is.EqualTo("Local"));
		Assert.That(map["LocalOnly"], Is.EqualTo("L"));
		Assert.That(map["RemoteOnly"], Is.EqualTo("R"));
	}

	[Test]
	public void ResourceMerge_AutomergeMode_EmitsConflictMarkersForSameKeyConflict()
	{
		var baseContent = ResolverTestSupport.BuildResource(("Caption", "Base"));
		var localContent = ResolverTestSupport.BuildResource(("Caption", "Local"), ("LocalOnly", "L"));
		var remoteContent = ResolverTestSupport.BuildResource(("Caption", "Remote"), ("RemoteOnly", "R"));

		var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
			global::Creatio.ConflictResolver.ConflictFileType.ResourceXml,
			baseContent,
			localContent,
			remoteContent,
			null,
			global::Creatio.ConflictResolver.MergeMode.Automerge));
		var normalized = ResolverTestSupport.NormalizeLineEndings(result.MergedContent!);

		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.AutoResolvedWithConflicts), result.ErrorMessage);
		Assert.That(result.Report.WinnerPolicy, Is.EqualTo("CONFLICT_MARKERS"));
		Assert.That(normalized, Does.Contain("<<<<<<< Local\n\t\t\t<Item Name=\"Caption\" Value=\"Local\" />\n=======\n\t\t\t<Item Name=\"Caption\" Value=\"Remote\" />\n>>>>>>> Remote"));
		Assert.That(normalized, Does.Not.Contain("\n\t\t\t<<<<<<< Local\n"));
		Assert.That(normalized, Does.Contain("Item Name=\"LocalOnly\" Value=\"L\""));
		Assert.That(normalized, Does.Contain("Item Name=\"RemoteOnly\" Value=\"R\""));
	}

	[TestCase(true)]
	[TestCase(false)]
	[Description("Preserves delete-versus-change resource conflicts so the changed branch cannot disappear silently.")]
	public void ResourceMerge_AutomergeMode_EmitsConflictMarkersForDeleteVersusChange(bool localDeletes)
	{
		// Arrange
		var baseContent = ResolverTestSupport.BuildResource(("Caption", "Base"));
		var changedContent = ResolverTestSupport.BuildResource(("Caption", "Changed"));
		var deletedContent = ResolverTestSupport.BuildResource();
		var localContent = localDeletes ? deletedContent : changedContent;
		var remoteContent = localDeletes ? changedContent : deletedContent;

		// Act
		var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
			global::Creatio.ConflictResolver.ConflictFileType.ResourceXml,
			baseContent,
			localContent,
			remoteContent,
			null,
			global::Creatio.ConflictResolver.MergeMode.Automerge));

		// Assert
		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.AutoResolvedWithConflicts),
			"a deletion on one branch and a modification on the other requires an explicit user decision");
		Assert.That(result.Report.TrueConflicts, Does.Contain("Caption"),
			"the report must identify the resource key involved in the delete-versus-change conflict");
		Assert.That(result.MergedContent, Does.Contain("<<<<<<< Local"),
			"the changed value and the deletion must both remain visible in marker content");
		Assert.That(result.MergedContent, Does.Contain("Value=\"Changed\""),
			"the surviving branch value must not be discarded before the user decides");
	}

	[Test]
	public void ResourceMerge_AutomergeMode_EmitsConflictMarkersFromFixture()
	{
		var baseContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("NewMode\\resourceXml", "base.xml"));
		var localContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("NewMode\\resourceXml", "local.xml"));
		var remoteContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("NewMode\\resourceXml", "remote.xml"));
		var expected = File.ReadAllText(ResolverTestSupport.GetFixturePath("NewMode\\resourceXml", "resolved.xml"));

		var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
			global::Creatio.ConflictResolver.ConflictFileType.ResourceXml,
			baseContent,
			localContent,
			remoteContent,
			null,
			global::Creatio.ConflictResolver.MergeMode.Automerge));
		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.AutoResolvedWithConflicts), result.ErrorMessage);

		Assert.That(result.MergedContent!, Is.EqualTo(expected));
	}

	[Test]
	public void ResourceMerge2_AutomergeMode_EmitsConflictMarkersFromFixture()
	{
		var baseContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("NewMode\\resoursesNew", "base.xml"));
		var localContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("NewMode\\resoursesNew", "local.xml"));
		var remoteContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("NewMode\\resoursesNew", "remote.xml"));
		var expected = File.ReadAllText(ResolverTestSupport.GetFixturePath("NewMode\\resoursesNew", "resolved.xml"));

		var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
			global::Creatio.ConflictResolver.ConflictFileType.ResourceXml,
			baseContent,
			localContent,
			remoteContent,
			null,
			global::Creatio.ConflictResolver.MergeMode.Automerge));
		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.AutoResolvedWithConflicts), result.ErrorMessage);

		Assert.That(result.MergedContent!, Is.EqualTo(expected));
	}
	
	[Test]
	public void ResourceMerge_AutomergeMode_EmitsConflictMarkersFromCorruptedFixture()
	{
		var baseContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("NewMode\\resoursesCorrupted", "base.xml"));
		var localContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("NewMode\\resoursesCorrupted", "local.xml"));
		var remoteContent = File.ReadAllText(ResolverTestSupport.GetFixturePath("NewMode\\resoursesCorrupted", "remote.xml"));
		var expected = File.ReadAllText(ResolverTestSupport.GetFixturePath("NewMode\\resoursesCorrupted", "resolved.xml"));

		var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
			global::Creatio.ConflictResolver.ConflictFileType.ResourceXml,
			baseContent,
			localContent,
			remoteContent,
			null,
			global::Creatio.ConflictResolver.MergeMode.Automerge));
		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.AutoResolvedWithConflicts), result.ErrorMessage);

		Assert.That(result.MergedContent!, Is.EqualTo(expected));
	}
}
