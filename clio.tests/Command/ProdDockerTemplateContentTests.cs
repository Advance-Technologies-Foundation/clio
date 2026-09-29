using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// Content guard for the bundled <c>prod</c> Docker template's OpenShift hardening step. The step creates
/// the <c>/conf</c> and <c>/Terrasoft.Configuration</c> compatibility links, and it must stay idempotent over
/// a base image that already carries exactly those links (the Creatio .NET 10 product image and any image a
/// previous <c>--template prod</c> build produced), while still failing the build on any other pre-existing
/// layout. The Dockerfile cannot be executed in a cross-platform unit test, so the guard's shape is asserted.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class ProdDockerTemplateContentTests {
	private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

	private static readonly (string Target, string Link)[] CompatibilityLinks = [
		("/app/conf", "/conf"),
		("/app/Terrasoft.Configuration", "/Terrasoft.Configuration")
	];

	private static string ProdDockerfilePath =>
		Path.Combine(AppContext.BaseDirectory, "tpl", "docker-templates", "prod", "Dockerfile");

	[Test]
	[Description("The prod Dockerfile guard should keep a compatibility link that already is a symlink whose readlink target is exactly the expected /app path.")]
	public void ProdDockerfile_ShouldKeepCompatibilityLinkThatAlreadyPointsAtExpectedTarget() {
		// Arrange
		string guardBody = ReadLinkGuardBody();

		// Act
		Match keepBranch = Regex.Match(guardBody,
			@"^\s*if\s+\[\s*-L\s+""\$2""\s*\]\s*&&\s*\[\s*""\$\(readlink\s+""\$2""\)""\s*=\s*""\$1""\s*\]\s*;\s*then\s+(?<body>.*?)\belif\b",
			RegexOptions.Singleline, RegexTimeout);

		// Assert
		keepBranch.Success.Should().BeTrue(
			"because the first branch must recognise an existing symlink whose readlink target is exactly the expected path, which is how an already-hardened base image looks");
		keepBranch.Groups["body"].Value.Should().NotMatchRegex(@"\bln\b|\breturn\s+1\b|\bexit\b",
			"because an already-correct link must be kept as is, neither recreated nor treated as a failure");
	}

	[Test]
	[Description("The prod Dockerfile guard should fail the build when a compatibility link path exists as anything other than the expected symlink.")]
	public void ProdDockerfile_ShouldFailWhenCompatibilityLinkPathExistsWithOtherContent() {
		// Arrange
		string guardBody = ReadLinkGuardBody();

		// Act
		Match failBranch = Regex.Match(guardBody,
			@"\belif\s+\[\s*-e\s+""\$2""\s*\]\s*\|\|\s*\[\s*-L\s+""\$2""\s*\]\s*;\s*then\s+(?<body>.*?)\belse\b",
			RegexOptions.Singleline, RegexTimeout);

		// Assert
		failBranch.Success.Should().BeTrue(
			"because any other existing file, directory, or symlink (including a dangling one) at the link path must be detected");
		failBranch.Groups["body"].Value.Should().MatchRegex(@"\breturn\s+1\b",
			"because an unexpected pre-existing layout must fail the image build instead of being layered over");
		failBranch.Groups["body"].Value.Should().Contain(">&2",
			"because the build failure must carry an explanatory message on stderr");
		failBranch.Groups["body"].Value.Should().NotMatchRegex(@"\bln\b",
			"because an unexpected pre-existing path must never be replaced");
	}

	[Test]
	[Description("The prod Dockerfile should create both compatibility links only through the guard and never force-replace an existing path.")]
	public void ProdDockerfile_ShouldCreateCompatibilityLinksOnlyThroughGuard() {
		// Arrange
		string hardeningInstruction = ReadHardeningRunInstruction();
		string guardBody = ReadLinkGuardBody();

		// Act
		int lnInvocationCount = Regex.Matches(hardeningInstruction, @"\bln\s", RegexOptions.None, RegexTimeout).Count;
		bool createsInElseBranch = Regex.IsMatch(guardBody,
			@"\belse\s+ln\s+-sT\s+""\$1""\s+""\$2""\s*;\s*fi\b", RegexOptions.None, RegexTimeout);

		// Assert
		lnInvocationCount.Should().Be(1,
			"because the only link creation must be the guarded one; an unconditional 'ln -sT /app/conf /conf' fails with 'File exists' over an already-hardened base image");
		createsInElseBranch.Should().BeTrue(
			"because a missing link path must still be created with 'ln -sT' so the hardened layout exists on a plain base image");
		hardeningInstruction.Should().NotMatchRegex(@"\bln\s+-[A-Za-z]*f",
			"because 'ln -f' would silently replace an unexpected pre-existing path, which the guard exists to prevent");
		foreach ((string target, string link) in CompatibilityLinks) {
			hardeningInstruction.Should().MatchRegex($@"\bensure_link\s+{Regex.Escape(target)}\s+{Regex.Escape(link)}\s*&&",
				$"because the '{link}' -> '{target}' compatibility link must be created through the guard");
		}

		hardeningInstruction.Should().Contain("test -e /conf && test -e /Terrasoft.Configuration",
			"because the step must still prove both links resolve after it ran");
	}

	[Test]
	[Description("The prod Dockerfile should empty /app before copying the source, so a populated base image's stale assemblies, packages, and db backup never survive into the new image.")]
	public void ProdDockerfile_ShouldReplaceAppWholesaleBeforeCopyingSource() {
		// Arrange
		string[] instructions = ReadInstructions();

		// Act
		int resetIndex = Array.FindIndex(instructions, instruction =>
			instruction.StartsWith("RUN ", StringComparison.Ordinal)
			&& Regex.IsMatch(instruction, @"\brm\s+-rf\s+/app(?=\s|;|$)", RegexOptions.None, RegexTimeout)
			&& Regex.IsMatch(instruction, @"\bmkdir\s+(?:-p\s+)?/app(?=\s|;|$)", RegexOptions.None, RegexTimeout));
		int firstAppReference = Array.FindIndex(instructions, instruction =>
			instruction.Contains("/app", StringComparison.Ordinal));
		int copySourceIndex = Array.FindIndex(instructions, instruction =>
			Regex.IsMatch(instruction, @"^COPY\s+source/\s+\./?$", RegexOptions.None, RegexTimeout));
		int workdirIndex = Array.IndexOf(instructions, "WORKDIR /app");

		// Assert
		resetIndex.Should().BeGreaterThanOrEqualTo(0,
			"because /app must be removed and recreated so the image holds exactly the source payload, not a merge with the parent image's /app");
		firstAppReference.Should().Be(resetIndex,
			"because nothing may touch /app before it is reset, or a parent image's content (or an /app symlink) would be used first");
		workdirIndex.Should().BeGreaterThan(resetIndex,
			"because WORKDIR must point at the freshly created /app, not at a directory that is deleted afterwards");
		copySourceIndex.Should().BeGreaterThan(workdirIndex,
			"because the source must be copied into the reset /app");
	}

	[Test]
	[Description("The prod Dockerfile should verify that /app/conf and /app/Terrasoft.Configuration are real directories before any recursive chmod, so the group-write grant can never follow a symlink out of /app.")]
	public void ProdDockerfile_ShouldVerifyWritableDirectoriesAreRealBeforeGrantingGroupWrite() {
		// Arrange
		string hardeningInstruction = ReadHardeningRunInstruction();

		// Act
		Match guard = Regex.Match(hardeningInstruction,
			@"\bensure_real_dir\s*\(\s*\)\s*\{(?<body>.*?)\}\s*&&", RegexOptions.Singleline, RegexTimeout);
		int firstRecursiveChmod = hardeningInstruction.IndexOf("chmod -R", StringComparison.Ordinal);

		// Assert
		guard.Success.Should().BeTrue(
			"because the hardening step must define an 'ensure_real_dir <path>' guard for the group-writable directories");
		guard.Groups["body"].Value.Should().MatchRegex(
			@"\bif\s+\[\s*-L\s+""\$1""\s*\]\s*\|\|\s*\[\s*!\s+-d\s+""\$1""\s*\]\s*;\s*then\b.*>&2.*\breturn\s+1\b",
			"because a symlink or a non-directory must fail the build with a message instead of being chmod-ed through");
		firstRecursiveChmod.Should().BeGreaterThan(0, "because the step still grants group access recursively");
		foreach (string directory in new[] { "/app/conf", "/app/Terrasoft.Configuration" }) {
			int guardCall = Regex.Match(hardeningInstruction,
				$@"\bensure_real_dir\s+{Regex.Escape(directory)}\s*&&", RegexOptions.None, RegexTimeout).Index;
			guardCall.Should().BeInRange(1, firstRecursiveChmod - 1,
				$"because '{directory}' must be proven a real directory before 'chmod -R' dereferences it as an operand");
		}

		hardeningInstruction.Should().MatchRegex(@"find\s+/app\s+-maxdepth\s+1\s+-type\s+f\s",
			"because the config-file chmod must not follow a symlinked ConnectionStrings.config or Terrasoft.WebHost.dll.config out of /app");
	}

	private static string[] ReadInstructions() {
		File.Exists(ProdDockerfilePath).Should().BeTrue(
			$"because the bundled prod Dockerfile must be copied to the test output at '{ProdDockerfilePath}' (clio.csproj copies tpl/**)");
		string dockerfile = File.ReadAllText(ProdDockerfilePath);
		string joined = Regex.Replace(dockerfile, @"\\\r?\n", " ", RegexOptions.None, RegexTimeout);
		return joined
			.Split('\n')
			.Select(line => line.Trim())
			.Where(line => line.Length > 0 && !line.StartsWith('#'))
			.ToArray();
	}

	private static string ReadHardeningRunInstruction() {
		string[] matchingInstructions = ReadInstructions()
			.Where(line => line.StartsWith("RUN ", StringComparison.Ordinal)
				&& line.Contains("/Terrasoft.Configuration", StringComparison.Ordinal))
			.ToArray();
		matchingInstructions.Should().HaveCount(1,
			"because exactly one RUN instruction owns the /conf and /Terrasoft.Configuration hardening step");
		return matchingInstructions[0];
	}

	private static string ReadLinkGuardBody() {
		string hardeningInstruction = ReadHardeningRunInstruction();
		Match guard = Regex.Match(hardeningInstruction,
			@"\bensure_link\s*\(\s*\)\s*\{(?<body>.*?)\}\s*&&",
			RegexOptions.Singleline, RegexTimeout);
		guard.Success.Should().BeTrue(
			"because the hardening step must define an 'ensure_link <target> <link>' guard used for both compatibility links");
		return guard.Groups["body"].Value;
	}
}
