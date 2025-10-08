using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using ErrorOr;

namespace Clio.Command.ProcessModel;


public class ProcessSchemaResponse{
	
	public static ErrorOr<ProcessSchemaResponse> FromJson(string jsonString, Common.ILogger logger) {
		try {
			ProcessSchemaResponse item =  JsonSerializer.Deserialize<ProcessSchemaResponse>(jsonString);
			FillParameterCaption(item, jsonString, logger);
			return item;
		}
		catch (Exception e) {
			return Error.Failure("FromJson", $"Error deserializing process schema: {e.Message}");
		}
	}
	private static void FillParameterCaption(ProcessSchemaResponse item, string jsonString, Common.ILogger logger) {
		JsonDocument jsonDocument = JsonDocument.Parse(jsonString);
		JsonElement root = jsonDocument.RootElement;
		JsonElement resources = root.GetProperty("schema").GetProperty("resources");
		
		item.Schema.MetaDataSchema.Parameters?.ForEach(p => {
			SetCaptionsForParameter(p, resources, logger);
			SetDescriptionForParameter(p, resources, logger);
		});
	}
	private static void SetCaptionsForParameter(ProcessParameter parameter, JsonElement resourcesElement, Common.ILogger logger) {
		string currentStep = $"GetProperty_Parameters.{parameter.Name}.Caption";
		try {
			JsonElement cap = resourcesElement.GetProperty($"Parameters.{parameter.Name}.Caption");
			
			//This line may fail
			currentStep = "Deserialize description";
			Dictionary<string, string> captions = cap.Deserialize<Dictionary<string, string>>();
			
			currentStep = "Set caption";
			parameter.Captions = captions;
		}
		catch (Exception e){
			// Don't need to do anything, suppress all errors
			logger.WriteWarning($"Could not complete {currentStep} in SetCaptionsForParameter for Parameter: {parameter.Name} due to: {e.Message}");
		}
	}
	private static void SetDescriptionForParameter(ProcessParameter parameter, JsonElement resourcesElement, Common.ILogger logger) {
		string currentStep = $"GetProperty_Parameters.{parameter.Name}.Sys_Description";
		try {
			JsonElement desc = resourcesElement.GetProperty($"Parameters.{parameter.Name}.Sys_Description");
			
			//This line may fail
			currentStep = "Deserialize description";
			Dictionary<string, string> descriptions = desc.Deserialize<Dictionary<string, string>>();
			
			currentStep = "Set description";
			parameter.Captions = descriptions;
		}
		catch(Exception e) {
			// Don't need to do anything, suppress all errors
			logger.WriteWarning($"Could not complete {currentStep} in SetDescriptionForParameter for Parameter: {parameter.Name} due to: {e.Message}");
		}
	}
	
	
	[JsonPropertyName("schema")]
	public Schema Schema { get; set; }
	
	[JsonPropertyName("success")]
	public bool Success { get; set; }
	
	[JsonPropertyName("maxEntitySchemaNameLength")]
	public int MaxEntitySchemaNameLength { get; set; }
	
}

public class Schema{
	public MetaDataSchema MetaDataSchema { get; private set; }

	private string _metadata = string.Empty;
	[JsonPropertyName("metaData")]
	public string Metadata {
		get => _metadata;
		set {
			_metadata = value;
			MetaDataSchema = JsonSerializer.Deserialize<MetaDataWrapper>(value)?.MetaData.Schema;
		} 
	}
	
	[JsonPropertyName("resources")]
	public Resources Resources { get; set; }
	
	[JsonPropertyName("parentSchemaUId")]
	public Guid ParentSchemaUId { get; set; }
	
	[JsonPropertyName("uId")]
	public Guid UId { get; set; }
	
	[JsonPropertyName("realUId")]
	public Guid RealUId { get; set; }
	
	[JsonPropertyName("name")]
	public string Name { get; set; }
	
	[JsonPropertyName("caption")]
	public Dictionary<string, string>? Caption { get; set; }
	
	[JsonPropertyName("description")]
	public Dictionary<string, string>? Description { get; set; }
	
	[JsonPropertyName("extendParent")]
	public bool ExtendParent { get; set; }
	
	[JsonPropertyName("packageUId")]
	public Guid PackageUId { get; set; }
	
	[JsonPropertyName("lazyProperties")]
	public List<object>? LazyProperties { get; set; }
	
	[JsonPropertyName("loadedLazyProperties")]
	public List<object>? LoadedLazyProperties { get; set; }
	
};

