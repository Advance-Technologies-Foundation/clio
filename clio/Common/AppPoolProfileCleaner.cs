using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using Microsoft.Win32;

namespace Clio.Common;

/// <summary>Outcome of a best-effort IIS application-pool profile cleanup.</summary>
public enum AppPoolProfileCleanupStatus {
	/// <summary>No registered IIS virtual-account profile exists on this platform.</summary>
	NotApplicable,
	/// <summary>The registered profile and its files were deleted.</summary>
	Deleted,
	/// <summary>The profile could not be deleted after bounded retries.</summary>
	Warning
}

/// <summary>Data returned by application-pool profile cleanup.</summary>
/// <param name="Status">Cleanup outcome.</param>
/// <param name="ProfilePath">Resolved registered profile path, when available.</param>
/// <param name="Detail">Safe technical detail suitable for logs and MCP progress.</param>
/// <param name="ErrorCode">Stable machine-readable warning code.</param>
public sealed record AppPoolProfileCleanupResult(
	AppPoolProfileCleanupStatus Status,
	string ProfilePath = null,
	string Detail = null,
	string ErrorCode = null);

/// <summary>Immutable profile cleanup target resolved before destructive IIS removal.</summary>
/// <param name="Registration">Validated Windows profile registration, when present.</param>
/// <param name="PreparedResult">A terminal not-applicable or warning result produced during resolution.</param>
public sealed record AppPoolProfileCleanupTarget(
	WindowsProfileRegistration Registration = null,
	AppPoolProfileCleanupResult PreparedResult = null);

/// <summary>Deletes a registered IIS application-pool virtual-account profile on a best-effort basis.</summary>
public interface IAppPoolProfileCleaner {
	/// <summary>Resolves and validates the profile while the IIS virtual account still exists.</summary>
	/// <param name="appPoolName">Actual IIS application-pool name captured before IIS deletion.</param>
	AppPoolProfileCleanupTarget Prepare(string appPoolName);

	/// <summary>Attempts to delete a previously prepared profile registration.</summary>
	/// <param name="target">Immutable target captured before IIS deletion.</param>
	/// <returns>A non-throwing cleanup result.</returns>
	AppPoolProfileCleanupResult TryDelete(AppPoolProfileCleanupTarget target);
}

/// <summary>Native Windows profile registration resolved from the IIS virtual-account identity.</summary>
/// <param name="Sid">Validated IIS virtual-account SID.</param>
/// <param name="ProfilePath">ProfileList registration path.</param>
public sealed record WindowsProfileRegistration(string Sid, string ProfilePath);

/// <summary>Provides the Windows identity, ProfileList, and user-profile API boundary.</summary>
public interface IWindowsUserProfileApi {
	/// <summary>Resolves a registered IIS virtual-account profile, or returns <see langword="null"/> when absent.</summary>
	WindowsProfileRegistration Resolve(string appPoolName);

	/// <summary>Deletes a profile using the Windows user-profile API.</summary>
	/// <returns>Zero on success; otherwise the native Win32 error code.</returns>
	int Delete(WindowsProfileRegistration registration);
}

/// <summary>Provides the bounded pause between native profile-deletion attempts.</summary>
public interface IProfileDeletionRetryDelay {
	/// <summary>Waits before the next retry.</summary>
	void Wait();
}

/// <summary>Default short delay between profile-deletion attempts.</summary>
public sealed class ProfileDeletionRetryDelay : IProfileDeletionRetryDelay {
	private static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(250);

	/// <inheritdoc />
	public void Wait() => Thread.Sleep(Delay);
}

/// <summary>Windows implementation that resolves only registered IIS virtual-account profiles.</summary>
public sealed class WindowsUserProfileApi : IWindowsUserProfileApi {
	private const int ErrorAccessDenied = 5;
	private const int ErrorDirectoryNotEmpty = 145;
	private const int ErrorGenFailure = 31;
	private const string IisVirtualAccountSidPrefix = "S-1-5-82-";
	private const string ProfileListKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList";

