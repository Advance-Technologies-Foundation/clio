using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clio.Command.AddonSchemaDesigner;

internal sealed class AddonGetRequestDto {
	[JsonPropertyName("addonName")]
	public string AddonName { get; set; } = string.Empty;

	[JsonPropertyName("targetSchemaUId")]
	public Guid TargetSchemaUId { get; set; }

	[JsonPropertyName("targetParentSchemaUId")]
	public Guid TargetParentSchemaUId { get; set; }

	[JsonPropertyName("targetPackageUId")]
	public Guid TargetPackageUId { get; set; }

	[JsonPropertyName("targetSchemaManagerName")]
	public string TargetSchemaManagerName { get; set; } = string.Empty;

	[JsonPropertyName("useFullHierarchy")]
	public bool UseFullHierarchy { get; set; }
}

internal sealed class AddonSchemaResponseDto {
	[JsonPropertyName("success")]
	public bool Success { get; set; }

	[JsonPropertyName("schema")]
	public AddonSchemaDto? Schema { get; set; }

	[JsonPropertyName("errorInfo")]
	public ErrorInfoDto? ErrorInfo { get; set; }
}

internal sealed class AddonSaveResponseDto {
	[JsonPropertyName("success")]
	public bool Success { get; set; }

	[JsonPropertyName("value")]
	public bool? Value { get; set; }

	[JsonPropertyName("errorInfo")]
	public ErrorInfoDto? ErrorInfo { get; set; }
}

internal sealed class AddonSchemaDto {
	[JsonPropertyName("metaData")]
	public string MetaData { get; set; } = string.Empty;

	[JsonPropertyName("resources")]
	public List<AddonResourceDto> Resources { get; set; } = [];

	[JsonExtensionData]
	public Dictionary<string, JsonElement>? AdditionalData { get; set; }
}

internal sealed class AddonResourceDto {
	[JsonPropertyName("key")]
	public string Key { get; set; } = string.Empty;

	[JsonPropertyName("value")]
	public List<AddonResourceValueDto> Value { get; set; } = [];
}

internal sealed class AddonResourceValueDto {
	[JsonPropertyName("key")]
	public string Key { get; set; } = string.Empty;

	[JsonPropertyName("value")]
	public string Value { get; set; } = string.Empty;
}

internal sealed class ErrorInfoDto {
	[JsonPropertyName("message")]
	public string? Message { get; set; }
}
