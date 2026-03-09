using System.Runtime.InteropServices;

namespace Clio.Common.IIS;

/// <summary>
/// Detects the current operating system for IIS-specific logic.
/// </summary>
public interface IPlatformDetector
{
	/// <summary>
	/// Gets a value indicating whether the current host is Windows.
	/// </summary>
	bool IsWindows();
}

/// <summary>
/// Default platform detector backed by <see cref="RuntimeInformation"/>.
/// </summary>
public sealed class PlatformDetector : IPlatformDetector
{
	/// <inheritdoc />
	public bool IsWindows()
	{
		return RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
	}
}
