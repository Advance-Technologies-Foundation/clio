using System;
using Clio.Common;
using Clio.Tests.Command;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public sealed class AppPoolProfileCleanerTests : BaseClioModuleTests {
	private IWindowsUserProfileApi _profileApi;
	private IProfileDeletionRetryDelay _retryDelay;
	private IAppPoolProfileCleaner _sut;

	protected override void AdditionalRegistrations(IServiceCollection services) {
		base.AdditionalRegistrations(services);
		services.AddSingleton(_profileApi);
		services.AddSingleton(_retryDelay);
		services.AddTransient<IAppPoolProfileCleaner, WindowsAppPoolProfileCleaner>();
		services.AddTransient<NonWindowsAppPoolProfileCleaner>();
	}

	public override void Setup() {
		_profileApi = Substitute.For<IWindowsUserProfileApi>();
		_retryDelay = Substitute.For<IProfileDeletionRetryDelay>();
		base.Setup();
		_sut = Container.GetRequiredService<IAppPoolProfileCleaner>();
	}

	[Test]
	[Description("Deletes the registered IIS virtual-account profile without retry when the native API succeeds.")]
	public void TryDelete_ShouldReturnDeletedWithoutRetry_WhenNativeDeletionSucceeds() {
		// Arrange
		WindowsProfileRegistration registration = new("S-1-5-82-1", @"C:\Users\custom-pool");
		_profileApi.Resolve("custom-pool").Returns(registration);
		_profileApi.Delete(registration).Returns(0);

		// Act
		AppPoolProfileCleanupResult result = _sut.TryDelete(_sut.Prepare("custom-pool"));

		// Assert
		result.Status.Should().Be(AppPoolProfileCleanupStatus.Deleted,
			because: "a zero native result means Windows removed registration and files");
		result.ProfilePath.Should().Be(registration.ProfilePath,
			because: "the caller needs the resolved registered path for safe diagnostics");
		_retryDelay.DidNotReceive().Wait();
	}

	[Test]
	[Description("Treats an absent registered profile as not applicable without calling the native deletion API.")]
	public void TryDelete_ShouldReturnNotApplicable_WhenProfileIsAbsent() {
		// Arrange
		_profileApi.Resolve("custom-pool").Returns((WindowsProfileRegistration)null);

		// Act
		AppPoolProfileCleanupResult result = _sut.TryDelete(_sut.Prepare("custom-pool"));

		// Assert
		result.Status.Should().Be(AppPoolProfileCleanupStatus.NotApplicable,
			because: "an identity without a ProfileList registration has nothing to delete");
		_profileApi.DidNotReceive().Delete(Arg.Any<WindowsProfileRegistration>());
	}

	[Test]
	[Description("Retries a locked or denied native profile deletion exactly three times and returns one safe warning result.")]
	public void TryDelete_ShouldReturnWarningAfterThreeAttempts_WhenNativeDeletionKeepsFailing() {
		// Arrange
		WindowsProfileRegistration registration = new("S-1-5-82-1", @"C:\Users\custom-pool");
		_profileApi.Resolve("custom-pool").Returns(registration);
		_profileApi.Delete(registration).Returns(5);

		// Act
		AppPoolProfileCleanupResult result = _sut.TryDelete(_sut.Prepare("custom-pool"));

		// Assert
		result.Status.Should().Be(AppPoolProfileCleanupStatus.Warning,
			because: "best-effort cleanup must not throw after retry exhaustion");
		result.ErrorCode.Should().Be(WindowsAppPoolProfileCleaner.ProfileDeleteFailedErrorCode,
			because: "MCP and Ring need a stable warning classification");
		_profileApi.Received(3).Delete(registration);
		_retryDelay.Received(2).Wait();
	}

	[Test]
	[Description("Converts access denied during profile resolution into a non-throwing warning.")]
	public void TryDelete_ShouldReturnWarning_WhenProfileResolutionIsDenied() {
		// Arrange
		const string deniedPoolName = "denied-pool";
		_profileApi.When(api => api.Resolve(deniedPoolName)).Do(_ => throw new UnauthorizedAccessException());

		// Act
		AppPoolProfileCleanupResult result = null;
		Action act = () => result = _sut.TryDelete(_sut.Prepare(deniedPoolName));

		// Assert
		act.Should().NotThrow(because: "profile resolution failure is a warning, never an uninstall exception");
		result!.Status.Should().Be(AppPoolProfileCleanupStatus.Warning,
			because: "access denied must remain visible without failing uninstall");
	}

	[Test]
	[Description("Converts a Windows identity translation failure during profile resolution into a non-throwing warning.")]
	public void TryDelete_ShouldReturnWarning_WhenProfileIdentityTranslationFails() {
		// Arrange
		const string poolName = "translation-failure-pool";
		_profileApi.When(api => api.Resolve(poolName)).Do(_ => throw new SystemException("Win32 translation failed"));

		// Act
		AppPoolProfileCleanupResult result = null;
		Action act = () => result = _sut.TryDelete(_sut.Prepare(poolName));

		// Assert
		act.Should().NotThrow(because: "documented Windows identity translation failures are best-effort warnings");
		result!.Status.Should().Be(AppPoolProfileCleanupStatus.Warning,
			because: "identity translation failure must remain visible without failing uninstall");
	}

	[Test]
	[Description("Converts an exception from the native deletion boundary into a non-throwing warning.")]
	public void TryDelete_ShouldReturnWarning_WhenNativeDeletionThrows() {
		// Arrange
		WindowsProfileRegistration registration = new("S-1-5-82-1", @"C:\Users\custom-pool");
		_profileApi.Resolve("custom-pool").Returns(registration);
		_profileApi.When(api => api.Delete(registration)).Do(_ => throw new UnauthorizedAccessException());

		// Act
		AppPoolProfileCleanupResult result = null;
		Action act = () => result = _sut.TryDelete(_sut.Prepare("custom-pool"));

		// Assert
		act.Should().NotThrow(because: "the native Windows boundary must never turn best-effort cleanup into uninstall failure");
		result!.Status.Should().Be(AppPoolProfileCleanupStatus.Warning,
			because: "native access failure must remain visible as a typed warning");
		result.ErrorCode.Should().Be(WindowsAppPoolProfileCleaner.ProfileDeleteFailedErrorCode,
			because: "all warning paths need the same stable MCP and Ring classification");
	}

	[Test]
	[Description("Converts a Windows system failure from the native deletion boundary into a non-throwing warning.")]
	public void TryDelete_ShouldReturnWarning_WhenNativeDeletionThrowsSystemException() {
		// Arrange
		WindowsProfileRegistration registration = new("S-1-5-82-1", @"C:\Users\custom-pool");
		_profileApi.Resolve("custom-pool").Returns(registration);
		_profileApi.When(api => api.Delete(registration)).Do(_ => throw new SystemException("Win32 deletion failed"));

		// Act
		AppPoolProfileCleanupResult result = null;
		Action act = () => result = _sut.TryDelete(_sut.Prepare("custom-pool"));

		// Assert
		act.Should().NotThrow(because: "native Windows system failures must never fail best-effort uninstall cleanup");
		result!.Status.Should().Be(AppPoolProfileCleanupStatus.Warning,
			because: "native system failure must remain visible as a typed warning");
	}

	[Test]
	[Description("Reports profile cleanup as not applicable on non-Windows platforms.")]
	public void TryDelete_ShouldReturnNotApplicable_WhenCleanerIsNonWindows() {
		// Arrange
		NonWindowsAppPoolProfileCleaner sut = Container.GetRequiredService<NonWindowsAppPoolProfileCleaner>();

		// Act
		AppPoolProfileCleanupResult result = sut.TryDelete(sut.Prepare("custom-pool"));

		// Assert
		result.Status.Should().Be(AppPoolProfileCleanupStatus.NotApplicable,
			because: "non-Windows systems do not expose Windows ProfileList or DeleteProfileW");
	}

	[TestCase("S-1-5-82-1", true)]
	[TestCase("S-1-5-21-1", false)]
	[Description("Accepts only the IIS virtual-account SID namespace as profile cleanup authority.")]
	public void IsIisVirtualAccountSid_ShouldMatchOnlyIisNamespace_WhenSidIsProvided(string sid, bool expected) {
		// Arrange

		// Act
		bool result = WindowsUserProfileApi.IsIisVirtualAccountSid(sid);

		// Assert
		result.Should().Be(expected,
			because: "profile deletion must never be authorized for an arbitrary Windows account SID");
	}

	[TestCase(@"C:\Users\custom-pool", @"C:\Users", true)]
	[TestCase(@"C:\Users", @"C:\Users", false)]
	[TestCase(@"C:\Windows\System32", @"C:\Users", false)]
	[Description("Allows registered profile deletion only for child paths beneath the configured Windows profiles directory.")]
	public void IsRegisteredProfilePath_ShouldRejectPathsOutsideProfilesRoot(string profilePath,
		string profilesDirectory, bool expected) {
		// Arrange

		// Act
		bool result = WindowsUserProfileApi.IsRegisteredProfilePath(profilePath, profilesDirectory);

		// Assert
		result.Should().Be(expected,
			because: "a compromised ProfileList entry must never authorize arbitrary-directory deletion");
	}
}
