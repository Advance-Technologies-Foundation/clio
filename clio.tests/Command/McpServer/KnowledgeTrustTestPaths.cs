using System;
using System.IO;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Supplies a temporary directory root whose ancestry contains no symbolic link, for fixtures that
/// write trust material a knowledge trust store then has to accept.
/// </summary>
/// <remarks>
/// The trust store refuses any public-key path with a reparse point (symbolic link) anywhere in its
/// existing ancestry, which is a deliberate guard. On macOS the default temporary root is
/// <c>/var/folders/…</c> and <c>/var</c> itself is a symbolic link to <c>/private/var</c>, so a
/// fixture writing under <see cref="Path.GetTempPath"/> is refused for the platform's own reason
/// rather than for anything the test arranged. Resolving the link once here keeps those fixtures
/// runnable on macOS, Linux and Windows alike without relaxing the guard under test.
/// <para>
/// This mirrors <c>Clio.Mcp.E2E.Support.PhysicalPath</c>, which solves the same problem for the
/// end-to-end fixtures; it is duplicated rather than shared because that type is internal to another
/// test assembly. Keep the two in step: the recursion deliberately stops before the path root,
/// because <see cref="Directory.ResolveLinkTarget"/> throws on a root such as <c>C:\</c> instead of
/// answering "not a link".
/// </para>
/// </remarks>
internal static class KnowledgeTrustTestPaths {
	/// <summary>
	/// Gets the temporary directory root with every symbolic link in its ancestry resolved.
	/// </summary>
	internal static string ResolvedTempRoot { get; } = ResolveTempRoot();

	private static string ResolveTempRoot() {
		string root = Path.GetFullPath(Path.GetTempPath());
		// A fixture must never fail in a type initializer: that surfaces as every test in the fixture
		// erroring in SetUp with the resolution failure buried in a TypeInitializationException. An
		// unresolvable root is not worth that, and the unresolved path is the correct answer wherever
		// no ancestor is linked anyway.
		try {
			return Resolve(root);
		} catch (Exception exception) when (exception is IOException
				or UnauthorizedAccessException
				or ArgumentException
				or NotSupportedException) {
			return root;
		}
	}

	private static string Resolve(string path) {
		string full = Path.GetFullPath(path);
		string? parent = Path.GetDirectoryName(full);
		if (parent is null) {
			return full;
		}
		string candidate = Path.Combine(Resolve(parent), Path.GetFileName(full));
		return Directory.ResolveLinkTarget(candidate, returnFinalTarget: true)?.FullName ?? candidate;
	}
}
