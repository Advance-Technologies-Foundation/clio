using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using ATF.Repository;
using ATF.Repository.Providers;
using CreatioModel;
using DocumentFormat.OpenXml.Office2010.Excel;
using Newtonsoft.Json.Linq;
using NewtonsoftJson = Newtonsoft.Json;
using Terrasoft.Core;
using static CreatioModel.SysSettings;
using IAbstractionsFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Common;

/// <summary>
/// Creatio's file-upload security mode (mirrors the platform's FileSecurityMode lookup). <see cref="Unknown"/>
/// is clio-only: the configured value was missing, malformed, or an unrecognized id, so the mode could not be
/// resolved and Binary uploads must fail closed rather than be treated as Disabled.
/// </summary>
public enum FileSecurityMode { Disabled, AllowList, DenyList, Unknown }

/// <summary>
/// The environment's active file-upload security policy, read from sys-settings. Advisory on the
/// sys-setting write path (the platform enforces it only on the multipart file-upload service), but clio
/// applies it client-side so a Binary upload from a file mirrors what the environment would allow.
/// </summary>
public sealed record FileSecurityPolicy(FileSecurityMode Mode, IReadOnlySet<string> Extensions, bool AllowUnknownType)
{
	public static FileSecurityPolicy DisabledPolicy { get; } =
		new(FileSecurityMode.Disabled, new HashSet<string>(), true);

	/// <summary>Policy for an unresolvable mode: enforcement is on and every Binary upload is refused (fail closed).</summary>
	public static FileSecurityPolicy UnknownPolicy { get; } =
		new(FileSecurityMode.Unknown, new HashSet<string>(), false);

	public bool IsActive => Mode != FileSecurityMode.Disabled;
}

public interface ISysSettingsManager
{

	#region Methods: Public

	/// <summary>
	///     Retrieves the value of a system setting by its code.
	/// </summary>
	/// <param name="code">The unique code identifier of the system setting.</param>
	/// <returns>A string representation of the system setting's value.</returns>
	/// <remarks>
	///     Uses GetSysSettingValueByCode endpoint implemented in clio-gate
	/// </remarks>
	string GetSysSettingValueByCode(string code);

	/// <summary>
	///     Retrieves the value of a system setting by its code and converts it to the specified type.
	/// </summary>
	/// <typeparam name="T">The type to which the system setting value should be converted.</typeparam>
	/// <param name="code">The unique code identifier of the system setting.</param>
	/// <returns>The system setting's value, converted to the specified type.</returns>
	/// <exception cref="ArgumentOutOfRangeException">Thrown when the type T is not supported.</exception>
	/// <exception cref="InvalidCastException">Thrown when the value cannot be converted to the specified type.</exception>
	/// <remarks>
	///     This method uses the GetSysSettingValueByCode endpoint implemented in clio-gate.
	///     The type T can be one of the following: string, int, decimal, bool, DateTime.
	///     If the type T is not one of these, an ArgumentOutOfRangeException will be thrown.
	///     If the value cannot be converted to the specified type, an InvalidCastException will be thrown.
	/// </remarks>
	T GetSysSettingValueByCode<T>(string code);

	/// <summary>
	/// Returns the All-Users default value of a sys-setting (never a personal/current-user override),
	/// matching the contract advertised by the MCP get-sys-setting tool.
	/// </summary>
	string GetAllUsersDefaultByCode(string code);

	/// <summary>
	/// Returns the All-Users default value and the resolved value-type-name of a sys-setting in a single
	/// model lookup. Callers that need to apply type-aware policy (for example masking SecureText values
	/// before surfacing them to MCP clients) should prefer this method over <see cref="GetAllUsersDefaultByCode"/>
	/// to avoid a second round-trip. Returns an empty value and a null type name when the setting is unknown.
	/// </summary>
	(string Value, string ValueTypeName) GetAllUsersDefaultWithType(string code);

	SysSettingsManager.InsertSysSettingResponse InsertSysSetting(string name, string code, string valueTypeName,
		bool cached = true, string description = "", bool valueForCurrentUser = false,
		Guid? referenceSchemaUId = null);

	Guid? FindSchemaUIdByName(string schemaName);

	/// <summary>
	/// Resolves a sys-setting's definition metadata by code: its <c>ValueTypeName</c> and, for Lookup
	/// settings, the referenced entity schema name. Returns <c>null</c> when no setting with the given
	/// code exists on the environment. Used by the business-rule engine to type a system-setting
	/// condition operand.
	/// </summary>
	/// <param name="code">The unique code identifier of the system setting.</param>
	(string ValueTypeName, string? ReferenceSchemaName)? GetSysSettingTypeByCode(string code);

	bool UpdateSysSetting(string code, object value, string valueTypeName = "Text");

	/// <summary>
	/// Validates a Binary sys-setting value (well-formed Base64 within the size cap). Returns false with a
	/// specific <paramref name="error"/> (malformed vs too-large) so callers can surface the real cause
	/// instead of a generic update failure.
	/// </summary>
	bool TryValidateBinaryValue(string value, out string error);

	#endregion

	void CreateSysSettingIfNotExists(string optsCode, string code, string optsType);
	
	/// <summary>
	/// Returns all sys-settings with their values attached. Binary-type settings are excluded by default
	/// (the manifest/download path cannot serialize a blob value); pass <paramref name="includeBinary"/>
	/// = true for the discovery surface (list-sys-settings), where the metadata is useful even though the
	/// blob value itself is not readable.
	/// </summary>
	public List<SysSettings> GetAllSysSettingsWithValues(bool includeBinary = false);

