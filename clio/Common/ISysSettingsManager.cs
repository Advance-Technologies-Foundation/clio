using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using ATF.Repository;
using ATF.Repository.Providers;
using CreatioModel;
using DocumentFormat.OpenXml.Office2010.Excel;
using Newtonsoft.Json.Linq;
using Terrasoft.Core;
using static CreatioModel.SysSettings;

namespace Clio.Common;

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

	SysSettingsManager.InsertSysSettingResponse InsertSysSetting(string name, string code, string valueTypeName,
		bool cached = true, string description = "", bool valueForCurrentUser = false);

	bool UpdateSysSetting(string code, object value, string valueTypeName = "");

	#endregion

	void CreateSysSettingIfNotExists(string optsCode, string code, string optsType);
	
	public List<SysSettings> GetAllSysSettingsWithValues();

}

public class SysSettingsManager : ISysSettingsManager
{

	#region Fields: Private

	private readonly IApplicationClient _creatioClient;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;
	private readonly IDataProvider _dataProvider;
	private readonly IWorkingDirectoriesProvider _workingDirectoriesProvider;
	private readonly IFileSystem _filesystem;
	private readonly ILogger _logger;

	private readonly JsonSerializerOptions _jsonSerializerOptions = new() {
		WriteIndented = false,
		AllowTrailingCommas = false,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	private sealed record UpdateSysSettingResponse(
		[property: JsonPropertyName("success")] bool Success,
		[property: JsonPropertyName("responseStatus")] ResponseStatus ResponseStatus);

	#endregion

	#region Constructors: Public

	public SysSettingsManager(IApplicationClient creatioClient,
		IServiceUrlBuilder serviceUrlBuilder, IDataProvider dataProvider,
		IWorkingDirectoriesProvider workingDirectoriesProvider, IFileSystem filesystem, ILogger logger){
		_creatioClient = creatioClient;
		_serviceUrlBuilder = serviceUrlBuilder;
		_dataProvider = dataProvider;
		_workingDirectoriesProvider = workingDirectoriesProvider;
		_filesystem = filesystem;
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

	private Guid GetEntityIdByDisplayValue(string entityName, string optsValue){
		string jsonFilePath = Path.Join(
			_workingDirectoriesProvider.TemplateDirectory, "dataservice-requests", "selectIdByDisplayValue.json");

		string jsonContent = _filesystem.ReadAllText(jsonFilePath);
		jsonContent = jsonContent.Replace("{{rootSchemaName}}", entityName);
		jsonContent = jsonContent.Replace("{{diplayvalue}}", optsValue);

		string selectQueryUrl = _serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery");
		string responseJson = _creatioClient.ExecutePostRequest(selectQueryUrl, jsonContent);
		JObject json = JObject.Parse(responseJson);
		string jsonPath = "$.rows[0].Id";
		string id = (string)json.SelectToken(jsonPath);
		bool isGuid = Guid.TryParse(id, out Guid value);
		return isGuid ? value : Guid.Empty;
	}

	private string GetSysSchemaNameByUid(Guid uid){
		SysSchema sysSchema = AppDataContextFactory.GetAppDataContext(_dataProvider)
			.Models<SysSchema>()
			.Where(i => i.UId == uid)
			.ToList().FirstOrDefault();
		return sysSchema.Name;
	}
	

	private SysSettings GetSysSettingByCode(string code){

		SysSettings sysSetting = AppDataContextFactory.GetAppDataContext(_dataProvider)
													.Models<SysSettings>()
													.Where(i => i.Code == code)
													.ToList().FirstOrDefault();
		return sysSetting;
	}

	#endregion

	#region Methods: Public

	public string GetSysSettingValueByCode(string code){
		string json = JsonSerializer.Serialize(new GetSettingRequestData(code), _jsonSerializerOptions);
		string url = _serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.GetSysSettingValueByCode);
		string result = _creatioClient.ExecutePostRequest(url, json);
		return result;
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

	public InsertSysSettingResponse InsertSysSetting(string name, string code, string valueTypeName,
		bool cached = true, string description = "", bool valueForCurrentUser = false){
		
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
			"Currency" => new CCurrency(name, code, null, cached, description, valueForCurrentUser),
			"Decimal" => new CDecimal(name, code, null, cached, description, valueForCurrentUser),
			"Lookup" => new Lookup(name, code, null, cached, description, valueForCurrentUser),
			var _ => throw new ArgumentOutOfRangeException(nameof(valueTypeName), valueTypeName,
				"Unsupported SysSettingType, Allowed values (Text, ShortText, MediumText, LongText, SecureText, " +
				"MaxSizeText, Boolean, DateTime, Date, Time, Integer, Currency, Decimal, Lookup)")
		};
		string json = sysSetting.ToString();
		const string endpoint = "DataService/json/SyncReply/InsertSysSettingRequest";
		string url = _serviceUrlBuilder.Build(endpoint);
		string response = _creatioClient.ExecutePostRequest(url, json);
		return JsonSerializer.Deserialize<InsertSysSettingResponse>(response, _jsonSerializerOptions);
	}

	public bool UpdateSysSetting(string code, object value, string valueTypeName = "Text"){
		string requestData = string.Empty;
		var sysSetting = GetSysSettingByCode(code);
		string optionsType = sysSetting is not null
			? sysSetting.ValueTypeName : valueTypeName;
		if (optionsType.Contains("Text") || optionsType.Contains("Date") || optionsType.Contains("Time") || optionsType.Contains("Lookup")) {
			if (optionsType == "Lookup") {
				bool isGuid = Guid.TryParse(value.ToString(), out Guid id);
				if (!isGuid) {
					Guid referenceSchemaUIduid = sysSetting.ReferenceSchemaUIdId;
					string entityName = GetSysSchemaNameByUid(referenceSchemaUIduid);
					Guid entityId = GetEntityIdByDisplayValue(entityName, value.ToString());
					value = entityId.ToString();
				}
			}
			if (new[] { "Date", "DateTime", "Time" }.Contains(optionsType)) {
				value = DateTime.Parse(value.ToString(), CultureInfo.InvariantCulture).ToString("yyyy-MM-ddTHH:mm:ss.fff");
			}

			//Enclosed opts.Value in "", otherwise update fails for all text settings
			requestData = "{\"isPersonal\":false,\"sysSettingsValues\":{" + $"\"{code}\":\"{value}\"" + "}}";
		} else {
			if (optionsType.Contains("Boolean")) {
				value = bool.Parse(value.ToString()).ToString().ToLower(CultureInfo.InvariantCulture);
			}

			requestData = "{\"isPersonal\":false,\"sysSettingsValues\":{" + $"\"{code}\":{value}" + "}}";
		}
		string postSysSettingsValuesUrl
			= _serviceUrlBuilder.Build("DataService/json/SyncReply/PostSysSettingsValues");
		string result = _creatioClient.ExecutePostRequest(postSysSettingsValuesUrl, requestData);
		if (string.IsNullOrWhiteSpace(result)) {
			_logger.WriteError($"SysSettings with code: {code} is not updated. Empty response received.");
			return false;
		}
		try {
			UpdateSysSettingResponse response = JsonSerializer.Deserialize<UpdateSysSettingResponse>(result, _jsonSerializerOptions);
			bool hasError = !string.IsNullOrWhiteSpace(response?.ResponseStatus?.ErrorCode)
				|| !string.IsNullOrWhiteSpace(response?.ResponseStatus?.Message);
			if (response is null || !response.Success || hasError) {
				_logger.WriteError($"SysSettings with code: {code} is not updated.");
				return false;
			}
			return true;
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

	public List<SysSettings> GetAllSysSettingsWithValues() {
		var sysSettings = AppDataContextFactory.GetAppDataContext(_dataProvider)
			.Models<SysSettings>()
			.Where(s => s.ValueTypeName != "Binary")
			.ToList();

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
