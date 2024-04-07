using System;
using System.Globalization;
using System.Text.Json;

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

	#endregion

}

public class SysSettingsManager : ISysSettingsManager
{

	#region Fields: Private

	private readonly IApplicationClient _creatioClient;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;

	private readonly JsonSerializerOptions _jsonSerializerOptions = new() {
		WriteIndented = false,
		AllowTrailingCommas = false,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	#endregion

	#region Constructors: Public

	public SysSettingsManager(IApplicationClient creatioClient,
		IServiceUrlBuilder serviceUrlBuilder){
		_creatioClient = creatioClient;
		_serviceUrlBuilder = serviceUrlBuilder;
	}

	#endregion

	#region Methods: Private

	private static object ConvertToBool(string value){
		bool isBool = bool.TryParse(value, out bool boolValue);
		return isBool ? (object)boolValue
			: throw new InvalidCastException($"Could not convert {value} to {nameof(Boolean)}");
	}

	private static object ConvertToDateTime(string value){
		bool isDateTime = DateTime.TryParse(value, out DateTime boolValue);
		return isDateTime ? (object)boolValue
			: throw new InvalidCastException($"Could not convert {value} to {nameof(Boolean)}");
	}

	private static object ConvertToDecimal(string value){
		bool isDecimal = decimal.TryParse(value, out decimal decValue);
		return isDecimal ? (object)decValue
			: throw new InvalidCastException($"Could not convert {value} to {nameof(Decimal)}");
	}
	
	private static object ConvertToGuid(string value){
    		bool isGuid = Guid.TryParse(value, out Guid decValue);
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

	#endregion

	#region Methods: Public

	public string GetSysSettingValueByCode(string code){
		const string endpoint = "rest/CreatioApiGateway/GetSysSettingValueByCode";
		string json = JsonSerializer.Serialize(new GetSettingRequestData(code), _jsonSerializerOptions);
		string url = _serviceUrlBuilder.Build(endpoint);
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

	#endregion

	internal record GetSettingRequestData(string Code);

}