	/// <summary>
	/// Reads the environment's file-upload security policy (FileSecurityMode + the active extension list +
	/// AllowFilesWithUnknownType) from sys-settings. Returns <see cref="FileSecurityPolicy.DisabledPolicy"/>
	/// when the mode is Disabled or cannot be resolved.
	/// </summary>
	FileSecurityPolicy GetFileSecurityPolicy();

}

public class SysSettingsManager : ISysSettingsManager
{

	#region Fields: Private

	private readonly IApplicationClient _creatioClient;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;
	private readonly IDataProvider _dataProvider;
	private readonly IWorkingDirectoriesProvider _workingDirectoriesProvider;
	private readonly IFileSystem _filesystem;
	private readonly IAbstractionsFileSystem _abstractionsFileSystem;
	private readonly ILogger _logger;

	private readonly JsonSerializerOptions _jsonSerializerOptions = new() {
		WriteIndented = false,
		AllowTrailingCommas = false,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	private sealed record UpdateSysSettingResponse(
		[property: JsonPropertyName("saveResult")] Dictionary<string, bool> SaveResult,
		[property: JsonPropertyName("rowsAffected")] int RowsAffected,
		[property: JsonPropertyName("success")] bool Success,
		[property: JsonPropertyName("responseStatus")] ResponseStatus ResponseStatus);

	#endregion

	#region Constructors: Public

	public SysSettingsManager(IApplicationClient creatioClient,
		IServiceUrlBuilder serviceUrlBuilder, IDataProvider dataProvider,
		IWorkingDirectoriesProvider workingDirectoriesProvider, IFileSystem filesystem,
		IAbstractionsFileSystem abstractionsFileSystem, ILogger logger){
		_creatioClient = creatioClient;
		_serviceUrlBuilder = serviceUrlBuilder;
		_dataProvider = dataProvider;
		_workingDirectoriesProvider = workingDirectoriesProvider;
		_filesystem = filesystem;
		_abstractionsFileSystem = abstractionsFileSystem;
		_logger = logger;
	}

	public SysSettingsManager(IDataProvider providerMock) {
		_dataProvider = providerMock;
	}

	#endregion

	#region Methods: Private

	private static object ConvertToBool(string value){
		bool isBool = bool.TryParse(value, out bool boolValue);
		return isBool ? (object)boolValue
			: throw new InvalidCastException($"Could not convert {value} to {nameof(Boolean)}");
	}

	private static object ConvertToDateTime(string value){
		bool isDateTime = DateTime.TryParse(value, CultureInfo.InvariantCulture, out DateTime dtValue);
		return isDateTime ? (object)dtValue
			: throw new InvalidCastException($"Could not convert {value} to {nameof(Boolean)}");
	}
	private static object ConvertToDate(string value){
		bool isDateTime = DateTime.TryParse(value, CultureInfo.InvariantCulture, out DateTime dateValue);
		return isDateTime ? (object)dateValue.Date
			: throw new InvalidCastException($"Could not convert {value} to {nameof(Boolean)}");
	}

	private static object ConvertToDecimal(string value){
		bool isDecimal = decimal.TryParse(value, CultureInfo.InvariantCulture, out decimal decValue);
		return isDecimal ? (object)decValue
			: throw new InvalidCastException($"Could not convert {value} to {nameof(Decimal)}");
	}

	private static object ConvertToGuid(string value){
		bool isGuid = Guid.TryParse(value, CultureInfo.InvariantCulture, out Guid decValue);
		return isGuid ? (object)decValue
			: throw new InvalidCastException($"Could not convert {value} to {nameof(Guid)}");
	}

	private static object ConvertToInt(string value){
		const NumberStyles style = NumberStyles.Integer | NumberStyles.AllowThousands;
		CultureInfo provider = new("en-US"); //Should probably get culture from creatio
		bool isInt = int.TryParse(value, style, provider, out int intValue);
		return isInt ? (object)intValue
			: throw new InvalidCastException($"Could not convert {value} to to {nameof(Int32)}");
	}

	/// <summary>
	/// Upper bound on a Binary sys-setting payload (decoded bytes). Applies to every write path — a file
	/// read for value-file-path and an inline Base64 value alike — so no route can bypass it. A logo or
	/// background comfortably fits; the cap guards against a mistaken large file exhausting memory.
	/// </summary>
	public const long MaxBinaryValueBytes = 10L * 1024 * 1024;

	internal enum Base64ValidationResult { Valid, Malformed, TooLarge }

	/// <summary>
	/// Validates a Binary sys-setting payload without allocating from unbounded attacker-controlled input.
	/// First rejects on the encoded length (an upper bound on the decoded size) so an oversized value never
	/// gets a full buffer allocated for it; only then allocates to verify Base64 well-formedness and the
	/// exact decoded size. Distinguishes <see cref="Base64ValidationResult.Malformed"/> from
	/// <see cref="Base64ValidationResult.TooLarge"/> so callers can report the real cause.
	/// </summary>
	private static Base64ValidationResult ValidateBinaryBase64(string value, out int decodedByteLength){
		decodedByteLength = 0;
		if (string.IsNullOrWhiteSpace(value)) {
			return Base64ValidationResult.Malformed;
		}
		// Base64 encodes 3 bytes per 4 chars, so decoded <= length/4*3. Reject on this upper bound BEFORE
		// allocating, so a huge inline value cannot force a large allocation just to be rejected afterward.
		long maxPossibleDecoded = (long)value.Length / 4 * 3;
		if (maxPossibleDecoded > MaxBinaryValueBytes) {
			return Base64ValidationResult.TooLarge;
		}
		byte[] buffer = new byte[value.Length];
		if (!Convert.TryFromBase64String(value, buffer, out decodedByteLength)) {
			decodedByteLength = 0;
			return Base64ValidationResult.Malformed;
		}
		return decodedByteLength > MaxBinaryValueBytes
			? Base64ValidationResult.TooLarge
			: Base64ValidationResult.Valid;
	}

	private Guid GetEntityIdByDisplayValue(string entityName, string optsValue){
		string jsonFilePath = _abstractionsFileSystem.Path.Join(
			_workingDirectoriesProvider.TemplateDirectory, "dataservice-requests", "selectIdByDisplayValue.json");

		// Parse the template as a JObject and assign caller-supplied values via property access
		// so Newtonsoft handles JSON escaping. Raw string-replacement of {{diplayvalue}} previously
		// let agent-supplied display names break out of the JSON string literal.
		string jsonContent = _filesystem.ReadAllText(jsonFilePath);
		JObject requestBody = JObject.Parse(jsonContent);
		requestBody["rootSchemaName"] = entityName;
		const string parameterPath = "$.filters.items['8caf69f4-9583-4e77-86c0-716c07ce4ec7'].rightExpression.parameter";
		JToken parameter = requestBody.SelectToken(parameterPath);
		if (parameter is not JObject parameterObj) {
			throw new InvalidOperationException(
				$"selectIdByDisplayValue.json template is malformed: expected JObject at '{parameterPath}'. " +
				"The template structure changed and the caller-supplied display value cannot be assigned safely.");
		}
		parameterObj["value"] = optsValue;

		string selectQueryUrl = _serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery");
		string responseJson = _creatioClient.ExecutePostRequest(selectQueryUrl, requestBody.ToString(NewtonsoftJson.Formatting.None));
		JObject json = JObject.Parse(responseJson);
		JArray rows = json["rows"] as JArray;
		if (rows is null || rows.Count == 0) {
			return Guid.Empty;
		}
		if (rows.Count > 1) {
			throw new InvalidOperationException(
				$"Ambiguous lookup display value '{optsValue}' for entity '{entityName}': {rows.Count} rows match. " +
				"Pass the record GUID instead of the display name to disambiguate.");
		}
		string id = (string)rows[0]["Id"];
		bool isGuid = Guid.TryParse(id, out Guid value);
		return isGuid ? value : Guid.Empty;
	}

	private string GetSysSchemaNameByUid(Guid uid){
		SysSchema sysSchema = AppDataContextFactory.GetAppDataContext(_dataProvider)
			.Models<SysSchema>()
			.Where(i => i.UId == uid)
			.ToList().FirstOrDefault();
		return sysSchema?.Name;
	}
	

	private SysSettings GetSysSettingByCode(string code){

		SysSettings sysSetting = AppDataContextFactory.GetAppDataContext(_dataProvider)
													.Models<SysSettings>()
													.Where(i => i.Code == code)
													.ToList().FirstOrDefault();
		return sysSetting;
	}

	private static readonly Guid AllUsersAdminUnitId = new("a29a3ba5-4b0d-de11-9a51-005056c00008");

	private const string LookupTypeName = "Lookup";

	private static readonly TimeSpan SysSettingCodeRegexTimeout = TimeSpan.FromSeconds(1);

	/// <summary>
	/// Permitted characters in a sys-setting code: must start with a letter and contain
	/// only ASCII letters, digits, or underscore. Mirrors Creatio platform constraints and
	/// blocks malformed codes from reaching the DataService endpoint. The pattern is linear
	/// (no backtracking); the explicit 1-second timeout matches the convention used by other
	/// helpers in this assembly and protects against engine-side regressions and pathological inputs.
	/// </summary>
	private static readonly System.Text.RegularExpressions.Regex SysSettingCodeRegex
		= new("^[A-Za-z][A-Za-z0-9_]*$",
			System.Text.RegularExpressions.RegexOptions.Compiled,
			SysSettingCodeRegexTimeout);

	private SysSettings GetSysSettingByCodeWithValues(string code){
		SysSettings sysSetting = GetSysSettingByCode(code);
		if (sysSetting is null) {
			return null;
		}
		List<SysSettingsValue> values = AppDataContextFactory.GetAppDataContext(_dataProvider)
			.Models<SysSettingsValue>()
			.Where(v => v.SysSettingsId == sysSetting.Id)
			.ToList();
		sysSetting.SysSettingsValues = values;
		return sysSetting;
	}

	private static string FormatTypedValue(SysSettings sysSetting, SysSettingsValue value){
		return sysSetting.ValueTypeName switch {
			"Boolean" => value.BooleanValue.ToString().ToLowerInvariant(),
			"Integer" => value.IntegerValue.ToString(CultureInfo.InvariantCulture),
			"Float" or "Money" or "Decimal" or "Currency"
				=> value.FloatValue.ToString(CultureInfo.InvariantCulture),
			"Date" => value.DateTimeValue.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
			"Time" => value.DateTimeValue.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
			"DateTime" => value.DateTimeValue.ToString("o", CultureInfo.InvariantCulture),
			LookupTypeName => value.GuidValue.ToString(),
			_ => value.TextValue ?? string.Empty
		};
	}

	#endregion

	#region Methods: Public

	public string GetSysSettingValueByCode(string code){
		string providerValue = _dataProvider.GetSysSettingValue<string>(code);
		if (!string.IsNullOrEmpty(providerValue)) {
			return providerValue;
		}
		SysSettings sysSetting = GetSysSettingByCodeWithValues(code);
		if (sysSetting?.SysSettingsValues is null || sysSetting.SysSettingsValues.Count == 0) {
			return providerValue ?? string.Empty;
		}
		SysSettingsValue value = sysSetting.SysSettingsValues
			.FirstOrDefault(v => v.SysAdminUnitId == AllUsersAdminUnitId);
		return value is null ? string.Empty : FormatTypedValue(sysSetting, value);
	}

	public T GetSysSettingValueByCode<T>(string code){
		string val = GetSysSettingValueByCode(code);
		return typeof(T) switch {
			_ when typeof(T) == typeof(string) => (T)(object)val,
			_ when typeof(T) == typeof(int) => (T)ConvertToInt(val),
			_ when typeof(T) == typeof(decimal) => (T)ConvertToDecimal(val),
			_ when typeof(T) == typeof(bool) => (T)ConvertToBool(val),
			_ when typeof(T) == typeof(DateTime) => (T)ConvertToDateTime(val),
			_ when typeof(T) == typeof(Guid) => (T)ConvertToGuid(val),
			_ => throw new ArgumentOutOfRangeException()
		};
	}

	/// <summary>
	/// Returns the All-Users default value of a sys-setting (never a personal/current-user override).
	/// This is the contract the MCP get-sys-setting tool advertises; legacy
	/// <see cref="GetSysSettingValueByCode(string)"/> short-circuits through the data provider which
	/// can resolve a per-user value via the cliogate endpoint and would contradict that contract.
	/// </summary>
	public string GetAllUsersDefaultByCode(string code) => GetAllUsersDefaultWithType(code).Value;

	/// <inheritdoc cref="ISysSettingsManager.GetAllUsersDefaultWithType" />
	public (string Value, string ValueTypeName) GetAllUsersDefaultWithType(string code) {
		SysSettings sysSetting = GetSysSettingByCodeWithValues(code);
		if (sysSetting is null) {
			return (string.Empty, null);
		}
		if (sysSetting.SysSettingsValues is null || sysSetting.SysSettingsValues.Count == 0) {
			return (string.Empty, sysSetting.ValueTypeName);
		}
		SysSettingsValue value = sysSetting.SysSettingsValues
			.FirstOrDefault(v => v.SysAdminUnitId == AllUsersAdminUnitId);
		return value is null
			? (string.Empty, sysSetting.ValueTypeName)
			: (FormatTypedValue(sysSetting, value), sysSetting.ValueTypeName);
	}

	public InsertSysSettingResponse InsertSysSetting(string name, string code, string valueTypeName,
		bool cached = true, string description = "", bool valueForCurrentUser = false,
		Guid? referenceSchemaUId = null){

		CreatioSysSetting sysSetting = valueTypeName switch {
			"Text" => new TextSetting(name, code, null, cached, description, valueForCurrentUser),
			"ShortText" => new ShortText(name, code, null, cached, description, valueForCurrentUser),
			"MediumText" => new MediumText(name, code, null, cached, description, valueForCurrentUser),
			"LongText" => new LongText(name, code, null, cached, description, valueForCurrentUser),
			"SecureText" => new SecureText(name, code, null, cached, description, valueForCurrentUser),
			"MaxSizeText" => new MaxSizeText(name, code, null, cached, description, valueForCurrentUser),
			"Boolean" => new CBoolean(name, code, null, cached, description, valueForCurrentUser),
			"DateTime" => new CDateTime(name, code, null, cached, description, valueForCurrentUser),
			"Date" => new CDate(name, code, null, cached, description, valueForCurrentUser),
			"Time" => new CTime(name, code, null, cached, description, valueForCurrentUser),
			"Integer" => new CInteger(name, code, null, cached, description, valueForCurrentUser),
			"Money" or "Currency" => new CCurrency(name, code, null, cached, description, valueForCurrentUser),
			"Float" or "Decimal" => new CDecimal(name, code, null, cached, description, valueForCurrentUser),
			"Binary" => new CBinary(name, code, null, cached, description, valueForCurrentUser),
			LookupTypeName => new Lookup(name, code, null, cached, description, valueForCurrentUser),
			var _ => throw new ArgumentOutOfRangeException(nameof(valueTypeName), valueTypeName,
				"Unsupported SysSettingType, Allowed values (Text, ShortText, MediumText, LongText, SecureText, " +
				"MaxSizeText, Boolean, DateTime, Date, Time, Integer, Money, Float, Binary, Lookup). " +
				"Aliases: Currency = Money, Decimal = Float.")
		};
		if (referenceSchemaUId.HasValue && referenceSchemaUId.Value != Guid.Empty) {
			sysSetting.ReferenceSchemaUId = referenceSchemaUId.Value;
		}
		string json = sysSetting.ToString();
		const string endpoint = "DataService/json/SyncReply/InsertSysSettingRequest";
		string url = _serviceUrlBuilder.Build(endpoint);
		string response = _creatioClient.ExecutePostRequest(url, json);
		return JsonSerializer.Deserialize<InsertSysSettingResponse>(response, _jsonSerializerOptions);
	}

	public bool UpdateSysSetting(string code, object value, string valueTypeName = "Text"){
		if (string.IsNullOrWhiteSpace(code) || !SysSettingCodeRegex.IsMatch(code)) {
			_logger.WriteError(
				$"SysSettings code '{code}' is not a valid Creatio identifier (must start with a letter and contain only letters, digits, or underscore).");
			return false;
		}
		SysSettings sysSetting = GetSysSettingByCode(code);
		string optionsType = sysSetting is not null
			? sysSetting.ValueTypeName : valueTypeName;
		object payloadValue;
		if (optionsType.Contains("Text") || optionsType.Contains("Date") || optionsType.Contains("Time") || optionsType.Contains("Lookup")) {
			string stringValue = value?.ToString() ?? string.Empty;
			if (optionsType == LookupTypeName) {
				bool isGuid = Guid.TryParse(stringValue, out Guid id);
				if (!isGuid) {
					Guid referenceSchemaUIduid = sysSetting.ReferenceSchemaUIdId;
					string entityName = GetSysSchemaNameByUid(referenceSchemaUIduid);
					if (string.IsNullOrEmpty(entityName)) {
						// GetSysSchemaNameByUid returns null when the reference schema cannot be resolved.
						// Fail loudly instead of resolving the display value against a null root schema,
						// which would silently write Guid.Empty and clear the lookup.
						_logger.WriteError(
							$"SysSettings with code: {code} is not updated. Reference schema for the lookup could not be resolved.");
						return false;
					}
					Guid entityId = GetEntityIdByDisplayValue(entityName, stringValue);
					stringValue = entityId.ToString();
				}
				else {
					stringValue = id.ToString();
				}
			}
			if (new[] { "Date", "DateTime", "Time" }.Contains(optionsType)) {
				bool isDate = DateTime.TryParse(stringValue, CultureInfo.InvariantCulture, out DateTime dtValue);
				if (isDate) {
					stringValue = dtValue.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture);
				}
				else {
					_logger.WriteError($"SysSettings with code: {code} is not updated. Invalid date format.");
					return false;
				}
			}
			payloadValue = stringValue;
		}
		else if (optionsType.Contains("Boolean")) {
			bool isBool = bool.TryParse(value?.ToString(), out bool boolValue);
			if (!isBool) {
				_logger.WriteError($"SysSettings with code: {code} is not updated. Invalid boolean format.");
				return false;
			}
			payloadValue = boolValue;
		}
		else if (optionsType.Contains("Currency") || optionsType.Contains("Decimal")
			|| optionsType.Contains("Money") || optionsType.Contains("Float")) {
			bool isDecimal = decimal.TryParse(value?.ToString(), NumberStyles.Number,
				CultureInfo.InvariantCulture, out decimal decimalValue);
			if (!isDecimal) {
				_logger.WriteError($"SysSettings with code: {code} is not updated. Invalid decimal format.");
				return false;
			}
			payloadValue = decimalValue;
		}
		else if (optionsType.Contains("Integer")) {
			bool isInt = int.TryParse(value?.ToString(), NumberStyles.Integer,
				CultureInfo.InvariantCulture, out int intValue);
			if (!isInt) {
				_logger.WriteError($"SysSettings with code: {code} is not updated. Invalid integer format.");
				return false;
			}
			payloadValue = intValue;
		}
		else if (optionsType.Contains("Binary")) {
			// Binary sys-settings (e.g. LogoImage) are written by sending the payload as a Base64
			// string inside the same sysSettingsValues map used by every other type; the platform's
			// PostSysSettingsValues endpoint accepts it. Callers pass an already-Base64-encoded value
			// (the command layer encodes a file for CLI/MCP callers). Reading the value back is not
			// supported here — see SysSettingsCommand for the write-only rationale.
			// Validate + size-cap here so the limit holds for every write path (inline Base64 as well as a
			// file read upstream), enforced against the decoded byte count without allocating from an
			// unbounded input first.
			string base64Value = value?.ToString() ?? string.Empty;
			switch (ValidateBinaryBase64(base64Value, out int _)) {
				case Base64ValidationResult.Malformed:
					_logger.WriteError(
						$"SysSettings with code: {code} is not updated. Value is not valid Base64 " +
						"(Binary settings expect a Base64-encoded payload).");
					return false;
				case Base64ValidationResult.TooLarge:
					_logger.WriteError(
						$"SysSettings with code: {code} is not updated. Binary value exceeds the " +
						$"{MaxBinaryValueBytes:N0}-byte limit.");
					return false;
			}
			payloadValue = base64Value;
		}
		else {
			_logger.WriteError(
				$"SysSettings with code: {code} is not updated. Unsupported value-type-name '{optionsType}'.");
			return false;
		}
		string requestData = JsonSerializer.Serialize(new Dictionary<string, object> {
			["isPersonal"] = false,
			["sysSettingsValues"] = new Dictionary<string, object> { [code] = payloadValue }
		}, _jsonSerializerOptions);
		string postSysSettingsValuesUrl
			= _serviceUrlBuilder.Build("DataService/json/SyncReply/PostSysSettingsValues");
		try {

			string result = _creatioClient.ExecutePostRequest(postSysSettingsValuesUrl, requestData);
			if (string.IsNullOrWhiteSpace(result)) {
				_logger.WriteError($"SysSettings with code: {code} is not updated. Empty response received.");
				return false;
			}
			UpdateSysSettingResponse response =
				JsonSerializer.Deserialize<UpdateSysSettingResponse>(result, _jsonSerializerOptions);
			if (response?.SaveResult is not null
				&& response.SaveResult.TryGetValue(code, out bool perCodeOk)
				&& perCodeOk) {
				return true;
			}
			string errMsg = response?.ResponseStatus?.Message;
			_logger.WriteError(
				$"SysSettings with code: {code} is not updated. " +
				(string.IsNullOrWhiteSpace(errMsg) ? "Platform reported a failed update." : errMsg));
			return false;
		} catch (JsonException) {
			_logger.WriteError($"SysSettings with code: {code} is not updated. Invalid response format.");
			return false;
		}
	}
	