public class MetaDataWrapper{
	[JsonPropertyName("metaData")]
	public MetaData MetaData { get; set; }
}


public class MetaData{
	[JsonPropertyName("schema")]
	public MetaDataSchema Schema { get; set; }
}

public class MetaDataSchema{
	
	[JsonPropertyName("managerName")]
	public string ManagerName { get; set; }
	
	[JsonPropertyName("uId")]
	public Guid UId { get; set; }
	
	[JsonPropertyName("name")]
	public string Name { get; set; }
	
	[JsonPropertyName("packageUId")]
	public Guid PackageUId { get; set; }

	[JsonPropertyName("createdInVersion")]
	public Version? CreatedInVersion { get; set; }
	
	[JsonPropertyName("parameters")]
	public List<ProcessParameter>? Parameters { get; set; }
	
	[JsonPropertyName("serializeToDB")]
	public bool SerializeToDB { get; set; }
	
	
}

public class ProcessParameter{
	
	[JsonPropertyName("typeName")]
    public string TypeName { get; set; }
    
    [JsonPropertyName("uId")]
    public Guid UId { get; set; }
    
    [JsonPropertyName("name")]
    public string Name { get; set; }
    
    [JsonPropertyName("createdInSchemaUId")]
    public Guid CreatedInSchemaUId { get; set; }
    
    [JsonPropertyName("modifiedInSchemaUId")]
    public Guid ModifiedInSchemaUId { get; set; }
    
    [JsonPropertyName("dataValueType")]
    public Guid DataValueType { get; set; }
    
    public Type DataValueTypeResolved => DataValueTypeMap.Resolve(DataValueType);
    
    [JsonPropertyName("sourceValue")]
    public SourceValue SourceValue { get; set; }
    
    [JsonPropertyName("referenceSchemaUId")]
    public Guid ReferenceSchemaUId { get; set; }
    
    [JsonPropertyName("direction"), JsonConverter(typeof(JsonStringEnumConverter))]
    public ProcessParameterDirection Direction { get; set; }

    public Dictionary<string, string>? Captions { get; set; }
    public Dictionary<string, string>? Descriptions { get; set; }
    
}

public class SourceValue{
	[JsonPropertyName("modifiedInSchemaUId")]
    public Guid ModifiedInSchemaUId { get; set; }
}

public enum ProcessParameterDirection {
	Input = 0,
	Output = 1,
	Bidirectional = 2,
	Internal = 3
}


public static class DataValueTypeMap{
	public static Type Resolve(Guid dataValueTypeUId) => dataValueTypeUId switch {
		{} when dataValueTypeUId == BooleanDataValueTypeUId => typeof(bool),
		{} when dataValueTypeUId == ShortTextDataValueTypeUId => typeof(string),
		{} when dataValueTypeUId == MediumTextDataValueTypeUId => typeof(string),
		{} when dataValueTypeUId == LongTextDataValueTypeUId => typeof(string),
		{} when dataValueTypeUId == MaxSizeTextDataValueTypeUId => typeof(string),
		{} when dataValueTypeUId == TextDataValueTypeUId => typeof(string),
		{} when dataValueTypeUId == PhoneTextDataValueTypeUId => typeof(string),
		{} when dataValueTypeUId == WebTextDataValueTypeUId => typeof(string),
		{} when dataValueTypeUId == EmailTextDataValueTypeUId => typeof(string),
		{} when dataValueTypeUId == RichTextDataValueTypeUId => typeof(string),
		{} when dataValueTypeUId == SecureTextDataValueTypeUId => typeof(string),
		{} when dataValueTypeUId == HashTextDataValueTypeUId => typeof(string),
		{} when dataValueTypeUId == IntegerDataValueTypeUId => typeof(int),
		{} when dataValueTypeUId == FloatDataValueTypeUId => typeof(float),
		{} when dataValueTypeUId == Float1DataValueTypeUId => typeof(float),
		{} when dataValueTypeUId == Float2DataValueTypeUId => typeof(float),
		{} when dataValueTypeUId == Float3DataValueTypeUId => typeof(float),
		{} when dataValueTypeUId == Float4DataValueTypeUId => typeof(float),
		{} when dataValueTypeUId == Float8DataValueTypeUId => typeof(float),
		{} when dataValueTypeUId == MoneyDataValueTypeUId => typeof(decimal),
		{} when dataValueTypeUId == LookupDataValueTypeUId => typeof(Guid),
		{} when dataValueTypeUId == GuidDataValueTypeUId => typeof(Guid),
		{} when dataValueTypeUId == DateDataValueTypeUId => typeof(DateOnly),
		{} when dataValueTypeUId == DateTimeDataValueTypeUId => typeof(DateTime),
		{} when dataValueTypeUId == TimeDataValueTypeUId => typeof(TimeOnly),
		var _ => typeof(object)
	};


