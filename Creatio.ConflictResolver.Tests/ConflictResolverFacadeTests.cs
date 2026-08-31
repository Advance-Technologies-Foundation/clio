namespace Creatio.ConflictResolver.Tests;

[TestFixture]
[Category("Unit")]
public sealed class ConflictResolverFacadeTests
{
	[Test]
	[Description("Propagates unexpected strategy failures so callers can report an invocation error.")]
	public void Resolve_UnexpectedStrategyFailure_PropagatesException()
	{
		// Arrange
		var resolver = new global::Creatio.ConflictResolver.ConflictResolver([new ThrowingStrategy()]);
		var request = new global::Creatio.ConflictResolver.MergeRequest(
			global::Creatio.ConflictResolver.ConflictFileType.MetadataJson,
			"base",
			"local",
			"remote");

		// Act
		Action act = () => resolver.Resolve(request);

		// Assert
		var exception = Assert.Throws<InvalidOperationException>(act,
			"unexpected implementation defects must not be mislabeled as invalid user input");
		Assert.That(exception!.Message, Is.EqualTo("boom"),
			"the original exception should reach the hosting boundary for normal error handling");
	}

	[Test]
	[Description("Returns invalid input when a semantic report exceeds its bounded entry count.")]
	public void Resolve_OversizedSemanticReport_ReturnsInvalidInput()
	{
		// Arrange
		var resolver = new global::Creatio.ConflictResolver.ConflictResolver([new OversizedReportStrategy()]);
		var request = new global::Creatio.ConflictResolver.MergeRequest(
			global::Creatio.ConflictResolver.ConflictFileType.MetadataJson,
			"base",
			"local",
			"remote");

		// Act
		var result = resolver.Resolve(request);

		// Assert
		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.InvalidInput),
			"branch-controlled report paths must not grow agent output without a bound");
		Assert.That(result.ErrorCode, Is.EqualTo("MergeReportLimitExceeded"),
			"the caller needs the stable report-budget diagnostic");
	}

	private sealed class ThrowingStrategy : global::Creatio.ConflictResolver.IMergeStrategy
	{
		public bool CanHandle(global::Creatio.ConflictResolver.ConflictFileType fileType) => true;

		public global::Creatio.ConflictResolver.MergeResult Merge(global::Creatio.ConflictResolver.MergeRequest request) =>
			throw new InvalidOperationException("boom");
	}

	private sealed class OversizedReportStrategy : global::Creatio.ConflictResolver.IMergeStrategy
	{
		public bool CanHandle(global::Creatio.ConflictResolver.ConflictFileType fileType) => true;

		public global::Creatio.ConflictResolver.MergeResult Merge(global::Creatio.ConflictResolver.MergeRequest request) =>
			global::Creatio.ConflictResolver.MergeResultFactory.Resolved(
				"merged",
				"test",
				trueConflicts: Enumerable.Range(0, 1025).Select(index => $"path-{index}"));
	}
}
