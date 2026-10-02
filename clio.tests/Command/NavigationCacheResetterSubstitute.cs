using Clio.Command;
using Clio.Common;
using NSubstitute;

namespace Clio.Tests.Command;

/// <summary>
/// Builds <see cref="INavigationCacheResetter"/> substitutes. An unstubbed substitute returns an empty string,
/// which the services read as a failed reset, so fixtures that do not test the reset use a succeeding one.
/// </summary>
internal static class NavigationCacheResetterSubstitute {
	/// <summary>The browser-session note every substitute returns.</summary>
	public const string BrowserSessionNote = "refresh the open tab with GetData(true)";

	/// <summary>Returns a resetter whose every reset succeeds and whose note is <see cref="BrowserSessionNote"/>.</summary>
	public static INavigationCacheResetter Succeeding() {
		INavigationCacheResetter resetter = Substitute.For<INavigationCacheResetter>();
		resetter.TryReset(Arg.Any<IApplicationClient>(), Arg.Any<EnvironmentSettings>()).Returns((string?)null);
		resetter.BuildBrowserSessionNote(Arg.Any<EnvironmentSettings>()).Returns(BrowserSessionNote);
		return resetter;
	}
}
