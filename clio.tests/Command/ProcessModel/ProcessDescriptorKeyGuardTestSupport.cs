using Clio.Command.ProcessModel;
using Clio.Common;
using Clio.Project.NuGet;
using NSubstitute;

namespace Clio.Tests.Command.ProcessModel;

/// <summary>
/// A REAL <see cref="ProcessDescriptorKeyGuard"/> in its strict mode - the environment reports exactly the bundled
/// CrtProcessBuilder version - for service tests that post descriptors or operations.
/// </summary>
/// <remarks>
/// Deliberately not a substitute. Every descriptor and operations array the create, modify and
/// as-new-version service tests send then passes through the real key check, so a fixture carrying a key the
/// server would drop in silence fails its own test instead of quietly asserting on a request the server never
/// honours in full (ENG-95244, test plan TC-U-14).
/// </remarks>
internal static class ProcessDescriptorKeyGuardTestSupport {

	/// <summary>The version both sides report, so an unknown key is refused rather than warned about.</summary>
	internal const string SameVersion = "5.0.0.0";

	internal static IProcessDescriptorKeyGuard Strict() {
		IRequiredPackageChecker checker = Substitute.For<IRequiredPackageChecker>();
		checker.GetInstalledVersion(BundledPackages.ProcessBuilderPackageName)
			.Returns(PackageVersion.ParseVersion(SameVersion));
		IBundledPackageCatalog catalog = Substitute.For<IBundledPackageCatalog>();
		catalog.TryGetVersion(BundledPackages.ProcessBuilderPackageName, out Arg.Any<PackageVersion>(), out Arg.Any<string>())
			.Returns(call => {
				call[1] = PackageVersion.ParseVersion(SameVersion);
				call[2] = null;
				return true;
			});
		return new ProcessDescriptorKeyGuard(new ProcessDescriptorKeyValidator(), checker, catalog);
	}
}
