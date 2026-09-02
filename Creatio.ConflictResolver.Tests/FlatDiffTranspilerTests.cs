using System.Text.Json.Nodes;
using Creatio.ConflictResolver.Tests.TestSupport;

namespace Creatio.ConflictResolver.Tests;

[TestFixture, Category("Unit")]
public class FlatDiffTranspilerTests
{
	private static readonly global::Creatio.ConflictResolver.FlatDiffTranspiler Transpiler = new();

	[Test]
	public void Transform_MetadataFixture_ProducesExpectedTransformedMetadata()
	{
		var metadata = ReadTestCaseFile("FlatDiffTransplierCase1", "metadata.json");
		var expected = JsonNode.Parse(ReadTestCaseFile("FlatDiffTransplierCase1", "transformedMetadata.json"));

		var transformed = Transpiler.Transform(metadata);
		var actual = JsonNode.Parse(transformed);

		Assert.That(JsonNode.DeepEquals(actual, expected), Is.True);
	}

	[Test]
	public void Restore_TransformedMetadataFixture_ProducesExpectedMetadata()
	{
		var transformedMetadata = ReadTestCaseFile("FlatDiffTransplierCase1", "transformedMetadata.json");
		var expectedMetadata = ReadTestCaseFile("FlatDiffTransplierCase1", "metadata.json");

		var restored = Transpiler.Restore(transformedMetadata);

		Assert.That(
			restored,
			Is.EqualTo(expectedMetadata));
	}
	
	[Test]
	public void Restore_TransformedMetadataFixture2_ProducesExpectedMetadata()
	{
		var transformedMetadata = ReadTestCaseFile("FlatDiffTransplierCase2", "transformedMetadata.json");
		var expectedMetadata = ReadTestCaseFile("FlatDiffTransplierCase2", "metadata.json");

		var restored = Transpiler.Restore(transformedMetadata);

		Assert.That(restored, Is.EqualTo(expectedMetadata));
	}

	[Test]
	public void TransformThenRestore_MetadataFixture_RoundTripsToOriginal()
	{
		var metadata = ReadTestCaseFile("FlatDiffTransplierCase1", "metadata.json");

		var transformed = Transpiler.Transform(metadata);
		var restored = Transpiler.Restore(transformed);

		Assert.That(restored, Is.EqualTo(metadata));
	}
	
	[Test]
	public void TransformThenRestore_MetadataFixture2_RoundTripsToOriginal()
	{
		var metadata = ReadTestCaseFile("FlatDiffTransplierCase2", "metadata.json");

		var transformed = Transpiler.Transform(metadata);
		var restored = Transpiler.Restore(transformed);

		Assert.That(restored, Is.EqualTo(metadata));
	}
	[Test]
	public void Transform_EdgeCaseSamePathRemoveAndAdd_UsesHasBodyMarkerInUid()
	{
		const string metadata = "- MetaData.Schema.B7\r\n+ MetaData.Schema.B7 false";

		var transformedText = Transpiler.Transform(metadata);
		var transformed = JsonNode.Parse(transformedText)!.AsObject();
		var items = transformed["Items"]!.AsArray();

		Assert.That(items, Has.Count.EqualTo(2));
		Assert.That(items[0]!["OperationType"]!.GetValue<string>(), Is.EqualTo("Remove"));
		Assert.That(items[0]!["UId"]!.GetValue<string>(), Is.EqualTo("MetaData.Schema.B7.{hasBody:false}"));
		Assert.That(items[0]!["Inline"]!.GetValue<bool>(), Is.True);
		Assert.That(items[0]!["Body"], Is.Null);

		Assert.That(items[1]!["OperationType"]!.GetValue<string>(), Is.EqualTo("Add"));
		Assert.That(items[1]!["UId"]!.GetValue<string>(), Is.EqualTo("MetaData.Schema.B7.{hasBody:true}"));
		Assert.That(items[1]!["Body"]!.GetValue<bool>(), Is.False);
		Assert.That(items[1]!["Inline"]!.GetValue<bool>(), Is.True);

		var restored = Transpiler.Restore(transformedText);
		Assert.That(restored, Is.EqualTo(metadata));
	}

	[Test]
	public void TransformThenRestore_InlineStringWithSingleQuotes_DoesNotEscapeSingleQuote()
	{
		const string metadata = "= MetaData.Schema.HD6 \"define('{0}Structure', ['{0}Resources'])\"";

		var transformed = Transpiler.Transform(metadata);
		var restored = Transpiler.Restore(transformed);

		Assert.That(restored, Is.EqualTo(metadata));
		Assert.That(restored, Does.Not.Contain("\\u0027"));
	}
	
	private static string ReadTestCaseFile(string caseName, string fileName)
	{
		var fixtureCasePath = Path.Combine("TestCases", caseName);
		return File.ReadAllText(ResolverTestSupport.GetFixturePath(fixtureCasePath, fileName));
	}
}
