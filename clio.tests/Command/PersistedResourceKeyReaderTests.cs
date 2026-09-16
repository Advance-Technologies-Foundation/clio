using System;
using System.Collections.Generic;
using Clio.Command;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// PR #1495 review (ArtemKulykov): the reader carries scope-lifetime mutable state across three call
/// sites and an explicit "failures are not cached, but their reason is" rule, and had no fixture of its
/// own — which is why a failure that outlived its own successful retry, and two gates keying one stand by
/// different fields, both survived. Those are the first two cases here.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class PersistedResourceKeyReaderTests {

	private const string SchemaName = "Test_FormPage";

	private static PageUpdateOptions Options(string environment = null, string uri = null) =>
		new() { Environment = environment, Uri = uri, SchemaName = SchemaName };

	/// <summary>
	/// A resolver that folds a registered environment NAME and that environment's URL onto one key,
	/// which is exactly what the production <c>ToolCommandResolver.GetTargetKey</c> does and what the
	/// hand-rolled <c>FirstNonBlank(Environment, Uri)</c> did not.
	/// </summary>
	private static IToolCommandResolver ResolverFoldingNameAndUrl(string environmentName, string url) {
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.GetTargetKey(Arg.Any<EnvironmentOptions>()).Returns(callInfo => {
			EnvironmentOptions options = callInfo.Arg<EnvironmentOptions>();
			bool namesTheSameStand = options.Environment == environmentName || options.Uri == url;
			return namesTheSameStand ? url : "unresolved:other";
		});
		return resolver;
	}

	[Test]
	[Description("A successful read clears the failure recorded by an earlier failed read of the same target, so a page that succeeded is not annotated with strictness that was never applied.")]
	public void Read_ShouldClearTheRecordedFailure_WhenALaterReadOfTheSameTargetSucceeds() {
		// Arrange
		IToolCommandResolver resolver = ResolverFoldingNameAndUrl("dev", "http://dev/");
		PersistedResourceKeyReader reader = new(resolver);
		using IDisposable scope = reader.BeginRequestScope();
		PageUpdateOptions options = Options(environment: "dev");

		// Act
		reader.Read(options, () => PersistedResourceKeyRead.Failure("the environment was unreachable"));
		string warningAfterFailure = reader.GetFailureWarning(options);
		reader.Read(options, () => PersistedResourceKeyRead.FromKeys(new HashSet<string> { "Resources.Strings.Caption" }));

		// Assert
		warningAfterFailure.Should().NotBeNullOrWhiteSpace(
			because: "a failed read is deliberately not cached, so its REASON is what has to survive until the retry");
		reader.GetFailureWarning(options).Should().BeNull(
			because: "the retry succeeded, and reporting the earlier failure would tell the caller the strict verdict was applied to a page that in fact validated against real keys");
	}

	[Test]
	[Description("Invalidate clears the recorded failure as well as the cached keys, so the next read is not pre-judged by a reason its own retry may not reproduce.")]
	public void Invalidate_ShouldClearTheRecordedFailure() {
		// Arrange
		PersistedResourceKeyReader reader = new(ResolverFoldingNameAndUrl("dev", "http://dev/"));
		using IDisposable scope = reader.BeginRequestScope();
		PageUpdateOptions options = Options(environment: "dev");
		reader.Read(options, () => PersistedResourceKeyRead.Failure("the environment was unreachable"));

		// Act
		reader.Invalidate(options);

		// Assert
		reader.GetFailureWarning(options).Should().BeNull(
			because: "invalidating the key while keeping the reason leaves the next read reporting a failure that nothing re-established");
	}

	[Test]
	[Description("Two gates addressing one stand by different fields - a registered name and that environment's URL - share one cache entry, because the key comes from GetTargetKey rather than from whichever field happened to be populated.")]
	public void Read_ShouldShareOneCacheEntry_WhenTwoGatesNameTheSameStandDifferently() {
		// Arrange
		PersistedResourceKeyReader reader = new(ResolverFoldingNameAndUrl("dev", "http://dev/"));
		using IDisposable scope = reader.BeginRequestScope();
		int reads = 0;
		PersistedResourceKeyRead Read() {
			reads++;
			return PersistedResourceKeyRead.FromKeys(new HashSet<string> { "Resources.Strings.Caption" });
		}

		// Act
		reader.Read(Options(environment: "dev"), Read);
		reader.Read(Options(uri: "http://dev/"), Read);

		// Assert
		reads.Should().Be(1,
			because: "the two option shapes name the SAME stand, and keying by whichever field is populated made the memoisation silently stop working the moment two gates disagreed on which one to fill");
	}

	[Test]
	[Description("Two genuinely different schemas on one stand keep separate entries, so folding the environment key does not fold the targets.")]
	public void Read_ShouldKeepSeparateEntries_ForDifferentSchemasOnOneStand() {
		// Arrange
		PersistedResourceKeyReader reader = new(ResolverFoldingNameAndUrl("dev", "http://dev/"));
		using IDisposable scope = reader.BeginRequestScope();
		int reads = 0;
		PersistedResourceKeyRead Read() {
			reads++;
			return PersistedResourceKeyRead.FromKeys(new HashSet<string> { "Resources.Strings.Caption" });
		}

		// Act
		reader.Read(new PageUpdateOptions { Environment = "dev", SchemaName = "First_FormPage" }, Read);
		reader.Read(new PageUpdateOptions { Environment = "dev", SchemaName = "Second_FormPage" }, Read);

		// Assert
		reads.Should().Be(2,
			because: "the key names what is actually read, and two schemas are two reads however the stand is addressed");
	}
}