	/// <inheritdoc />
	public WindowsProfileRegistration Resolve(string appPoolName) {
		if (string.IsNullOrWhiteSpace(appPoolName)) {
			return null;
		}

		NTAccount account = new("IIS APPPOOL", appPoolName);
		SecurityIdentifier sid;
		try {
			sid = (SecurityIdentifier)account.Translate(typeof(SecurityIdentifier));
		}
		catch (IdentityNotMappedException) {
			return null;
		}
		catch (SystemException exception) {
			throw new InvalidOperationException("Windows could not resolve the IIS application-pool identity.", exception);
		}

		string sidValue = sid.Value;
		if (!IsIisVirtualAccountSid(sidValue)) {
			throw new InvalidOperationException("Resolved identity is not an IIS application-pool virtual account.");
		}

		using RegistryKey localMachine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
		using RegistryKey profileListKey = localMachine.OpenSubKey(ProfileListKey);
		using RegistryKey profileKey = profileListKey?.OpenSubKey(sidValue);
		string registeredPath = profileKey?.GetValue("ProfileImagePath") as string;
		if (string.IsNullOrWhiteSpace(registeredPath)) {
			return null;
		}
		string profilesDirectory = profileListKey?.GetValue("ProfilesDirectory") as string;
		string expandedPath = Environment.ExpandEnvironmentVariables(registeredPath);
		if (!IsRegisteredProfilePath(expandedPath, profilesDirectory)) {
			throw new InvalidOperationException("Registered IIS application-pool profile path is outside the Windows profiles directory.");
		}
		if (IsReparsePoint(expandedPath)) {
			throw new InvalidOperationException("Registered IIS application-pool profile path is a reparse point.");
		}
		return new WindowsProfileRegistration(sidValue, expandedPath);
	}

	internal static bool IsIisVirtualAccountSid(string sid) =>
		sid?.StartsWith(IisVirtualAccountSidPrefix, StringComparison.Ordinal) == true;

	internal static bool IsRegisteredProfilePath(string registeredPath, string profilesDirectory) {
		if (string.IsNullOrWhiteSpace(registeredPath) || string.IsNullOrWhiteSpace(profilesDirectory)) {
			return false;
		}
		string profilePath = Path.GetFullPath(registeredPath);
		string profilesRoot = Path.GetFullPath(Environment.ExpandEnvironmentVariables(profilesDirectory));
		string relativePath = Path.GetRelativePath(profilesRoot, profilePath);
		return !Path.IsPathRooted(relativePath)
			&& relativePath.Length > 0
			&& relativePath != "."
			&& !relativePath.Equals("..", StringComparison.Ordinal)
			&& !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
	}

	/// <inheritdoc />
	public int Delete(WindowsProfileRegistration registration) {
		ArgumentNullException.ThrowIfNull(registration);
		string currentRegisteredPath = ReadRegisteredProfilePath(registration.Sid);
		if (string.IsNullOrWhiteSpace(currentRegisteredPath)) {
			return ProfilePathExists(registration.ProfilePath) ? ErrorAccessDenied : 0;
		}
		if (!IsIisVirtualAccountSid(registration.Sid)
			|| !Path.GetFullPath(currentRegisteredPath).Equals(Path.GetFullPath(registration.ProfilePath),
				StringComparison.OrdinalIgnoreCase)
			|| !IsRegisteredProfilePath(registration.ProfilePath, ReadProfilesDirectory())
			|| IsReparsePoint(registration.ProfilePath)) {
			return ErrorAccessDenied;
		}
		if (!DeleteProfile(registration.Sid, registration.ProfilePath, null)) {
			int errorCode = Marshal.GetLastWin32Error();
			return errorCode == 0 ? ErrorGenFailure : errorCode;
		}
		return ProfilePathExists(registration.ProfilePath) ? ErrorDirectoryNotEmpty : 0;
	}

	private static bool ProfilePathExists(string profilePath) {
		try {
			File.GetAttributes(profilePath);
			return true;
		}
		catch (FileNotFoundException) {
			return false;
		}
		catch (DirectoryNotFoundException) {
			return false;
		}
	}

	private static string ReadProfilesDirectory() {
		using RegistryKey localMachine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
		using RegistryKey profileListKey = localMachine.OpenSubKey(ProfileListKey);
		return profileListKey?.GetValue("ProfilesDirectory") as string;
	}

	private static string ReadRegisteredProfilePath(string sid) {
		using RegistryKey localMachine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
		using RegistryKey profileKey = localMachine.OpenSubKey($@"{ProfileListKey}\{sid}");
		string registeredPath = profileKey?.GetValue("ProfileImagePath") as string;
		return string.IsNullOrWhiteSpace(registeredPath)
			? null
			: Environment.ExpandEnvironmentVariables(registeredPath);
	}

	private static bool IsReparsePoint(string profilePath) {
		try {
			return (File.GetAttributes(profilePath) & FileAttributes.ReparsePoint) != 0;
		}
		catch (FileNotFoundException) {
			return false;
		}
		catch (DirectoryNotFoundException) {
			return false;
		}
	}