	/// <summary>
	/// Boolean data value type Id.
	/// </summary>
	private static readonly Guid BooleanDataValueTypeUId  = new ("{90B65BF8-0FFC-4141-8779-2420877AF907}");

	/// <summary>
	/// ShortText data value type Id.
	/// </summary>
	private static readonly Guid ShortTextDataValueTypeUId  = new ("{325A73B8-0F47-44A0-8412-7606F78003AC}");

	/// <summary>
	/// MediumText data value type Id.
	/// </summary>
	private static readonly Guid MediumTextDataValueTypeUId  = new ("{DDB3A1EE-07E8-4D62-B7A9-D0E618B00FBD}");

	/// <summary>
	/// LongText data value type Id.
	/// </summary>
	private static readonly Guid LongTextDataValueTypeUId  = new ("{5CA35F10-A101-4C67-A96A-383DA6AFACFC}");

	/// <summary>
	/// MaxSizeText data value type Id.
	/// </summary>
	private static readonly Guid MaxSizeTextDataValueTypeUId  = new ("{C0F04627-4620-4bc0-84E5-9419DC8516B1}");

	/// <summary>
	/// Text data value type Id.
	/// </summary>
	private static readonly Guid TextDataValueTypeUId  = new ("{8B3F29BB-EA14-4ce5-A5C5-293A929B6BA2}");

	/// <summary>
	/// Phone data value type Id.
	/// </summary>
	private static readonly Guid PhoneTextDataValueTypeUId  = new ("{26CBA63C-DAF1-4F36-B2EA-73C0D675D90C}");
	
	/// <summary>
	/// Web data value type Id.
	/// </summary>
	private static readonly Guid WebTextDataValueTypeUId  = new ("{26CBA64C-DAF1-4F36-B2EA-73C0D695D90C}");
	
	/// <summary>
	/// Email data value type Id.
	/// </summary>
	private static readonly Guid EmailTextDataValueTypeUId  = new ("{66CBA64C-DAF1-4F36-B8EA-73C0D695D90C}");

	/// <summary>
	/// Rich text data value type Id.
	/// </summary>
	private static readonly Guid RichTextDataValueTypeUId  = new ("{79BCCFFA-8C8B-4863-B376-A69D2244182B}");

	/// <summary>
	/// SecureText data value type Id.
	/// </summary>
	private static readonly Guid SecureTextDataValueTypeUId  = new ("{3509B9DD-2C90-4540-B82E-8F6AE85D8248}");

	/// <summary>
	/// HashText data value type Id.
	/// </summary>
	private static readonly Guid HashTextDataValueTypeUId  = new ("{ECBCCE18-2A17-4ead-829A-9D02FA9578A4}");

	/// <summary>
	/// DbObjectName data value type Id.
	/// </summary>
	private static readonly Guid DbObjectNameDataValueTypeUId  = new ("{0EAAA70F-2A5A-444e-BDF1-98B37895C820}");

	/// <summary>
	/// Localizable string data value type Id.
	/// </summary>
	private static readonly Guid LocalizableStringDataValueTypeUId  =
		new ("{95C6E6C4-2CC8-46BE-A1CB-96F942655F86}");

	/// <summary>
	/// Integer data value type Id.
	/// </summary>
	private static readonly Guid IntegerDataValueTypeUId  = new ("{6B6B74E2-820D-490E-A017-2B73D4CCF2B0}");

	/// <summary>
	/// Float data value type Id.
	/// </summary>
	private static readonly Guid FloatDataValueTypeUId  = new ("{57EE4C31-5EC4-45FA-B95D-3A2868AA89A8}");

	/// <summary>
	/// Float1 data value type Id.
	/// </summary>
	private static readonly Guid Float1DataValueTypeUId  = new ("{07BA84CE-0BF7-44B4-9F2C-7B15032EB98C}");

	/// <summary>
	/// Float2 data value type Id.
	/// </summary>
	private static readonly Guid Float2DataValueTypeUId  = new ("{5CC8060D-6D10-4773-89FC-8C12D6F659A6}");