	public void CreateSysSettingIfNotExists(string optsCode, string code, string optsType){
		SysSettings sysSetting = GetSysSettingByCode(code); 
		if(sysSetting is null) {
			InsertSysSettingResponse result = InsertSysSetting(optsCode, code, optsType);
			string text = result switch {
				{Success: true, Id: var id} when id != Guid.Empty => $"SysSettings with code: {code} created.",
				{Success: false, Id: var id} when id == Guid.Empty => $"SysSettings with code: {code} already exists.",
				{Success: false}  => $"SysSettings with code: {code} already exists.",
				{Success: true}  => $"SysSettings with code: {code} created.",
			};
			_logger.WriteInfo(text);
		}
	}

	public Guid? FindSchemaUIdByName(string schemaName) {
		if (string.IsNullOrWhiteSpace(schemaName)) {
			return null;
		}
		SysSchema sysSchema = AppDataContextFactory.GetAppDataContext(_dataProvider)
			.Models<SysSchema>()
			.Where(s => s.Name == schemaName)
			.AsEnumerable().FirstOrDefault();
		return sysSchema?.UId;
	}

	/// <inheritdoc cref="ISysSettingsManager.GetSysSettingTypeByCode" />
	public (string ValueTypeName, string? ReferenceSchemaName)? GetSysSettingTypeByCode(string code) {
		if (string.IsNullOrWhiteSpace(code)) {
			return null;
		}
		SysSettings sysSetting = GetSysSettingByCode(code);
		if (sysSetting is null) {
			return null;
		}
		string referenceSchemaName = sysSetting.ReferenceSchemaUIdId != Guid.Empty
			? GetSysSchemaNameByUid(sysSetting.ReferenceSchemaUIdId)
			: null;
		return (sysSetting.ValueTypeName, referenceSchemaName);
	}

