using System.IO;
using ClioRing.Services;
using FluentAssertions;
using NUnit.Framework;

namespace ClioRing.Tests;

/// <summary>
/// Unit tests for <see cref="DotNetHostResolver"/>: the trusted, absolute <c>dotnet</c> host resolution
/// shared by <see cref="ClioToolUpdateService"/> and <see cref="DevClioLaunch"/>. Pins the security
/// invariant that the resolved value is always an absolute path, never a bare <c>"dotnet"</c> name that
/// would be resolved via <c>CreateProcess</c>'s search order (calling-process directory first, then
/// <c>PATH</c>).
/// </summary>
[TestFixture]
[Category("Unit")]
public sealed class DotNetHostResolverTests {
	[Test]
	[Description("ResolveOrDefault never returns the bare 'dotnet' name; it is always rooted (absolute).")]
	public void ResolveOrDefault_ShouldReturnAbsolutePath_Always() {
		// Arrange — no setup needed; the resolver reads only environment variables and the filesystem.

		// Act — resolve the trusted dotnet host.
		string resolved = DotNetHostResolver.ResolveOrDefault();

		// Assert — the result is a rooted, absolute path and not the bare PATH-resolved name.
		Path.IsPathRooted(resolved).Should().BeTrue(
			because: "the resolved host must be an absolute path so it cannot be hijacked via PATH search order");
		resolved.Should().NotBe("dotnet",
			because: "falling back to the bare name would reintroduce the PATH-hijack defect this resolver exists to close");
	}

	[Test]
	[Description("ResolveOrDefault is stable across repeated calls for the same environment.")]
	public void ResolveOrDefault_ShouldReturnSameValue_WhenCalledTwice() {
		// Arrange — no setup needed; resolution is a pure function of environment state.

		// Act — resolve twice.
		string first = DotNetHostResolver.ResolveOrDefault();
		string second = DotNetHostResolver.ResolveOrDefault();

		// Assert — resolution is deterministic for an unchanged environment.
		second.Should().Be(first, because: "resolution must be deterministic so launch settings are reproducible");
	}
}
