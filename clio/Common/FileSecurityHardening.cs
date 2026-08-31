using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Clio.Common;

/// <summary>
/// Default <see cref="IFileSecurityHardening"/>.
/// <para>
/// <b>Unix (macOS/Linux):</b> sets the file to <c>0600</c> and the directory to <c>0700</c> via
/// <see cref="File.SetUnixFileMode(string, UnixFileMode)"/> — owner read/write only.
/// </para>
/// <para>
/// <b>Windows:</b> the session cache lives under <c>%LOCALAPPDATA%</c>
/// (<see cref="SettingsRepository.AppSettingsFolderPath"/>) and the Clio
/// host-environment store is protected with an explicit ACL that grants full control only to the
/// current user, LocalSystem, and built-in administrators. Inheritance is disabled so a broader
/// parent ACL cannot make bearer credentials readable by another user.
/// </para>
/// </summary>
public sealed class FileSecurityHardening : IFileSecurityHardening {
	private const UnixFileMode OwnerOnlyFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;
	private const UnixFileMode OwnerOnlyDirectory =
		UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

	/// <summary>Initializes the hardening helper.</summary>
	public FileSecurityHardening() { }

	/// <inheritdoc />
	public void HardenFile(string filePath) {
		if (string.IsNullOrEmpty(filePath)) {
			return;
		}
		if (OperatingSystem.IsWindows()) {
			new FileInfo(filePath).SetAccessControl(CreateProtectedFileSecurity());
			return;
		}
		File.SetUnixFileMode(filePath, OwnerOnlyFile);
	}

	/// <inheritdoc />
	public void HardenDirectory(string directoryPath) {
		if (string.IsNullOrEmpty(directoryPath)) {
			return;
		}
		if (OperatingSystem.IsWindows()) {
			new DirectoryInfo(directoryPath).SetAccessControl(CreateProtectedDirectorySecurity());
			return;
		}
		File.SetUnixFileMode(directoryPath, OwnerOnlyDirectory);
	}

	private static FileSecurity CreateProtectedFileSecurity() {
		SecurityIdentifier owner = WindowsIdentity.GetCurrent().User
			?? throw new UnauthorizedAccessException("The current Windows user SID is unavailable.");
		FileSecurity security = new();
		security.SetOwner(owner);
		security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
		AddFullControlRule(security, owner);
		AddFullControlRule(security, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null));
		AddFullControlRule(security, new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
		return security;
	}

	private static DirectorySecurity CreateProtectedDirectorySecurity() {
		SecurityIdentifier owner = WindowsIdentity.GetCurrent().User
			?? throw new UnauthorizedAccessException("The current Windows user SID is unavailable.");
		DirectorySecurity security = new();
		security.SetOwner(owner);
		security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
		AddFullControlRule(security, owner);
		AddFullControlRule(security, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null));
		AddFullControlRule(security, new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
		return security;
	}

	private static void AddFullControlRule(FileSecurity security, SecurityIdentifier identity) {
		security.AddAccessRule(new FileSystemAccessRule(identity, FileSystemRights.FullControl,
			AccessControlType.Allow));
	}

	private static void AddFullControlRule(DirectorySecurity security, SecurityIdentifier identity) {
		const InheritanceFlags inheritance = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
		security.AddAccessRule(new FileSystemAccessRule(identity, FileSystemRights.FullControl, inheritance,
			PropagationFlags.None, AccessControlType.Allow));
	}
}
