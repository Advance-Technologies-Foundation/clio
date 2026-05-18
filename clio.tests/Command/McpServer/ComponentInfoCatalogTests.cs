using System.IO;
using System.Text;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class ComponentInfoCatalogTests {
	private const string TopLevelArrayPayload =
		"""[ { "componentType": "crt.Sample", "category": "interactive", "description": "test", "container": false, "properties": {} } ]""";

	private const string WrappedObjectPayload =
		"""{ "components": [ { "componentType": "crt.Sample", "category": "interactive", "description": "test", "container": false, "properties": {} } ] }""";

	[Test]
	[Description("Legacy top-level-array payload (current CDN shape) is still accepted.")]
	public void LoadFromStream_Accepts_TopLevel_Array_Payload() {
		using Stream stream = new MemoryStream(Encoding.UTF8.GetBytes(TopLevelArrayPayload));

		ComponentCatalogState state = ComponentInfoCatalog.LoadFromStream(stream, "latest", ComponentRegistrySource.Cdn);

		state.Entries.Should().HaveCount(1);
		state.Entries[0].ComponentType.Should().Be("crt.Sample");
	}

	[Test]
	[Description("Wrapped { \"components\": [...] } payload is accepted as the new canonical shape.")]
	public void LoadFromStream_Accepts_Wrapped_Object_Payload() {
		using Stream stream = new MemoryStream(Encoding.UTF8.GetBytes(WrappedObjectPayload));

		ComponentCatalogState state = ComponentInfoCatalog.LoadFromStream(stream, "latest", ComponentRegistrySource.Local);

		state.Entries.Should().HaveCount(1,
			because: "the wrapper { components: [...] } must yield the same entries as the legacy top-level array");
		state.Entries[0].ComponentType.Should().Be("crt.Sample");
		state.Source.Should().Be(ComponentRegistrySource.Local);
	}

	[Test]
	[Description("Payloads that are neither an array nor a wrapped object are rejected with a clear error.")]
	public void LoadFromStream_Rejects_Unsupported_Json_Shape() {
		using Stream stream = new MemoryStream(Encoding.UTF8.GetBytes("""{ "items": [] }"""));

		System.Action act = () => ComponentInfoCatalog.LoadFromStream(stream);

		act.Should().Throw<System.InvalidOperationException>()
			.WithMessage("*array of component entries or an object with a 'components' array*",
				because: "the error must guide developers to one of the two supported shapes");
	}
}