	public List<SysSettings> GetAllSysSettingsWithValues(bool includeBinary = false) {
		var models = AppDataContextFactory.GetAppDataContext(_dataProvider).Models<SysSettings>();
		var sysSettings = includeBinary
			? models.ToList()
			: models.Where(s => s.ValueTypeName != "Binary").ToList();

		var sysSettingsValues = AppDataContextFactory.GetAppDataContext(_dataProvider)
			.Models<SysSettingsValue>()
			.ToList();
		foreach(var sysSetting in sysSettings) {
			var currentSysSettingValue = sysSettingsValues
				.Where(i => i.SysSettingsId == sysSetting.Id)
				.ToList();
			sysSetting.SysSettingsValues = currentSysSettingValue;
		}
		return sysSettings;
	}

	// Platform-fixed FileSecurityMode lookup ids (constants in Terrasoft.Web.FileSecurity.FileSecurityModeProvider).
	private static readonly Guid FileSecurityModeDisabledId = new("9801C625-FAFB-4ED3-9383-C3C942A5C1E3");
	private static readonly Guid FileSecurityModeDenyListId = new("60849C6E-24B4-45DF-9AAD-2F69D419823C");
	private static readonly Guid FileSecurityModeAllowListId = new("C6CA9A2F-3A4A-4D51-B67B-DE36852CB916");

