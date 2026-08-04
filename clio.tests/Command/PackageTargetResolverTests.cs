namespace Clio.Tests.Command;

using System;
using System.Text.Json;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

/// <summary>
/// Unit coverage for <see cref="IPackageTargetResolver"/>: the two ways a target package is chosen (a name the
/// caller passed, or the environment's <c>CurrentPackageId</c> setting), the editability check that keeps a
/// run from writing to a locked package, and the classification that tells "the environment answered and
/// there is no usable target" apart from "the environment could not be asked" — a caller that confuses the
/// two either tells the user a package does not exist because of a network blip, or asks them to pick another
/// package when nothing is wrong with theirs.
/// </summary>
[TestFixture]
[Property("Module", "Command")]
public sealed class PackageTargetResolverTests {

	private const string PackageName = "UsrBrandingPkg";
	private const string SelectUrl = "http://localhost/0/DataService/json/SyncReply/SelectQuery";
	private const int LockedInstallType = 1;

	private static readonly Guid PackageUId = Guid.Parse("1d07fd0e-2ca4-4d20-93b4-eb5a795ea03f");
	private static readonly Guid CurrentPackageRowId = Guid.Parse("2e3f4a5b-6c7d-4e8f-9a0b-1c2d3e4f5a6b");

	[Test, Category("Unit")]
	[Description("Resolves a package the caller named, matching the name as the environment reports it rather than as it was typed.")]
	public void Resolve_Should_Return_The_Named_Package() {
		// Arrange
		IPackageTargetResolver sut = CreateResolver(new ResolverEnvironment());

		// Act
		PackageTargetResolution resolution = sut.Resolve(PackageName.ToUpperInvariant());

		// Assert
		resolution.Success.Should().BeTrue(
			because: "the package exists and is open for design-time writes");
		resolution.PackageName.Should().Be(PackageName,
			because: "the name is reported back the way the environment spells it, so the caller states that spelling to the user");
		resolution.PackageUId.Should().Be(PackageUId,
			because: "the UId is what the binding endpoints address the package by");
	}

	[Test, Category("Unit")]
	[Description("Refuses a name the environment does not have, definitively, and points at the command that lists the real names.")]
	public void Resolve_Should_Refuse_A_Package_That_Does_Not_Exist() {
		// Arrange
		IPackageTargetResolver sut = CreateResolver(new ResolverEnvironment());

		// Act
		PackageTargetResolution resolution = sut.Resolve("NoSuchPackage");

		// Assert
		resolution.Success.Should().BeFalse(because: "there is no such package to deliver into");
		resolution.ResolutionFailed.Should().BeTrue(
			because: "the environment answered, so asking the user for another package is the only way forward");
		resolution.Error.Should().Contain("list-packages",
			because: "the caller must be able to tell the user where the real names come from");
	}

	[Test, Category("Unit")]
	[Description("Refuses a locked package before anything is written, and names the command that unlocks it.")]
	public void Resolve_Should_Refuse_A_Locked_Named_Package() {
		// Arrange
		IPackageTargetResolver sut = CreateResolver(new ResolverEnvironment { InstallType = LockedInstallType });

		// Act
		PackageTargetResolution resolution = sut.Resolve(PackageName);

		// Assert
		resolution.Success.Should().BeFalse(
			because: "a locked package cannot receive design-time writes, and finding that out from the write itself leaves the run half-applied");
		resolution.ResolutionFailed.Should().BeTrue(because: "the environment answered and the package is closed");
		resolution.Error.Should().Contain("unlock-package",
			because: "the user can fix this, so the message names how");
	}

	[Test, Category("Unit")]
	[Description("Resolves the package the environment's CurrentPackageId setting points at when the caller names none.")]
	public void Resolve_Should_Return_The_CurrentPackageId_Package_When_No_Name_Is_Passed() {
		// Arrange
		IPackageTargetResolver sut = CreateResolver(new ResolverEnvironment());

		// Act
		PackageTargetResolution resolution = sut.Resolve(null);

		// Assert
		resolution.Success.Should().BeTrue(
			because: "design-time writes land in the environment's current package, and package data follows the same convention");
		resolution.PackageName.Should().Be(PackageName,
			because: "the resolved name is what the caller states to the user, who never sees the setting's raw id");
	}

	[Test, Category("Unit")]
	[Description("Refuses definitively when no package is named and CurrentPackageId is unset, instead of picking a well-known package.")]
	public void Resolve_Should_Refuse_When_No_Name_Is_Passed_And_CurrentPackageId_Is_Unset() {
		// Arrange
		IPackageTargetResolver sut = CreateResolver(new ResolverEnvironment { CurrentPackageIdValue = string.Empty });

		// Act
		PackageTargetResolution resolution = sut.Resolve(string.Empty);

		// Assert
		resolution.Success.Should().BeFalse(because: "nothing names a target package");
		resolution.ResolutionFailed.Should().BeTrue(
			because: "the environment answered: it has no current package, so the caller must ask the user for one");
		resolution.Error.Should().Contain("CurrentPackageId",
			because: "the message must name the setting the user or their IDE sets");
	}

	[Test, Category("Unit")]
	[Description("Refuses definitively when CurrentPackageId points at a package the environment no longer has.")]
	public void Resolve_Should_Refuse_When_CurrentPackageId_Points_At_Nothing() {
		// Arrange
		IPackageTargetResolver sut = CreateResolver(new ResolverEnvironment {
			CurrentPackageIdValue = Guid.NewGuid().ToString()
		});

		// Act
		PackageTargetResolution resolution = sut.Resolve(null);

		// Assert
		resolution.Success.Should().BeFalse(because: "a dangling current-package setting names no usable target");
		resolution.ResolutionFailed.Should().BeTrue(because: "the environment answered with no matching package");
	}

