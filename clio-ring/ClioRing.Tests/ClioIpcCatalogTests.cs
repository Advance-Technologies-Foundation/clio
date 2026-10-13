using System.Collections.Generic;
using System.Linq;
using ClioRing.Ipc;
using FluentAssertions;
using NUnit.Framework;

namespace ClioRing.Tests;

/// <summary>
/// The catalog Ring parses out of get-tool-contract replies. Ring and clio are released independently, so the
/// reply can come from a clio that answers bare <c>{}</c> with <c>tool-index</c> text lines and returns the
/// per-tool objects only for <c>detail=index-entries</c>, from an older clio that returns the objects for any
/// request without names, or from the oldest builds with the full <c>tools</c> shape.
/// </summary>
[TestFixture]
[Category("Unit")]
public sealed class ClioIpcCatalogTests {
	// Shaped like clio's reply to detail=index-entries, and like an older clio's reply to any no-names request.
	private const string ObjectIndexReply =
		"{\"success\":true,\"tools\":null,\"error\":null,\"index\":["
		+ "{\"name\":\"list-apps\",\"purpose\":\"Lists installed applications.\",\"contract-available\":true,\"resident\":true,\"destructive\":false},"
		+ "{\"name\":\"stop-creatio\",\"purpose\":\"Stops Creatio instance by environment name\",\"contract-available\":true,\"resident\":false,\"destructive\":true}"
		+ "]}";

	// Shaped like the default reply of a clio with the line index: no object index, nothing with flags to parse.
	private const string LineIndexReply =
		"{\"success\":true,\"tools\":null,\"error\":null,\"index\":null,"
		+ "\"legend\":\"One line per tool: name (R)(D) [aka deprecated names]: purpose, cut to 48 characters.\","
		+ "\"tool-index\":[\"list-apps (R): Lists installed applications.\",\"stop-creatio (D): Stops Creatio instance by environment name\"]}";

	// An object index whose entries carry no usable destructive flag: null, as clio writes when it cannot tell, and
	// absent altogether.
	private const string UnknownDestructiveReply =
		"{\"success\":true,\"index\":["
		+ "{\"name\":\"install-gate\",\"purpose\":\"Installs cliogate.\",\"contract-available\":true,\"resident\":false,\"destructive\":null},"
		+ "{\"name\":\"reg-web-app\",\"purpose\":\"Registers an environment.\",\"contract-available\":true,\"resident\":false}"
		+ "]}";

	private const string OldestFullToolsReply =
		"{\"success\":true,\"tools\":[{\"name\":\"list-apps\",\"description\":\"Lists installed applications.\"}]}";

	[Test]
	[Description("The object index parses into entries with the resident and destructive flags Ring gates its actions on.")]
	public void ParseCatalog_ShouldReadTheFlags_WhenReplyCarriesTheObjectIndex() {
		// Arrange
		// (reply literal above)

		// Act
		(IReadOnlyList<ClioCatalogEntry> entries, bool modern) = ClioIpcClient.ParseCatalog(ObjectIndexReply);

		// Assert
		modern.Should().BeTrue(
			because: "the object index is the modern compact shape with safety flags");
		entries.Should().BeEquivalentTo(new[] {
				new ClioCatalogEntry { Name = "list-apps", Purpose = "Lists installed applications.", ContractAvailable = true, Resident = true, Destructive = false },
				new ClioCatalogEntry { Name = "stop-creatio", Purpose = "Stops Creatio instance by environment name", ContractAvailable = true, Resident = false, Destructive = true }
			}, options => options.WithStrictOrdering(),
			because: "Ring's destructive confirmation and resident dispatch read these flags");
	}

	[Test]
	[Description("The default reply of a clio with the line index parses as an empty, not-modern catalog: its tool-index lines carry no flags, which is why Ring asks for detail=index-entries rather than reading them.")]
	public void ParseCatalog_ShouldReturnEmpty_WhenReplyCarriesOnlyTheLineIndex() {
		// Arrange
		// (reply literal above)

		// Act
		(IReadOnlyList<ClioCatalogEntry> entries, bool modern) = ClioIpcClient.ParseCatalog(LineIndexReply);

		// Assert
		entries.Should().BeEmpty(
			because: "text lines are for an agent; reading flags out of them would make Ring depend on wording");
		modern.Should().BeFalse(
			because: "an empty catalog must never be reported as the modern compact index");
	}

	[Test]
	[Description("The oldest clio, with the full tools shape, still yields a catalog, reported as not modern so the UI shows the incompatible-clio state.")]
	public void ParseCatalog_ShouldReportNotModern_WhenReplyCarriesTheFullToolsShape() {
		// Arrange
		// (reply literal above)

		// Act
		(IReadOnlyList<ClioCatalogEntry> entries, bool modern) = ClioIpcClient.ParseCatalog(OldestFullToolsReply);

		// Assert
		entries.Select(entry => entry.Name).Should().Equal(new[] { "list-apps" },
			because: "the full tools shape still names the tools");
		modern.Should().BeFalse(
			because: "without the safety flags Ring must not present the catalog as the modern one");
	}

	[Test]
	[Description("An entry whose destructive flag is null or absent parses as destructive, so Ring asks before running it rather than treating an unknown flag as safe.")]
	public void ParseCatalog_ShouldTreatAnUnknownDestructiveFlagAsDestructive_WhenTheFlagIsNullOrAbsent() {
		// Arrange
		// (reply literal above)

		// Act
		(IReadOnlyList<ClioCatalogEntry> entries, _) = ClioIpcClient.ParseCatalog(UnknownDestructiveReply);

		// Assert
		entries.Should().HaveCount(2, because: "anti-vacuity: both entries were parsed");
		entries.Should().OnlyContain(entry => entry.Destructive,
			because: "Ring enables Run without confirmation only for a tool clio reports as not destructive, and an "
				+ "unknown flag is not that report");
	}

	[Test]
	[Description("The catalog request Ring sends asks for the object index explicitly, so a clio whose default no-names reply is text lines still returns the per-tool flags; reverting to a bare {} would leave Ring with an empty catalog.")]
	public void CatalogRequest_ShouldAskForTheObjectIndex() {
		// Arrange
		// (constant under test)

		// Act
		string request = ClioIpcClient.CatalogRequest;

		// Assert
		request.Should().Be("{\"args\":{\"detail\":\"index-entries\"}}",
			because: "index-entries is the only no-names request a lines-by-default clio answers with objects, and the "
				+ "args wrapper is the shape every clio build accepts");
	}
}