	/// <inheritdoc cref="ISysSettingsManager.TryValidateBinaryValue" />
	public bool TryValidateBinaryValue(string value, out string error) {
		switch (ValidateBinaryBase64(value, out int _)) {
			case Base64ValidationResult.Malformed:
				error = "Value is not valid Base64 (Binary settings expect a Base64-encoded payload).";
				return false;
			case Base64ValidationResult.TooLarge:
				error = $"Binary value exceeds the {MaxBinaryValueBytes:N0}-byte limit.";
				return false;
			default:
				error = null;
				return true;
		}
	}

	/// <inheritdoc cref="ISysSettingsManager.GetFileSecurityPolicy" />
	public FileSecurityPolicy GetFileSecurityPolicy() {
		// Read the authoritative All-Users value and resolve the mode fail-closed: only the explicit
		// Disabled id counts as Disabled. A missing, malformed, or unrecognized id resolves to Unknown so
		// callers refuse the Binary upload rather than silently skipping the only barrier on this path.
		string modeRaw = GetAllUsersDefaultByCode("FileSecurityMode");
		FileSecurityMode mode;
		if (!Guid.TryParse(modeRaw, out Guid modeId)) {
			mode = FileSecurityMode.Unknown;
		} else if (modeId == FileSecurityModeDisabledId) {
			mode = FileSecurityMode.Disabled;
		} else if (modeId == FileSecurityModeAllowListId) {
			mode = FileSecurityMode.AllowList;
		} else if (modeId == FileSecurityModeDenyListId) {
			mode = FileSecurityMode.DenyList;
		} else {
			mode = FileSecurityMode.Unknown;
		}
		if (mode == FileSecurityMode.Disabled) {
			return FileSecurityPolicy.DisabledPolicy;
		}
		if (mode == FileSecurityMode.Unknown) {
			return FileSecurityPolicy.UnknownPolicy;
		}
		string listCode = mode == FileSecurityMode.AllowList ? "FileExtensionsAllowList" : "FileExtensionsDenyList";
		string listRaw = GetAllUsersDefaultByCode(listCode) ?? string.Empty;
		HashSet<string> extensions = new(StringComparer.OrdinalIgnoreCase);
		foreach (string entry in listRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
			extensions.Add(entry.TrimStart('.'));
		}
		// AllowFilesWithUnknownType: an unset value uses the platform default (true); a present-but-malformed
		// value is treated conservatively (false / do not allow unknown-type files).
		string allowUnknownRaw = GetAllUsersDefaultByCode("AllowFilesWithUnknownType");
		bool allowUnknown = string.IsNullOrWhiteSpace(allowUnknownRaw)
			|| (bool.TryParse(allowUnknownRaw, out bool parsed) && parsed);
		return new FileSecurityPolicy(mode, extensions, allowUnknown);
	}

