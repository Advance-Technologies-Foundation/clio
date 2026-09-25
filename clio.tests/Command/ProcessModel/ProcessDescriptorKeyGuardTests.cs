using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Clio.Command.ProcessModel;
using Clio.Common;
using Clio.Project.NuGet;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.ProcessModel;

/// <summary>
/// Unit tests for <see cref="ProcessDescriptorKeyGuard"/>: when an unknown key is refused, when it is only a
/// warning, and that a clean call never asks for the installed version (ENG-95244, TC-U-09..TC-U-11).
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "ProcessModel")]
public sealed class ProcessDescriptorKeyGuardTests {

	private const string Bundled = "1.6.6.23";

	private static readonly JsonNode TypoDescriptor =
		JsonNode.Parse("""{"flows":[{"source":"S","target":"E","lable":"Go"}]}""");

	private IRequiredPackageChecker _checker;
	private IBundledPackageCatalog _catalog;

	[SetUp]
	public void SetUp() {
		_checker = Substitute.For<IRequiredPackageChecker>();
		_catalog = Substitute.For<IBundledPackageCatalog>();
		BundledVersionIs(Bundled);
	}

	private void BundledVersionIs(string version) =>
		_catalog.TryGetVersion(BundledPackages.ProcessBuilderPackageName, out Arg.Any<PackageVersion>(), out Arg.Any<string>())
			.Returns(call => {
				call[1] = version is null ? null : PackageVersion.ParseVersion(version);
				call[2] = version is null ? "unreadable" : null;
				return version is not null;
			});

	private void InstalledVersionIs(string version) =>
		_checker.GetInstalledVersion(BundledPackages.ProcessBuilderPackageName)
			.Returns(version is null ? null : PackageVersion.ParseVersion(version));

	private ProcessDescriptorKeyGuard Guard() => new(new ProcessDescriptorKeyValidator(), _checker, _catalog);

	[Test]
	[Description("An unknown key on an environment at the bundled version is REFUSED, and the refusal names the path, the intended key, and that nothing was sent.")]
	public void Enforce_ShouldRefuse_WhenTheEnvironmentRunsTheBundledVersion() {
		// Arrange
		InstalledVersionIs(Bundled);

		// Act
		Action act = () => Guard().Enforce(TypoDescriptor, ProcessWritePayload.CreateDescriptor);

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "the server this clio's contract describes would drop the key and still report success")
			.WithMessage("*flows[0].lable*'label'*")
			.WithMessage("*nothing was sent*");
	}

	[Test]
	[Description("An unknown key on an environment OLDER than the bundle is refused as well. Convergence normally refuses such an environment first; the guard does not rely on it, because an older package knows fewer keys, never more.")]
	public void Enforce_ShouldRefuse_WhenTheEnvironmentIsOlderThanTheBundle() {
		// Arrange
		InstalledVersionIs("1.6.6.20");

		// Act
		Action act = () => Guard().Enforce(TypoDescriptor, ProcessWritePayload.CreateDescriptor);

		// Assert
		act.Should().Throw<InvalidOperationException>(
			because: "a key unknown to the bundled contract is unknown to every older one too");
	}

	[Test]
	[Description("An unknown key on an environment NEWER than the bundle is a WARNING and the call proceeds: a developer build may accept a member this clio does not know yet, and refusing it would block a working call.")]
	public void Enforce_ShouldWarnAndProceed_WhenTheEnvironmentIsNewerThanTheBundle() {
		// Arrange
		InstalledVersionIs("1.6.6.27");

		// Act
		IReadOnlyList<string> warnings = Guard().Enforce(TypoDescriptor, ProcessWritePayload.CreateDescriptor);

		// Assert
		warnings.Should().ContainSingle(because: "one unknown key is one warning");
		warnings[0].Should().Contain("flows[0].lable").And.Contain("1.6.6.27").And.Contain("'label'",
			because: "the warning names the key, why it was sent anyway, and what it may have been meant to be");
	}

	[Test]
	[Description("When either version cannot be read the guard cannot decide, so it warns and lets the call proceed - the same answer convergence gives in that state.")]
	[TestCase(null, Bundled)]
	[TestCase(Bundled, null)]
	public void Enforce_ShouldWarn_WhenAVersionCannotBeRead(string installed, string bundled) {
		// Arrange
		InstalledVersionIs(installed);
		BundledVersionIs(bundled);

		// Act
		IReadOnlyList<string> warnings = Guard().Enforce(TypoDescriptor, ProcessWritePayload.CreateDescriptor);

		// Assert
		warnings.Should().ContainSingle(line => line.Contains("could not compare"),
			because: "an undecidable comparison must not turn into a refusal of a call the server may accept");
	}

	[Test]
	[Description("A clean payload never reads the installed version: the lookup is a Creatio round trip, and a clean call must cost nothing extra.")]
	public void Enforce_ShouldNotReadTheInstalledVersion_WhenEveryKeyIsKnown() {
		// Arrange
		JsonNode clean = JsonNode.Parse("""[{"op":"removeFlow","source":"S","target":"E"}]""");

		// Act
		IReadOnlyList<string> warnings = Guard().Enforce(clean, ProcessWritePayload.ModifyOperations);

		// Assert
		warnings.Should().BeEmpty(because: "nothing is unknown");
		_checker.DidNotReceiveWithAnyArgs().GetInstalledVersion(default);
	}

	[Test]
	[Description("The refusal lists at most MaxListedKeys keys and counts the rest, so a descriptor with many mistakes produces a bounded message that still says how many there were.")]
	public void Enforce_ShouldBoundTheRefusal_WhenManyKeysAreUnknown() {
		// Arrange
		InstalledVersionIs(Bundled);
		int count = ProcessDescriptorKeyGuard.MaxListedKeys + 3;
		JsonArray flows = new();
		for (int i = 0; i < count; i++) {
			flows.Add(new JsonObject { ["source"] = "S", ["target"] = "E", ["lable"] = "x" });
		}
		JsonObject descriptor = new() { ["flows"] = flows };

		// Act
		Action act = () => Guard().Enforce(descriptor, ProcessWritePayload.CreateDescriptor);

		// Assert
		string message = act.Should().Throw<InvalidOperationException>(because: "every key is unknown").Which.Message;
		message.Should().Contain($"{count} key(s)").And.Contain("and 3 more",
			because: "the total is stated even when the list is cut");
		message.Split(Environment.NewLine).Count(line => line.StartsWith("- flows[")).Should()
			.Be(ProcessDescriptorKeyGuard.MaxListedKeys, because: "only the first MaxListedKeys keys are listed");
	}
}
