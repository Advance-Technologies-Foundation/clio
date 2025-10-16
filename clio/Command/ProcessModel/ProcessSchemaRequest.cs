using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;

namespace Clio.Command.ProcessModel;

/// <summary>
/// Dto for request to get process schema 
/// </summary>
/// <see cref="ServiceUrlBuilder.KnownRoute.ProcessSchemaRequest"/>
/// <param name="Uid">Process UId</param>
/// <param name="PackageUId">Package UId, usually null</param>
/// <param name="ConvertLocalizableStringToParameter">Usually <c>true</c></param>
internal record ProcessSchemaRequest(
	[property:JsonPropertyName("uId")]Guid Uid, 
	[property:JsonPropertyName("packageUId")]string? PackageUId = null, 
	[property:JsonPropertyName("convertLocalizableStringToParameter")]bool ConvertLocalizableStringToParameter = true){
	
	private static readonly JsonSerializerOptions JsonSerializerOptions = new () {
		WriteIndented = true, 
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};
	
	/// <summary>
	/// Converts to JSON string
	/// </summary>
	/// <returns>Object as JSON string</returns>
	public override string ToString() {
		return JsonSerializer.Serialize(this, JsonSerializerOptions);
	}
}