	#endregion

	internal record GetSettingRequestData(string Code);

	internal record InsertSysSettingRequest(Guid Id, string Name, string Code, string ValueTypeName, bool IsCacheable);

	public record InsertSysSettingResponse([property: JsonPropertyName("responseStatus")]
		ResponseStatus ResponseStatus,
		[property: JsonPropertyName("id")] Guid Id,
		[property: JsonPropertyName("rowsAffected")]
		int RowsAffected,
		[property: JsonPropertyName("nextPrcElReady")]
		bool NextPrcElReady,
		[property: JsonPropertyName("success")]
		bool Success);

	public record ResponseStatus([property: JsonPropertyName("ErrorCode")]
		string ErrorCode,
		[property: JsonPropertyName("Message")]
		string Message,
		[property: JsonPropertyName("Errors")] object[] Errors);

}

public sealed class TextSetting : CreatioSysSetting
{

	#region Constructors: Public

	public TextSetting(string name, string code, string value, bool isCacheable, string description, bool isPersonal)
		: base(name, code, value, isCacheable, description, isPersonal){ }

	public TextSetting(string name, string code, object value, bool isCacheable, string description, bool isPersonal)
		: base(name, code, value, isCacheable, description, isPersonal){ }

	#endregion

	#region Properties: Public

