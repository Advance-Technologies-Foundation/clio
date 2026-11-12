using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clio.Command.WebServiceDesigner;

// Wire shapes of the platform's ServiceDataContract family (Terrasoft.Nui.ServiceModel.DataContract). Every
// type keeps unknown members in an extension bag: a modify re-sends what it read, and a member this file
// does not model (a SOAP namespace, a body builder) must survive the round trip unchanged.

/// <summary>The platform's <c>ObjectCollection</c>: items keyed by their UId string.</summary>
public sealed class ServiceItemCollection<T> {
	private Dictionary<string, T> _items = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// Items by UId string, compared ignoring case. System.Text.Json assigns a new dictionary on read, so the
	/// setter re-wraps it: a key written in another case must still be found by a remove or a replace.
	/// </summary>
	[JsonPropertyName("Items")]
	public Dictionary<string, T> Items {
		get => _items;
		set => _items = value is null
			? new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase)
			: new Dictionary<string, T>(value, StringComparer.OrdinalIgnoreCase);
	}
}

/// <summary>A web service schema as <c>ServiceSchemaRequest</c> returns it and <c>ServiceDataContract</c> saves it.</summary>
public sealed class ServiceSchemaDto {
	[JsonPropertyName("uId")] public Guid UId { get; set; }
	[JsonPropertyName("realUId")] public Guid RealUId { get; set; }
	[JsonPropertyName("name")] public string Name { get; set; }
	[JsonPropertyName("caption")] public Dictionary<string, string> Caption { get; set; } = new();
	[JsonPropertyName("description")] public Dictionary<string, string> Description { get; set; } = new();
	[JsonPropertyName("extendParent")] public bool ExtendParent { get; set; }
	[JsonPropertyName("packageUId")] public Guid PackageUId { get; set; }
	[JsonPropertyName("baseUri")] public string BaseUri { get; set; }
	[JsonPropertyName("type")] public int Type { get; set; }
	[JsonPropertyName("retryCount")] public int RetryCount { get; set; }
	[JsonPropertyName("namespaces")] public ServiceItemCollection<JsonElement> Namespaces { get; set; } = new();
	[JsonPropertyName("methods")] public ServiceItemCollection<ServiceMethodDto> Methods { get; set; } = new();
	[JsonPropertyName("authInfo")] public ServiceAuthInfoDto AuthInfo { get; set; }
	[JsonExtensionData] public Dictionary<string, JsonElement> Extra { get; set; }
}

public sealed class ServiceMethodDto {
	[JsonPropertyName("uId")] public Guid UId { get; set; }
	[JsonPropertyName("name")] public string Name { get; set; }
	[JsonPropertyName("requestTimeout")] public int RequestTimeout { get; set; }
	[JsonPropertyName("request")] public ServiceMethodRequestDto Request { get; set; } = new();
	[JsonPropertyName("response")] public ServiceMethodResponseDto Response { get; set; } = new();
	[JsonPropertyName("caption")] public Dictionary<string, string> Caption { get; set; } = new();
	[JsonPropertyName("useAuthInfo")] public bool UseAuthInfo { get; set; }
	[JsonExtensionData] public Dictionary<string, JsonElement> Extra { get; set; }
}

public sealed class ServiceMethodRequestDto {
	[JsonPropertyName("uri")] public string Uri { get; set; }
	[JsonPropertyName("contentType")] public int ContentType { get; set; }
	[JsonPropertyName("httpMethodType")] public int HttpMethodType { get; set; }
	[JsonPropertyName("parameters")] public ServiceItemCollection<ServiceParameterDto> Parameters { get; set; } = new();
	[JsonExtensionData] public Dictionary<string, JsonElement> Extra { get; set; }
}

public sealed class ServiceMethodResponseDto {
	[JsonPropertyName("contentType")] public int ContentType { get; set; }
	[JsonPropertyName("parameters")] public ServiceItemCollection<ServiceParameterDto> Parameters { get; set; } = new();
	[JsonExtensionData] public Dictionary<string, JsonElement> Extra { get; set; }
}

public sealed class ServiceParameterDto {
	[JsonPropertyName("uId")] public Guid UId { get; set; }
	[JsonPropertyName("type")] public int Type { get; set; }
	[JsonPropertyName("name")] public string Name { get; set; }
	[JsonPropertyName("path")] public string Path { get; set; }
	[JsonPropertyName("defValue")] public ServiceParameterValueDto DefValue { get; set; }
	[JsonPropertyName("dataValueTypeName")] public string DataValueTypeName { get; set; }
	[JsonPropertyName("isArray")] public bool IsArray { get; set; }
	[JsonPropertyName("isRequired")] public bool IsRequired { get; set; }
	[JsonPropertyName("itemProperties")] public ServiceItemCollection<ServiceParameterDto> ItemProperties { get; set; } = new();
	[JsonPropertyName("caption")] public Dictionary<string, string> Caption { get; set; } = new();
	[JsonExtensionData] public Dictionary<string, JsonElement> Extra { get; set; }
}

/// <summary>A parameter or credential value: <c>source</c> 0 none, 1 constant, 2 system setting (value = its code).</summary>
public sealed class ServiceParameterValueDto {
	[JsonPropertyName("value")] public string Value { get; set; }
	[JsonPropertyName("source")] public int Source { get; set; }
}

public sealed class ServiceAuthInfoDto {
	[JsonPropertyName("authType")] public int AuthType { get; set; }
	[JsonPropertyName("values")] public ServiceItemCollection<ServiceParameterValueDto> Values { get; set; } = new();
}

/// <summary>Platform enum values the contract carries as numbers (Terrasoft.Services.Metadata).</summary>
public static class ServiceSchemaCodes {
	public const int ServiceTypeRest = 1;
	public const int ContentTypeJson = 1;
	public const int ContentTypeXml = 2;
	public const int ValueSourceNone = 0;
	public const int ValueSourceConst = 1;
	public const int ValueSourceSysSetting = 2;
	public const int AuthNone = 0;
	public const int AuthBasic = 1;
	public const int AuthDigest = 2;
	public const int AuthOAuth20 = 3;
	public const string ManagerName = "ServiceSchemaManager";
	public const string ProcessManagerName = "ProcessSchemaManager";

	public static readonly IReadOnlyDictionary<string, int> HttpMethods =
		new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) {
			["GET"] = 1, ["POST"] = 2, ["PUT"] = 3, ["PATCH"] = 4, ["DELETE"] = 5
		};

	// Parameter placement (ServiceParameterType). A response parameter uses body, header or cookie.
	public static readonly IReadOnlyDictionary<string, int> Placements =
		new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) {
			["body"] = 1, ["path"] = 2, ["query"] = 3, ["header"] = 4, ["cookie"] = 5
		};

	/// <summary>Where a RESPONSE parameter can be read from; Studio offers these three.</summary>
	public static readonly IReadOnlySet<string> ResponsePlacements =
		new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "body", "header", "cookie" };

	// The ServiceDesigner's ServiceEnums.DataValueTypeName; the platform stores any string, Studio offers these.

	public static readonly IReadOnlyList<string> DataTypes =
		["Text", "Integer", "Float", "Boolean", "DateTime", "Date", "Time", "Guid", "Object"];

	public static readonly IReadOnlyDictionary<string, int> AuthTypes =
		new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) {
			["none"] = AuthNone, ["basic"] = AuthBasic, ["digest"] = AuthDigest, ["oauth20"] = AuthOAuth20
		};
}