	/// <summary>
	/// Float3 data value type Id.
	/// </summary>
	private static readonly Guid Float3DataValueTypeUId  = new ("{3F62414E-6C25-4182-BCEF-A73C9E396F31}");

	/// <summary>
	/// Float4 data value type Id.
	/// </summary>
	private static readonly Guid Float4DataValueTypeUId  = new ("{FF22E049-4D16-46EE-A529-92D8808932DC}");

	/// <summary>
	/// Float8 data value type Id.
	/// </summary>
	private static readonly Guid Float8DataValueTypeUId  = new ("{A4AAF398-3531-4A0D-9D75-A587F5B5B59E}");

	/// <summary>
	/// Money data value type Id.
	/// </summary>
	private static readonly Guid MoneyDataValueTypeUId  = new ("{969093E2-2B4E-463B-883A-3D3B8C61F0CD}");

	/// <summary>
	/// Lookup data value type Id.
	/// </summary>
	private static readonly Guid LookupDataValueTypeUId  = new ("{B295071F-7EA9-4e62-8D1A-919BF3732FF2}");

	/// <summary>
	/// Guid data value type Id.
	/// </summary>
	private static readonly Guid GuidDataValueTypeUId  = new ("{23018567-A13C-4320-8687-FD6F9E3699BD}");

	/// <summary>
	/// DateTime data value type Id.
	/// </summary>
	private static readonly Guid DateTimeDataValueTypeUId  = new ("{D21E9EF4-C064-4012-B286-FA1A8171DA44}");

	/// <summary>
	/// Date data value type Id.
	/// </summary>
	private static readonly Guid DateDataValueTypeUId  = new ("{603D4960-A1A2-45e9-B232-206A54421B01}");

	/// <summary>
	/// Binary data value type Id.
	/// </summary>
	private static readonly Guid BinaryDataValueTypeUId  = new ("{B7342B7A-5DDE-40de-AA7C-24D2A57B3202}");

	/// <summary>
	/// Time data value type Id.
	/// </summary>
	private static readonly Guid TimeDataValueTypeUId  = new ("{04CC757B-8F06-482c-8A1A-0C0E171D2410}");

	/// <summary>
	/// EntityCollection data value type Id.
	/// </summary>
	private static readonly Guid EntityCollectionDataValueTypeUId  =
		new ("{51FB23BA-3EB2-11E2-B7D5-B0C76188709B}");

	/// <summary>
	/// EntityColumnMappingCollection data value type Id.
	/// </summary>
	private static readonly Guid EntityColumnMappingCollectionDataValueTypeUId  =
		new ("{B53EAA2A-4BB7-4A6B-9F4F-58CCAB293E31}");

	/// <summary>
	/// LocalizableParameterValuesList data value type Id.
	/// </summary>
	private static readonly Guid LocalizableParameterValuesListDataValueTypeUId  =
		new ("{CFFC4762-C5C7-44BC-8CC6-CB55ABA6E06B}");

	/// <summary>
	/// MetaDataText data value type Id.
	/// </summary>
	private static readonly Guid MetaDataTextDataValueTypeUId  = new ("{394E160F-C8E0-46FA-9C0D-75D97E9E9169}");

	/// <summary>Returns the object list data value type identifier.</summary>
	/// <value>The object list data value type identifier.</value>
	private static readonly Guid ObjectListDataValueTypeUId  = new ("4B51A8B5-1EE9-4437-9D58-F35E083CBCDF");

	/// <summary>
	/// Returns the composite object list data value type identifier.
	/// </summary>
	/// <value>The composite object list data value type identifier.</value>
	private static readonly Guid CompositeObjectListDataValueTypeUId  =
		new Guid("651EC16F-D140-46DB-B9E2-825C985A8AC2");

	/// <summary>
	/// Gets unique identifier of the file locator data value type.
	/// </summary>
	private static readonly Guid FileLocatorDataValueTypeUId  = new ("A33C9252-D401-453E-949D-169157067ED9");

	/// <summary>
	/// Gets unique identifier of the composite object data value type.
	/// </summary>
	private static readonly Guid CompositeObjectDataValueTypeUId  = new ("632E4371-0A7F-46CD-A284-A623B3933027");

	/// <summary>
	/// Gets unique identifier of the color data value type.
	/// </summary>
	private static readonly Guid ColorDataValueTypeUId  = new ("{DAFB71F9-EE9F-4e0b-A4D7-37AA15987155}");
}