	public override string ValueTypeName => "Text";

	#endregion

}

public sealed class ShortText : CreatioSysSetting
{

	#region Constructors: Public

	public ShortText(string name, string code, string value, bool isCacheable, string description, bool isPersonal)
		: base(name, code, value, isCacheable, description, isPersonal){ }

	public ShortText(string name, string code, object value, bool isCacheable, string description, bool isPersonal)
		: base(name, code, value, isCacheable, description, isPersonal){ }

	#endregion

	#region Properties: Public

	public override string ValueTypeName => "ShortText";

	#endregion

}

public sealed class MediumText : CreatioSysSetting
{

	#region Constructors: Public

	public MediumText(string name, string code, string value, bool isCacheable, string description, bool isPersonal)
		: base(name, code, value, isCacheable, description, isPersonal){ }

	public MediumText(string name, string code, object value, bool isCacheable, string description, bool isPersonal)
		: base(name, code, value, isCacheable, description, isPersonal){ }

	#endregion

	#region Properties: Public

	public override string ValueTypeName => "MediumText";

	#endregion

}

public sealed class LongText : CreatioSysSetting
{

	#region Constructors: Public

	public LongText(string name, string code, string value, bool isCacheable, string description, bool isPersonal)
		: base(name, code, value, isCacheable, description, isPersonal){ }

	public LongText(string name, string code, object value, bool isCacheable, string description, bool isPersonal)
		: base(name, code, value, isCacheable, description, isPersonal){ }

	#endregion

	#region Properties: Public

	public override string ValueTypeName => "LongText";

	#endregion

}

public sealed class SecureText : CreatioSysSetting
{

	#region Constructors: Public

	public SecureText(string name, string code, string value, bool isCacheable, string description, bool isPersonal)
		: base(name, code, value, isCacheable, description, isPersonal){ }

	public SecureText(string name, string code, object value, bool isCacheable, string description, bool isPersonal)
		: base(name, code, value, isCacheable, description, isPersonal){ }

	#endregion

	#region Properties: Public

	public override string ValueTypeName => "SecureText";

	#endregion

}

public sealed class MaxSizeText : CreatioSysSetting
{

	#region Constructors: Public

	public MaxSizeText(string name, string code, string value, bool isCacheable, string description, bool isPersonal)
		: base(name, code, value, isCacheable, description, isPersonal){ }

	public MaxSizeText(string name, string code, object value, bool isCacheable, string description, bool isPersonal)
		: base(name, code, value, isCacheable, description, isPersonal){ }

	#endregion

	#region Properties: Public

	public override string ValueTypeName => "MaxSizeText";

	#endregion

}

public sealed class CBoolean : CreatioSysSetting
{

	#region Constructors: Public

	public CBoolean(string name, string code, string value, bool isCacheable, string description, bool isPersonal)
		: base(name, code, value, isCacheable, description, isPersonal){ }

	public CBoolean(string name, string code, object value, bool isCacheable, string description, bool isPersonal)
		: base(name, code, value, isCacheable, description, isPersonal){ }

	#endregion

	#region Properties: Public

	public override string ValueTypeName => "Boolean";

	#endregion

}

public sealed class CDateTime : CreatioSysSetting
{

	#region Constructors: Public

