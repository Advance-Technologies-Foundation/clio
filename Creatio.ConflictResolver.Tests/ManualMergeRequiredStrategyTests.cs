namespace Creatio.ConflictResolver.Tests;

[TestFixture, Category("Unit")]
public class ManualMergeRequiredStrategyTests
{
	[TestCase(global::Creatio.ConflictResolver.ConflictFileType.SourceCode)]
	[TestCase(global::Creatio.ConflictResolver.ConflictFileType.SqlScript)]
	[TestCase(global::Creatio.ConflictResolver.ConflictFileType.ProcessMetadataJson)]
	[TestCase(global::Creatio.ConflictResolver.ConflictFileType.ProcessResourceXml)]
	public void Resolve_ReturnsManualMergeRequiredWithWholeFileConflict_ForConflictingNonAutomaticFileTypes(
		global::Creatio.ConflictResolver.ConflictFileType fileType)
	{
		var result = new global::Creatio.ConflictResolver.ConflictResolver().Resolve(
			new global::Creatio.ConflictResolver.MergeRequest(
				fileType,
				"base content",
				"local content",
				"remote content"));

		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.ManualMergeRequired));
		Assert.That(result.ErrorCode, Is.EqualTo("ManualMergeRequired"));
		Assert.That(result.ErrorMessage, Does.Contain(fileType.ToString()));
		Assert.That(result.Report.ResolutionType, Is.EqualTo("manual_merge_required"));
		Assert.That(result.Report.VerificationPassed, Is.False);
		Assert.That(result.Report.WinnerPolicy, Is.EqualTo("CONFLICT_MARKERS"));
		Assert.That(result.Report.TrueConflicts, Does.Contain("full_file"));
	}

	[TestCase(global::Creatio.ConflictResolver.ConflictFileType.SourceCode)]
	[TestCase(global::Creatio.ConflictResolver.ConflictFileType.SqlScript)]
	[TestCase(global::Creatio.ConflictResolver.ConflictFileType.ProcessMetadataJson)]
	[TestCase(global::Creatio.ConflictResolver.ConflictFileType.ProcessResourceXml)]
	public void Resolve_WrapsEntireLocalAndRemoteInSingleConflictBlock_WhenAllThreeDiffer(
		global::Creatio.ConflictResolver.ConflictFileType fileType)
	{
		var result = new global::Creatio.ConflictResolver.ConflictResolver().Resolve(
			new global::Creatio.ConflictResolver.MergeRequest(
				fileType,
				"base content",
				"local content",
				"remote content"));

		var expected = string.Join(
			"\n",
			"<<<<<<< Local",
			"local content",
			"=======",
			"remote content",
			">>>>>>> Remote");
		Assert.That(result.MergedContent, Is.EqualTo(expected));
	}

	[TestCase(global::Creatio.ConflictResolver.ConflictFileType.SourceCode)]
	[TestCase(global::Creatio.ConflictResolver.ConflictFileType.SqlScript)]
	[TestCase(global::Creatio.ConflictResolver.ConflictFileType.ProcessMetadataJson)]
	[TestCase(global::Creatio.ConflictResolver.ConflictFileType.ProcessResourceXml)]
	public void Resolve_PreservesMultilineContent_WithinWholeFileConflictBlock(
		global::Creatio.ConflictResolver.ConflictFileType fileType)
	{
		const string local = "line 1\nlocal line 2\nline 3";
		const string remote = "line 1\nremote line 2\nline 3";

		var result = new global::Creatio.ConflictResolver.ConflictResolver().Resolve(
			new global::Creatio.ConflictResolver.MergeRequest(
				fileType,
				"line 1\nbase line 2\nline 3",
				local,
				remote));

		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.ManualMergeRequired));
		Assert.That(result.MergedContent, Does.StartWith("<<<<<<< Local\n"));
		Assert.That(result.MergedContent, Does.Contain("\n=======\n"));
		Assert.That(result.MergedContent, Does.EndWith("\n>>>>>>> Remote"));
		Assert.That(result.MergedContent, Does.Contain(local));
		Assert.That(result.MergedContent, Does.Contain(remote));
	}

	[TestCase(global::Creatio.ConflictResolver.ConflictFileType.SourceCode)]
	[TestCase(global::Creatio.ConflictResolver.ConflictFileType.SqlScript)]
	[TestCase(global::Creatio.ConflictResolver.ConflictFileType.ProcessMetadataJson)]
	[TestCase(global::Creatio.ConflictResolver.ConflictFileType.ProcessResourceXml)]
	public void Resolve_ReturnsResolved_WhenLocalMatchesRemote(
		global::Creatio.ConflictResolver.ConflictFileType fileType)
	{
		var result = new global::Creatio.ConflictResolver.ConflictResolver().Resolve(
			new global::Creatio.ConflictResolver.MergeRequest(
				fileType,
				"base content",
				"same content",
				"same content"));

		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.Resolved));
		Assert.That(result.MergedContent, Is.EqualTo("same content"));
		Assert.That(result.Report.ResolutionType, Is.EqualTo("text_3way_trivial"));
	}

	[TestCase(global::Creatio.ConflictResolver.ConflictFileType.SourceCode)]
	[TestCase(global::Creatio.ConflictResolver.ConflictFileType.SqlScript)]
	[TestCase(global::Creatio.ConflictResolver.ConflictFileType.ProcessMetadataJson)]
	[TestCase(global::Creatio.ConflictResolver.ConflictFileType.ProcessResourceXml)]
	public void Resolve_ReturnsResolved_WhenLocalMatchesBase(
		global::Creatio.ConflictResolver.ConflictFileType fileType)
	{
		var result = new global::Creatio.ConflictResolver.ConflictResolver().Resolve(
			new global::Creatio.ConflictResolver.MergeRequest(
				fileType,
				"base content",
				"base content",
				"remote content"));

		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.Resolved));
		Assert.That(result.MergedContent, Is.EqualTo("remote content"));
		Assert.That(result.Report.ResolutionType, Is.EqualTo("text_3way_trivial"));
	}

	[TestCase(global::Creatio.ConflictResolver.ConflictFileType.SourceCode)]
	[TestCase(global::Creatio.ConflictResolver.ConflictFileType.SqlScript)]
	[TestCase(global::Creatio.ConflictResolver.ConflictFileType.ProcessMetadataJson)]
	[TestCase(global::Creatio.ConflictResolver.ConflictFileType.ProcessResourceXml)]
	public void Resolve_ReturnsResolved_WhenRemoteMatchesBase(
		global::Creatio.ConflictResolver.ConflictFileType fileType)
	{
		var result = new global::Creatio.ConflictResolver.ConflictResolver().Resolve(
			new global::Creatio.ConflictResolver.MergeRequest(
				fileType,
				"base content",
				"local content",
				"base content"));

		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.Resolved));
		Assert.That(result.MergedContent, Is.EqualTo("local content"));
		Assert.That(result.Report.ResolutionType, Is.EqualTo("text_3way_trivial"));
	}

	[TestCase(global::Creatio.ConflictResolver.ConflictFileType.SourceCode)]
	[TestCase(global::Creatio.ConflictResolver.ConflictFileType.SqlScript)]
	[TestCase(global::Creatio.ConflictResolver.ConflictFileType.ProcessMetadataJson)]
	[TestCase(global::Creatio.ConflictResolver.ConflictFileType.ProcessResourceXml)]
	public void Resolve_ReturnsResolved_WhenAllThreeIdentical(
		global::Creatio.ConflictResolver.ConflictFileType fileType)
	{
		var result = new global::Creatio.ConflictResolver.ConflictResolver().Resolve(
			new global::Creatio.ConflictResolver.MergeRequest(
				fileType,
				"same content",
				"same content",
				"same content"));

		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.Resolved));
		Assert.That(result.MergedContent, Is.EqualTo("same content"));
		Assert.That(result.Report.ResolutionType, Is.EqualTo("text_3way_trivial"));
	}
}
