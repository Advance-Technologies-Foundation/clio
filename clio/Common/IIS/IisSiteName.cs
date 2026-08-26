using System;
using System.IO;
using System.Linq;

namespace Clio.Common.IIS;

internal static class IisSiteName {
	private static readonly string[] ReservedDeviceNames = [
		"CON", "PRN", "AUX", "NUL",
		"COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
		"LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
	];

	internal static bool IsSafeLeaf(string name) => !string.IsNullOrWhiteSpace(name)
		&& string.Equals(name, name.Trim(), StringComparison.Ordinal)
		&& name is not "." and not ".."
		&& !name.EndsWith('.')
		&& !Path.IsPathFullyQualified(name)
		&& name.IndexOfAny(['/', '\\', '"']) < 0
		&& name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
		&& !name.Any(char.IsControl)
		&& !ReservedDeviceNames.Contains(Path.GetFileNameWithoutExtension(name),
			StringComparer.OrdinalIgnoreCase);
}