	public CDateTime(string name, string code, string value, bool isCacheable, string description, bool isPersonal)
		: base(name, code, value, isCacheable, description, isPersonal){ }

	public CDateTime(string name, string code, object value, bool isCacheable, string description, bool isPersonal)
		: base(name, code, value, isCacheable, description, isPersonal){ }

	#endregion

	#region Properties: Public

	public override string ValueTypeName => "DateTime";

	#endregion

}

public sealed class CTime : CreatioSysSetting
{

	#region Constructors: Public

	public CTime(string name, string code, string value, bool isCacheable, string description, bool isPersonal)
		: base(name, code, value, isCacheable, description, isPersonal){ }

	public CTime(string name, string code, object value, bool isCacheable, string description, bool isPersonal)
		: base(name, code, value, isCacheable, description, isPersonal){ }

	#endregion

	#region Properties: Public

	public override string ValueTypeName => "Time";

	#endregion

}

public sealed class CDate : CreatioSysSetting
{

	#region Constructors: Public

	public CDate(string name, string code, string value, bool isCacheable, string description, bool isPersonal)
		: base(name, code, value, isCacheable, description, isPersonal){ }

	public CDate(string name, string code, object value, bool isCacheable, string description, bool isPersonal)
		: base(name, code, value, isCacheable, description, isPersonal){ }

	#endregion

	#region Properties: Public

	public override string ValueTypeName => "Date";

	#endregion

}

public sealed class CInteger : CreatioSysSetting
{

	#region Constructors: Public

	public CInteger(string name, string code, string value, bool isCacheable, string description, bool isPersonal)
		: base(name, code, value, isCacheable, description, isPersonal){ }

	public CInteger(string name, string code, object value, bool isCacheable, string description, bool isPersonal)
		: base(name, code, value, isCacheable, description, isPersonal){ }

	#endregion

	#region Properties: Public

	public override string ValueTypeName => "Integer";

	#endregion

}

public sealed class CCurrency : CreatioSysSetting
{

	#region Constructors: Public

	public CCurrency(string name, string code, string value, bool isCacheable, string description, bool isPersonal)
		: base(name, code, value, isCacheable, description, isPersonal){ }

	public CCurrency(string name, string code, object value, bool isCacheable, string description, bool isPersonal)
		: base(name, code, value, isCacheable, description, isPersonal){ }

	#endregion

	#region Properties: Public

	public override string ValueTypeName => "Money";

	#endregion

}

public sealed class CDecimal : CreatioSysSetting
{

	#region Constructors: Public

	public CDecimal(string name, string code, string value, bool isCacheable, string description, bool isPersonal)
		: base(name, code, value, isCacheable, description, isPersonal){ }

	public CDecimal(string name, string code, object value, bool isCacheable, string description, bool isPersonal)
		: base(name, code, value, isCacheable, description, isPersonal){ }

	#endregion

	#region Properties: Public

	public override string ValueTypeName => "Float";

	#endregion

}

public sealed class Lookup : CreatioSysSetting
{

	#region Constructors: Public

	public Lookup(string name, string code, string value, bool isCacheable, string description, bool isPersonal)
		: base(name, code, value, isCacheable, description, isPersonal){ }

	public Lookup(string name, string code, object value, bool isCacheable, string description, bool isPersonal)
		: base(name, code, value, isCacheable, description, isPersonal){ }

	#endregion

	#region Properties: Public

	public override string ValueTypeName => "Lookup";

	#endregion

}

public sealed class CBinary : CreatioSysSetting
{

	#region Constructors: Public

	public CBinary(string name, string code, string value, bool isCacheable, string description, bool isPersonal)
		: base(name, code, value, isCacheable, description, isPersonal){ }

	public CBinary(string name, string code, object value, bool isCacheable, string description, bool isPersonal)
		: base(name, code, value, isCacheable, description, isPersonal){ }

	#endregion

	#region Properties: Public

	public override string ValueTypeName => "Binary";

	#endregion

}

public abstract class CreatioSysSetting
{

	#region Fields: Private

	private static readonly JsonSerializerOptions JsonSerializerOptions = new() {
		WriteIndented = false,
		AllowTrailingCommas = false,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	#endregion

	#region Properties: Public

	[JsonPropertyName("valueTypeName")]
	public abstract string ValueTypeName { get; }

	[JsonPropertyName("code")]
	public string Code { get; set; }

	[JsonPropertyName("description")]
	public string Description { get; set; }

	[JsonPropertyName("isCacheable")]
	public bool IsCacheable { get; set; }

	[JsonPropertyName("isPersonal")]
	public bool IsPersonal { get; set; }

	[JsonPropertyName("name")]
	public string Name { get; set; }

	[JsonPropertyName("value")]
	public string Value { get; set; }

	[JsonPropertyName("referenceSchemaUId")]
	public Guid? ReferenceSchemaUId { get; set; }

	#endregion

	#region Methods: Public

	public override string ToString() => JsonSerializer.Serialize(this, JsonSerializerOptions);

	#endregion

	private protected CreatioSysSetting(string name, string code, string value, bool isCacheable, string description,
		bool isPersonal){
		Name = name;
		Code = code;
		Value = value;
		IsCacheable = isCacheable;
		Description = description;
		IsPersonal = isPersonal;
	}

	private protected CreatioSysSetting(string name, string code, object value, bool isCacheable, string description,
		bool isPersonal){
		Name = name;
		Code = code;
		Value = value.ToString();
		IsCacheable = isCacheable;
		Description = description;
		IsPersonal = isPersonal;
	}

}