	[Test, Category("Unit")]
	[Description("Refuses the current package too when it is locked, so the no-name path is guarded exactly like a named one.")]
	public void Resolve_Should_Refuse_When_The_CurrentPackageId_Package_Is_Locked() {
		// Arrange
		IPackageTargetResolver sut = CreateResolver(new ResolverEnvironment { InstallType = LockedInstallType });

		// Act
		PackageTargetResolution resolution = sut.Resolve(null);

		// Assert
		resolution.Success.Should().BeFalse(
			because: "an omitted package must not lower the bar the named path is held to");
		resolution.ResolutionFailed.Should().BeTrue(because: "the environment answered and the package is closed");
		resolution.Error.Should().Contain("unlock-package", because: "the message names how the user fixes it");
	}

	[Test, Category("Unit")]
	[Description("Reports a failed read as non-definitive, so a caller never tells the user a package does not exist because the environment could not be reached.")]
	public void Resolve_Should_Not_Claim_A_Definitive_Answer_When_The_Environment_Cannot_Be_Read() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		applicationClient
			.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Throws(new InvalidOperationException("The remote server returned an error: (503)"));
		IPackageTargetResolver sut = new PackageTargetResolver(
			applicationClient, CreateServiceUrlBuilder(), CreateSysSettingsManager(new ResolverEnvironment()));

		// Act
		PackageTargetResolution resolution = sut.Resolve(PackageName);

		// Assert
		resolution.Success.Should().BeFalse(because: "no target could be produced");
		resolution.ResolutionFailed.Should().BeFalse(
			because: "the environment was never asked, so a retry may still succeed and reporting 'no such package' would be a lie");
		resolution.Error.Should().Contain("503",
			because: "the operator needs the transport detail to tell a blip from a misconfiguration");
	}

	[Test, Category("Unit")]
	[Description("Treats an InstallType the environment did not report as not-locked, so an oddly answered column cannot block every delivery.")]
	public void Resolve_Should_Accept_A_Package_Whose_InstallType_Is_Not_Reported() {
		// Arrange
		IPackageTargetResolver sut = CreateResolver(new ResolverEnvironment { ReportInstallType = false });

		// Act
		PackageTargetResolution resolution = sut.Resolve(PackageName);

		// Assert
		resolution.Success.Should().BeTrue(
			because: "the lock check is a cheap early guard, not the authority — the write itself still refuses a closed package");
	}

	[Test, Category("Unit")]
	[Description("Refuses a package row the environment answered without a usable UId, because package data cannot be addressed to it.")]
	public void Resolve_Should_Refuse_A_Package_Row_Without_A_Usable_UId() {
		// Arrange
		IPackageTargetResolver sut = CreateResolver(new ResolverEnvironment { PackageUIdValue = string.Empty });

		// Act
		PackageTargetResolution resolution = sut.Resolve(PackageName);

		// Assert
		resolution.Success.Should().BeFalse(because: "a binding is addressed by package UId, and there is none");
		resolution.ResolutionFailed.Should().BeTrue(because: "the environment answered with an unusable row");
	}

	private static IPackageTargetResolver CreateResolver(ResolverEnvironment environment) {
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		applicationClient
			.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(callInfo => environment.Answer(callInfo.ArgAt<string>(1)));
		return new PackageTargetResolver(
			applicationClient, CreateServiceUrlBuilder(), CreateSysSettingsManager(environment));
	}

	private static IServiceUrlBuilder CreateServiceUrlBuilder() {
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select).Returns(SelectUrl);
		return serviceUrlBuilder;
	}

	private static ISysSettingsManager CreateSysSettingsManager(ResolverEnvironment environment) {
		ISysSettingsManager sysSettingsManager = Substitute.For<ISysSettingsManager>();
		sysSettingsManager.GetSysSettingValueByCode("CurrentPackageId")
			.Returns(_ => environment.CurrentPackageIdValue);
		return sysSettingsManager;
	}

	private sealed class ResolverEnvironment {

		public string CurrentPackageIdValue { get; set; } = CurrentPackageRowId.ToString();

		public int InstallType { get; set; }

		public bool ReportInstallType { get; set; } = true;

		public string PackageUIdValue { get; set; } = PackageUId.ToString();

		public string Answer(string body) {
			using JsonDocument document = JsonDocument.Parse(body);
			bool filteredByOtherId = TryReadIdFilter(document.RootElement, out string id)
				&& !string.Equals(id, CurrentPackageRowId.ToString(), StringComparison.OrdinalIgnoreCase);
			return filteredByOtherId ? """{"success":true,"rows":[]}""" : PackageRow();
		}

		private string PackageRow() {
			string installType = ReportInstallType ? $""","InstallType":{InstallType}""" : string.Empty;
			return $$"""
				{"success":true,"rows":[{"Name":"{{PackageName}}","UId":"{{PackageUIdValue}}"{{installType}}}]}
				""";
		}

		private static bool TryReadIdFilter(JsonElement root, out string id) {
			id = null;
			if (!root.TryGetProperty("filters", out JsonElement filters)
				|| !filters.TryGetProperty("items", out JsonElement items)) {
				return false;
			}
			foreach (JsonProperty filter in items.EnumerateObject()) {
				if (!filter.Value.TryGetProperty("leftExpression", out JsonElement left)
					|| !left.TryGetProperty("columnPath", out JsonElement columnPath)
					|| columnPath.GetString() != "Id") {
					continue;
				}
				id = filter.Value.GetProperty("rightExpression").GetProperty("parameter")
					.GetProperty("value").GetString();
				return true;
			}
			return false;
		}
	}
}
