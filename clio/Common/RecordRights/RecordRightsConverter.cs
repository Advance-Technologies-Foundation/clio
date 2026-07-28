using System;

namespace Clio.Common.RecordRights;

internal static class RecordRightsConverter {

	internal const int OperationRead = 0;

	internal const int OperationEdit = 1;

	internal const int OperationDelete = 2;

	internal const int LevelGranted = 1;

	internal const int LevelDelegated = 2;

	internal static int ParseOperation(string value) => (value?.Trim().ToLowerInvariant()) switch {
		"read" => OperationRead,
		"edit" => OperationEdit,
		"delete" => OperationDelete,
		_ => throw new ArgumentException(
			$"Unknown operation '{value}'. Expected one of: read, edit, delete.", nameof(value))
	};

	internal static string OperationToName(int wireValue) => wireValue switch {
		OperationRead => "read",
		OperationEdit => "edit",
		OperationDelete => "delete",
		_ => wireValue.ToString()
	};

	internal static int ParseLevel(string value) => (value?.Trim().ToLowerInvariant()) switch {
		"granted" => LevelGranted,
		"delegated" => LevelDelegated,
		_ => throw new ArgumentException(
			$"Unknown level '{value}'. Expected one of: granted, delegated.", nameof(value))
	};

	internal static string LevelToName(int wireValue) => wireValue switch {
		LevelGranted => "granted",
		LevelDelegated => "delegated",
		_ => wireValue.ToString()
	};
}
