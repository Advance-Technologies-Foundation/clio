using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class RequestInfoCatalogTests {
	private const string ValidEnvelopeJson = """
	{
	  "requests": [
	    {
	      "requestType": "crt.SaveRecordRequest",
	      "parameters": {},
	      "description": "Saves the current record."
	    },
	    {
	      "requestType": "crt.ClosePageRequest",
	      "parameters": {},
	      "description": "Closes the currently open page.",
	      "references": {
	        "docs": ["request-docs/close-page.request.md"]
	      }
	    }
	  ],
	  "references": {
	    "baseParameters": {
	      "$context": { "type": "ViewModelContext", "description": "Platform-injected view-model context." }
	    },
	    "typeDefinitions": {
	      "RequestBindingConfig": { "fields": { "request": { "type": "string", "required": true } } }
	    }
	  }
	}
	""";

	[Test]
	[Description("A valid wrapped envelope parses into alphabetically ordered entries with a case-insensitive lookup and the global references block attached.")]
	public void LoadFromStream_ShouldBuildOrderedStateWithLookup_WhenEnvelopeIsValid() {
		// Arrange
		using MemoryStream stream = new(Encoding.UTF8.GetBytes(ValidEnvelopeJson));

		// Act
		RequestCatalogState state = RequestInfoCatalog.LoadFromStream(stream, "8.3.4", ComponentRegistrySource.FileCache);

		// Assert
		state.Entries.Should().HaveCount(2,
			because: "both declared requests are valid and must survive parsing");
		state.Entries[0].RequestType.Should().Be("crt.ClosePageRequest",
			because: "entries are ordered alphabetically for stable list-mode output regardless of producer order");
		state.Lookup.ContainsKey("CRT.CLOSEPAGEREQUEST").Should().BeTrue(
			because: "the lookup must be case-insensitive so agent-typed casing variants still resolve");
		state.ResolvedVersion.Should().Be("8.3.4",
			because: "the state must echo the version the bytes were resolved for");
		state.Source.Should().Be(ComponentRegistrySource.FileCache,
			because: "the state must echo which fallback tier produced the bytes");
		state.GlobalReferences.Should().NotBeNull(
			because: "the envelope ships a root references block that detail responses need");
		state.GlobalReferences!.BaseParameters.Should().ContainKey("$context",
			because: "baseParameters must round-trip so the tool can surface them separately from parameters");
		state.GlobalReferences.TypeDefinitions.Should().ContainKey("RequestBindingConfig",
			because: "the global wiring contract must be available to the type-definition closure");
	}

	[Test]
	[Description("The docs references on an entry round-trip so the tool can fetch the long-form documentation lazily.")]
	public void LoadFromStream_ShouldPreserveDocsReferences_WhenEntryDeclaresThem() {
		// Arrange
		using MemoryStream stream = new(Encoding.UTF8.GetBytes(ValidEnvelopeJson));

		// Act
		RequestCatalogState state = RequestInfoCatalog.LoadFromStream(stream);

		// Assert
		state.Lookup["crt.ClosePageRequest"].References!.Docs.Should()
			.ContainSingle(because: "the entry declares exactly one context doc")
			.Which.Should().Be("request-docs/close-page.request.md",
				because: "the raw producer path must be preserved for the docs pipeline");
	}

	[Test]
	[Description("A top-level JSON array is rejected: unlike the component registry there is no legacy array generation for requests, so the shape is a producer mistake that must fail loudly.")]
	public void LoadFromStream_ShouldThrow_WhenPayloadIsTopLevelArray() {
		// Arrange
		using MemoryStream stream = new(Encoding.UTF8.GetBytes("""[ { "requestType": "crt.ClosePageRequest" } ]"""));

		// Act
		Action act = () => RequestInfoCatalog.LoadFromStream(stream);

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "the request registry contract is the wrapped envelope only")
			.WithMessage("*must be an object with a 'requests' array*");
	}

	[Test]
	[Description("An object without a requests array is rejected so a renamed producer key cannot silently degrade into an empty catalog.")]
	public void LoadFromStream_ShouldThrow_WhenRequestsArrayIsMissing() {
		// Arrange
		using MemoryStream stream = new(Encoding.UTF8.GetBytes("""{ "components": [] }"""));

		// Act
		Action act = () => RequestInfoCatalog.LoadFromStream(stream);

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "a payload without the requests array is malformed, not merely empty")
			.WithMessage("*must be an object with a 'requests' array*");
	}

	[Test]
	[Description("An empty requests array is rejected: serving an empty catalog would hide a producer-side publishing failure behind success responses.")]
	public void LoadFromStream_ShouldThrow_WhenRequestsArrayIsEmpty() {
		// Arrange
		using MemoryStream stream = new(Encoding.UTF8.GetBytes("""{ "requests": [] }"""));

		// Act
		Action act = () => RequestInfoCatalog.LoadFromStream(stream);

		// Assert
		act.Should().Throw<InvalidOperationException>(
			because: "an empty catalog is a malformed registry, not a valid state");
	}

	[Test]
	[Description("Duplicate request types (case-insensitive) are rejected: the lookup would silently shadow one entry, serving an ambiguous catalog.")]
	public void LoadFromStream_ShouldThrow_WhenRequestTypesAreDuplicated() {
		// Arrange
		const string payload = """
		{
		  "requests": [
		    { "requestType": "crt.ClosePageRequest" },
		    { "requestType": "CRT.CLOSEPAGEREQUEST" }
		  ]
		}
		""";
		using MemoryStream stream = new(Encoding.UTF8.GetBytes(payload));

		// Act
		Action act = () => RequestInfoCatalog.LoadFromStream(stream);

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "a duplicate request type would be silently shadowed by the case-insensitive lookup")
			.WithMessage("*duplicate request types*");
	}

	[Test]
	[Description("Entries with a blank requestType are dropped while valid entries survive — a partially malformed payload keeps serving the valid part.")]
	public void LoadFromStream_ShouldDropBlankEntries_WhenValidEntriesRemain() {
		// Arrange
		const string payload = """
		{
		  "requests": [
		    { "requestType": "" },
		    { "requestType": "crt.ClosePageRequest", "description": "Closes the currently open page." }
		  ]
		}
		""";
		using MemoryStream stream = new(Encoding.UTF8.GetBytes(payload));

		// Act
		RequestCatalogState state = RequestInfoCatalog.LoadFromStream(stream);

		// Assert
		state.Entries.Should().ContainSingle(
				because: "the blank entry has no usable lookup key and must be dropped, keeping the valid one")
			.Which.RequestType.Should().Be("crt.ClosePageRequest",
				because: "the valid entry must survive the blank-entry filter");
	}

	[Test]
	[Description("A non-empty requests array whose entries ALL have a blank/whitespace requestType is rejected: the blank-entry filter empties it, and serving a zero-entry catalog would hide a producer-side publishing failure behind success responses. This is distinct from the empty-array case — the array is non-empty but filters down to nothing.")]
	public void LoadFromStream_ShouldThrow_WhenAllEntriesHaveBlankRequestType() {
		// Arrange — a non-empty array whose entries are all blank, so the blank-entry filter leaves zero valid entries.
		const string payload = """
		{
		  "requests": [
		    { "requestType": "" },
		    { "requestType": "   " }
		  ]
		}
		""";
		using MemoryStream stream = new(Encoding.UTF8.GetBytes(payload));

		// Act
		Action act = () => RequestInfoCatalog.LoadFromStream(stream);

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "an array that filters down to zero valid request types is a malformed registry, not a valid empty state")
			.WithMessage("*does not contain valid request types*");
	}

	[Test]
	[Description("LoadAsync normalizes the requested version before querying the registry client: a blank/whitespace/null version resolves to the 'latest' alias, and a padded version is trimmed — so a caller passing an empty version still loads the latest catalog rather than a literal blank key.")]
	public async Task LoadAsync_ShouldNormalizeRequestedVersion_WhenBlankOrPadded() {
		// Arrange
		RecordingRequestRegistryClient client = new(ValidEnvelopeJson);
		RequestInfoCatalog catalog = new(client);

		// Act
		await catalog.LoadAsync("   ");
		await catalog.LoadAsync(null!);
		await catalog.LoadAsync("  8.3.4  ");

		// Assert
		client.RequestedVersions.Should().Equal(
			new[] { ComponentRegistryClient.LatestVersion, ComponentRegistryClient.LatestVersion, "8.3.4" },
			because: "blank/whitespace/null normalizes to the latest alias and a padded version is trimmed before the registry client is queried");
	}

	/// <summary>
	/// Records the (normalized) version each <see cref="RequestInfoCatalog.LoadAsync"/> call forwards to the
	/// registry client, serving a fixed valid payload — lets a test assert the version-normalization LoadAsync
	/// applies before the byte-transport chain sees the key.
	/// </summary>
	private sealed class RecordingRequestRegistryClient(string registryJson) : IRequestRegistryClient {
		private readonly byte[] _payload = Encoding.UTF8.GetBytes(registryJson);

		public List<string> RequestedVersions { get; } = new();

		public Task<ComponentRegistryFetchResult> GetAsync(string requestedVersion, CancellationToken cancellationToken = default) {
			RequestedVersions.Add(requestedVersion);
			return Task.FromResult(new ComponentRegistryFetchResult(
				new MemoryStream(_payload, writable: false),
				requestedVersion,
				ComponentRegistrySource.Cdn));
		}

		public Task<bool> RefreshAsync(string version, CancellationToken cancellationToken = default) => Task.FromResult(false);
	}

	[Test]
	[Description("An empty parameters map on an entry round-trips as an empty dictionary, not null — 'accepts no parameters' is a meaningful contract distinct from 'parameters unknown'.")]
	public void LoadFromStream_ShouldPreserveEmptyParametersMap_WhenEntryDeclaresIt() {
		// Arrange
		using MemoryStream stream = new(Encoding.UTF8.GetBytes(ValidEnvelopeJson));

		// Act
		RequestCatalogState state = RequestInfoCatalog.LoadFromStream(stream);

		// Assert
		state.Lookup["crt.ClosePageRequest"].Parameters.Should().NotBeNull(
			because: "the producer explicitly declared 'parameters': {} on the entry");
		state.Lookup["crt.ClosePageRequest"].Parameters.Should().BeEmpty(
			because: "an explicitly empty map means the request accepts no parameters");
	}
}
