using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Clio.Common;

namespace Clio.Tests.Common;

/// <summary>
/// The C# source of the CrtProcessBuilder archive clio bundles, read from this test project's build output, for
/// tests that hold clio's own text to the server contract that archive defines.
/// </summary>
internal static class BundledProcessBuilderSource {

	internal static string ArchivePath => Path.Combine(
		AppContext.BaseDirectory,
		BundledPackages.ProcessBuilderPackageName,
		BundledPackages.ProcessBuilderArchiveFileName);

	/// <summary>
	/// Decompresses the archive and returns it as text. The stream is clio's own package format and
	/// carries binary members, so only file CONTENT that happens to be UTF-8 (the JSON descriptors, the C#
	/// sources, the csproj) is searchable here.
	/// </summary>
	/// <remarks>
	/// Entry PATHS are NOT searchable and never were: the container stores them UTF-16LE. List entries through
	/// <see cref="ICompressionUtilities.ListGZipEntryNames"/> for anything about a path, and treat a hit found here
	/// as evidence about a file's contents only.
	/// </remarks>
	internal static string ReadAsText() {
		using FileStream compressed = File.OpenRead(ArchivePath);
		using GZipStream decompressor = new(compressed, CompressionMode.Decompress);
		using MemoryStream buffer = new();
		decompressor.CopyTo(buffer);
		return Encoding.UTF8.GetString(buffer.ToArray());
	}

	/// <summary>
	/// The <c>#region Class: &lt;typeName&gt;</c> block of one contract type, or <see langword="null"/> when the
	/// archive declares no such region or leaves it unterminated.
	/// </summary>
	/// <remarks>
	/// The archive ships SOURCE and every contract type sits in its own region, so the region is that type's
	/// extent - which is what turns a name appearing on three contracts into an answer about ONE of them. Two ways
	/// this could quietly widen into a whole-archive search are closed: the region name is matched WITH its line
	/// terminator, so a later <c>#region Class: &lt;Name&gt;V2</c> cannot be found by a prefix search for
	/// &lt;Name&gt;; and an UNTERMINATED region answers null rather than taking the rest of the archive as its
	/// window, where a member on any later type would satisfy the probe. The region ends at the
	/// <c>#endregion</c> that balances it, not the first one: a nested region such as <c>#region Properties</c>
	/// would otherwise cut the window short, and a drift test would then report the members after it as fields
	/// the server lacks, blaming the shape instead of this reader.
	/// </remarks>
	internal static string? ClassRegion(string archive, string typeName) {
		int start = archive.IndexOf($"#region Class: {typeName}\r", StringComparison.Ordinal);
		if (start < 0) {
			start = archive.IndexOf($"#region Class: {typeName}\n", StringComparison.Ordinal);
		}
		if (start < 0) {
			return null;
		}
		int end = BalancingEndRegion(archive, start);
		return end < 0 ? null : archive[start..end];
	}

	private static int BalancingEndRegion(string archive, int regionStart) {
		int depth = 0;
		foreach (Match directive in RegionDirective.Matches(archive, regionStart)) {
			depth += directive.Groups["end"].Success ? -1 : 1;
			if (depth == 0) {
				return directive.Index;
			}
		}
		return -1;
	}

	/// <summary>
	/// The wire names of every <c>[DataMember]</c> one contract type declares, in declaration order; empty when the
	/// type's region is absent.
	/// </summary>
	internal static IReadOnlyList<string> DataMemberNames(string archive, string typeName) =>
		ClassRegion(archive, typeName) is { } region
			? DataMemberName.Matches(region).Select(match => match.Groups["name"].Value).ToArray()
			: [];

	/// <summary>
	/// The <c>op</c> tokens <c>ProcessDesignService.ModifyProcess</c> accepts: the server's
	/// <c>Operations.All</c>, each identifier resolved to its constant.
	/// </summary>
	/// <remarks>
	/// <c>Operations.All</c> is the server's single vocabulary - its own composition test holds every token to
	/// exactly one registered operation strategy - so reading it is reading what the tool accepts, not a second
	/// hand-written list.
	/// </remarks>
	/// <exception cref="InvalidOperationException">The archive no longer declares the vocabulary in that shape.</exception>
	internal static IReadOnlyList<string> ModifyOperationTokens(string archive) {
		int classStart = archive.IndexOf("internal static class Operations", StringComparison.Ordinal);
		int allStart = classStart < 0
			? -1
			: archive.IndexOf("IReadOnlyList<string> All = Array.AsReadOnly(new[] {", classStart, StringComparison.Ordinal);
		int allEnd = allStart < 0 ? -1 : archive.IndexOf("});", allStart, StringComparison.Ordinal);
		if (allEnd < 0) {
			throw new InvalidOperationException(
				"The bundled CrtProcessBuilder no longer declares Operations.All as Array.AsReadOnly(new[] { ... }); "
				+ "re-point this reader at the server's operation vocabulary.");
		}
		Dictionary<string, string> constants = OperationConstant.Matches(archive[classStart..allStart])
			.ToDictionary(match => match.Groups["id"].Value, match => match.Groups["token"].Value, StringComparer.Ordinal);
		string list = archive[allStart..allEnd];
		return Identifier.Matches(list[(list.IndexOf('{') + 1)..])
			.Select(match => constants.TryGetValue(match.Value, out string? token)
				? token
				: throw new InvalidOperationException($"Operations.All names '{match.Value}', which is no constant."))
			.ToArray();
	}

	// Up to the name's closing quote, so a member declared with EmitDefaultValue = false counts too.
	private static readonly Regex DataMemberName = new(@"\[DataMember\(Name = ""(?<name>[^""]+)""",
		RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

	private static readonly Regex RegionDirective = new(@"#(?<end>end)?region\b", RegexOptions.CultureInvariant,
		TimeSpan.FromSeconds(1));

	private static readonly Regex OperationConstant = new(@"public const string (?<id>\w+) = ""(?<token>[^""]+)"";",
		RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

	private static readonly Regex Identifier = new(@"\b[A-Z]\w*\b", RegexOptions.CultureInvariant,
		TimeSpan.FromSeconds(1));
}