	[DllImport("userenv.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool DeleteProfile(string sidString, string profilePath, string computerName);
}

/// <summary>Windows best-effort cleaner with a three-attempt bounded retry policy.</summary>
public sealed class WindowsAppPoolProfileCleaner(
	IWindowsUserProfileApi profileApi,
	IProfileDeletionRetryDelay retryDelay) : IAppPoolProfileCleaner {
	/// <summary>Stable typed-event error code for exhausted profile cleanup.</summary>
	public const string ProfileDeleteFailedErrorCode = "APPPOOL_PROFILE_DELETE_FAILED";

	private const int MaximumAttempts = 3;

	/// <inheritdoc />
	public AppPoolProfileCleanupTarget Prepare(string appPoolName) {
		WindowsProfileRegistration registration;
		try {
			registration = profileApi.Resolve(appPoolName);
		}
		catch (UnauthorizedAccessException) {
			return PreparedWarning("Windows denied access while resolving the registered application-pool profile.");
		}
		catch (InvalidOperationException exception) {
			return PreparedWarning(exception.Message);
		}
		catch (System.Security.SecurityException) {
			return PreparedWarning("Windows security policy blocked application-pool profile resolution.");
		}
		catch (IOException exception) {
			return PreparedWarning($"Windows could not read the registered application-pool profile: {exception.Message}");
		}
		catch (ArgumentException exception) {
			return PreparedWarning($"Windows could not resolve the application-pool identity: {exception.Message}");
		}
		catch (PlatformNotSupportedException exception) {
			return PreparedWarning($"Windows profile cleanup is unavailable: {exception.Message}");
		}
		catch (SystemException exception) {
			return PreparedWarning($"Windows could not resolve the application-pool profile: {exception.Message}");
		}

		if (registration is null) {
			return new AppPoolProfileCleanupTarget(PreparedResult:
				new AppPoolProfileCleanupResult(AppPoolProfileCleanupStatus.NotApplicable));
		}
		return new AppPoolProfileCleanupTarget(registration);
	}

	/// <inheritdoc />
	public AppPoolProfileCleanupResult TryDelete(AppPoolProfileCleanupTarget target) {
		if (target?.PreparedResult is not null) {
			return target.PreparedResult;
		}
		WindowsProfileRegistration registration = target?.Registration;
		if (registration is null) {
			return new AppPoolProfileCleanupResult(AppPoolProfileCleanupStatus.NotApplicable);
		}

		int errorCode = 0;
		for (int attempt = 1; attempt <= MaximumAttempts; attempt++) {
			try {
				errorCode = profileApi.Delete(registration);
			}
			catch (UnauthorizedAccessException) {
				return Warning(registration.ProfilePath,
					"Windows denied access while deleting the registered application-pool profile.");
			}
			catch (System.Security.SecurityException) {
				return Warning(registration.ProfilePath,
					"Windows security policy blocked application-pool profile deletion.");
			}
			catch (IOException exception) {
				return Warning(registration.ProfilePath,
					$"Windows could not delete the registered application-pool profile: {exception.Message}");
			}
			catch (InvalidOperationException exception) {
				return Warning(registration.ProfilePath, exception.Message);
			}
			catch (DllNotFoundException exception) {
				return Warning(registration.ProfilePath,
					$"Windows profile cleanup is unavailable: {exception.Message}");
			}
			catch (EntryPointNotFoundException exception) {
				return Warning(registration.ProfilePath,
					$"Windows profile cleanup is unavailable: {exception.Message}");
			}
			catch (SystemException exception) {
				return Warning(registration.ProfilePath,
					$"Windows could not delete the registered application-pool profile: {exception.Message}");
			}
			if (errorCode == 0) {
				return new AppPoolProfileCleanupResult(AppPoolProfileCleanupStatus.Deleted,
					registration.ProfilePath);
			}
			if (attempt < MaximumAttempts) {
				try {
					retryDelay.Wait();
				}
				catch (ThreadInterruptedException) {
					return Warning(registration.ProfilePath,
						"Application-pool profile deletion retry was interrupted.");
				}
			}
		}

		string detail = new Win32Exception(errorCode).Message;
		return Warning(registration.ProfilePath,
			$"Windows could not delete the registered profile after {MaximumAttempts} attempts: {detail}");
	}

	private static AppPoolProfileCleanupResult Warning(string profilePath, string detail) =>
		new(AppPoolProfileCleanupStatus.Warning, profilePath, detail, ProfileDeleteFailedErrorCode);

	private static AppPoolProfileCleanupTarget PreparedWarning(string detail) =>
		new(PreparedResult: Warning(null, detail));
}

/// <summary>Non-Windows cleaner that reports profile cleanup as not applicable.</summary>
public sealed class NonWindowsAppPoolProfileCleaner : IAppPoolProfileCleaner {
	/// <inheritdoc />
	public AppPoolProfileCleanupTarget Prepare(string appPoolName) =>
		new(PreparedResult: new AppPoolProfileCleanupResult(AppPoolProfileCleanupStatus.NotApplicable));

	/// <inheritdoc />
	public AppPoolProfileCleanupResult TryDelete(AppPoolProfileCleanupTarget target) =>
		target?.PreparedResult ?? new AppPoolProfileCleanupResult(AppPoolProfileCleanupStatus.NotApplicable);
}
