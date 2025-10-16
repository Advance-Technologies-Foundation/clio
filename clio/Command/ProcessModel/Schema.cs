using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using ErrorOr;

namespace Clio.Command.ProcessModel;


public class ProcessSchemaResponse{
	
	private static readonly JsonSerializerOptions IgnoreNullOptions = new () {
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	public readonly List<Guid> SubProcesses = [];
	public string ForAi = string.Empty;
	
	
	public static ErrorOr<ProcessSchemaResponse> FromJson(string jsonString, Common.ILogger logger) {
		try {
			ProcessSchemaResponse item =  JsonSerializer.Deserialize<ProcessSchemaResponse>(jsonString);
			FillParameterCaption(item, jsonString, logger);
			FillCollectionParameterCaption(item, jsonString, logger);
			FillFlowElementCaption(item, jsonString, logger);
			
			
			// var forAi = new {
			// 	processName = item.Schema.Name,
			// 	processDescription = item.Schema.Description?.GetValueOrDefault("en-US", string.Empty),
			// 	processCaption = item.Schema.Caption?.GetValueOrDefault("en-US", string.Empty),
			// 	uId= item.Schema.UId,
			// 	processParameters = item.Schema.MetaDataSchema.Parameters
			// 	, flowElements = item.Schema.MetaDataSchema.FlowElements
			// };
			// item.ForAi = JsonSerializer.Serialize(forAi, IgnoreNullOptions);
			
			IEnumerable<Guid> listOfSubProcesses = item.Schema.MetaDataSchema?.FlowElements?
									  .Where(fe => fe.EventType == ManagerMap.EventType.SubProcess && fe.SchemaUId != Guid.Empty)
									  .Select(fe => (Guid)fe.SchemaUId)
									  .AsEnumerable();
			item.SubProcesses.AddRange(listOfSubProcesses ?? []);
			return item;
		}
		catch (Exception e) {
			return Error.Failure("FromJson", $"Error deserializing process schema: {e.Message}");
		}
	}
	private static void FillParameterCaption(ProcessSchemaResponse item, string jsonString, Common.ILogger logger) {
		JsonDocument jsonDocument = JsonDocument.Parse(jsonString);
		JsonElement root = jsonDocument.RootElement;

		var hasResources = root.GetProperty("schema").TryGetProperty("resources", out var r);
		JsonElement resources = new ();
		if (hasResources) {
			resources = r;
		}
		else {
			return;
		}
		
		item.Schema.MetaDataSchema?.Parameters?.ForEach(p => {
			SetCaptionsForParameter(p, resources, logger);
			SetDescriptionForParameter(p, resources, logger);
		});
	}

	private static void FillCollectionParameterCaption(ProcessSchemaResponse item, string jsonString
		, Common.ILogger logger) {

		List<ProcessParameter> collectionParameters = item.Schema.MetaDataSchema?.Parameters?
														  .Where(p => p.ItemProperties != null && p.ItemProperties!.Count != 0)
														  .ToList();

		if (collectionParameters is null || collectionParameters.Count == 0) {
			return;
		}
		
		JsonDocument jsonDocument = JsonDocument.Parse(jsonString);
		JsonElement root = jsonDocument.RootElement;
		JsonElement resources = root.GetProperty("schema").GetProperty("resources");
		
		collectionParameters.ForEach(cp => {
			cp.ItemProperties?.ForEach(p => {
				SetCaptionsForParameter(p, resources, logger, cp.Name);
				SetDescriptionForParameter(p, resources, logger, cp.Name);
			});
		});
	}

	private static void FillFlowElementCaption(ProcessSchemaResponse item, string jsonString, Common.ILogger logger) {
		
		JsonDocument jsonDocument = JsonDocument.Parse(jsonString);
		JsonElement root = jsonDocument.RootElement;
		
		var hasResources = root.GetProperty("schema").TryGetProperty("resources", out var r);
		JsonElement resources = hasResources ? r : new JsonElement();
		
		
		item.Schema.MetaDataSchema?.FlowElements?.ForEach(fe => {
			
			string currentStep = $"GetProperty_BaseElement.{fe.Name}.Caption";
			try {
				JsonElement cap = resources.GetProperty($"BaseElements.{fe.Name}.Caption");
			
				//This line may fail
				currentStep = "Deserialize description";
				Dictionary<string, string> captions = cap.Deserialize<Dictionary<string, string>>();
			
				currentStep = "Set caption";
				fe.Captions = captions;
			}
			catch (Exception e){
				// Don't need to do anything, suppress all errors
				logger.WriteWarning($"Could not complete {currentStep} in FillFlowElementCaption for BaseElement: {fe.Name} due to: {e.Message}");
			}
			
		});
	}
	
	private static void SetCaptionsForParameter(ProcessParameter parameter, JsonElement resourcesElement, Common.ILogger logger, string collectionName = "") {
		string currentStep = $"GetProperty_Parameters.{parameter.Name}.Caption";
		try {
			JsonElement cap = string.IsNullOrWhiteSpace(collectionName) 
				? resourcesElement.GetProperty($"Parameters.{parameter.Name}.Caption")
				: resourcesElement.GetProperty($"Parameters.{collectionName}.{parameter.Name}.Caption");
			
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
	private static void SetDescriptionForParameter(ProcessParameter parameter, JsonElement resourcesElement, Common.ILogger logger, string collectionName = "") {
		string currentStep = $"GetProperty_Parameters.{parameter.Name}.Sys_Description";
		try {
			JsonElement desc =  string.IsNullOrWhiteSpace(collectionName) 
				? resourcesElement.GetProperty($"Parameters.{parameter.Name}.Sys_Description")
				: resourcesElement.GetProperty($"Parameters.{collectionName}.{parameter.Name}.Sys_Description");
			
			//This line may fail
			currentStep = "Deserialize description";
			Dictionary<string, string> descriptions = desc.Deserialize<Dictionary<string, string>>();
			
			currentStep = "Set description";
			parameter.Descriptions = descriptions;
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
	
	[JsonPropertyName("flowElements")]
	public List<FlowElement>? FlowElements { get; set; }
	
	
}

public class FlowElement{
	
	[JsonPropertyName("typeName")]
	public string TypeName { get; set; }
	
	[JsonPropertyName("uId")]
	public Guid UId { get; set; }
	
	[JsonPropertyName("name")]
	public string Name { get; set; }
	
	[JsonIgnore]
	[JsonPropertyName("createdInSchemaUId")]
	public Guid CreatedInSchemaUId { get; set; }
	
	[JsonIgnore]
	[JsonPropertyName("modifiedInSchemaUId")]
	public Guid ModifiedInSchemaUId { get; set; }
	
	[JsonIgnore]
	[JsonPropertyName("createdInPackageId")]
	public Guid CreatedInPackageId { get; set; }
	
	[JsonIgnore]
	[JsonPropertyName("containerUId")]
	public Guid ContainerUId { get; set; }
	
	[JsonIgnore]
	[JsonPropertyName("position")]
	public string Position { get; set; }
	
	[JsonPropertyName("managerItemUId")]
	public Guid ManagerItemUId { get; set; }

	[JsonPropertyName("EventType")]
	[JsonConverter(typeof(JsonStringEnumConverter))]
	public ManagerMap.EventType EventType => ManagerMap.Resolve(ManagerItemUId);
	
	[JsonIgnore]
	[JsonPropertyName("createdInOwnerSchemaUId")]
	public Guid CreatedInOwnerSchemaUId { get; set; }
	
	[JsonIgnore]
	[JsonPropertyName("size")]
	public string Size { get; set; }
	
	[JsonIgnore]
	[JsonPropertyName("isLogging")]
	public bool IsLogging { get; set; }
	
	[JsonPropertyName("parameters")]
	public List<FlowElementParameter> Parameters { get; set; }
	
	[JsonIgnore]
	[JsonPropertyName("isInterrupting")]
	public bool IsInterrupting { get; set; }
	
	[JsonPropertyName("sourceRefUId")]
	public Guid? SourceRefUId { get; set; }
	
	[JsonPropertyName("targetRefUId")]
	public Guid? TargetRefUId { get; set; }
	
	[JsonPropertyName("conditionExpression")]
	public string? ConditionExpression { get; set; }
	
	[JsonPropertyName("flowType")]
	[JsonConverter(typeof(JsonStringEnumConverter))]
	public FlowTypeSequence FlowType { get; set; } = 0;

	[JsonPropertyName("Captions")]
	public Dictionary<string, string>? Captions { get; set; }
	
	[JsonPropertyName("cronExpression")]
	public string? CronExpression { get; set; }
	
	[JsonPropertyName("timeZoneOffset")]
	public string? TimeZoneOffset { get; set; }

	[JsonPropertyName("timeZoneInfo")]
	[JsonConverter(typeof(TimeZoneInfoJsonConverter))]
	public TimeZoneInfo? TimeZoneInfo => string.IsNullOrWhiteSpace(TimeZoneOffset)
		? null
		: TimeZoneInfo.FindSystemTimeZoneById(TimeZoneOffset ?? "UTC");

	[JsonPropertyName("schemaUId")]
	public Guid? SchemaUId { get; set; }
}

public class FlowElementParameter{
	
	[JsonPropertyName("typeName")]
	public string TypeName { get; set; }
	
	[JsonPropertyName("uId")]
	public string UId { get; set; }
	
	[JsonPropertyName("name")]
	public string Name { get; set; }
	
	[JsonIgnore]
	[JsonPropertyName("createdInSchemaUId")]
	public Guid CreatedInSchemaUId { get; set; }
	
	[JsonIgnore]
	[JsonPropertyName("modifiedInSchemaUId")]
	public Guid ModifiedInSchemaUId { get; set; }
	
	[JsonIgnore]
	[JsonPropertyName("createdInPackageId")]
	public Guid CreatedInPackageId { get; set; }
	
	[JsonIgnore]
	[JsonPropertyName("containerUId")]
	public Guid ContainerUId { get; set; }
	
	[JsonPropertyName("dataValueType")]
	public Guid DataValueType { get; set; }
	
	[JsonPropertyName("DataValueTypeResolved")]
	[JsonConverter(typeof(TypeJsonConverter))]
	public Type DataValueTypeResolved => DataValueTypeMap.Resolve(DataValueType);
	
	[JsonPropertyName("sourceValue")]
	public SourceValue SourceValue { get; set; }
	
	
	[JsonPropertyName("direction"), JsonConverter(typeof(JsonStringEnumConverter))]
	public ProcessParameterDirection Direction { get; set; }
	
	[JsonPropertyName("itemProperties")]
	public List<ProcessParameter>? ItemProperties { get; set; }
	
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
    
	[JsonPropertyName("DataValueTypeResolved")]
	[JsonConverter(typeof(TypeJsonConverter))]
    public Type DataValueTypeResolved => DataValueTypeMap.Resolve(DataValueType);
    
    [JsonPropertyName("sourceValue")]
    public SourceValue SourceValue { get; set; }
    
    [JsonPropertyName("referenceSchemaUId")]
    public Guid? ReferenceSchemaUId { get; set; }
    
    [JsonPropertyName("direction"), JsonConverter(typeof(JsonStringEnumConverter))]
    public ProcessParameterDirection Direction { get; set; }

    public Dictionary<string, string>? Captions { get; set; }
    public Dictionary<string, string>? Descriptions { get; set; }
    
	[JsonPropertyName("itemProperties")]
	public List<ProcessParameter>? ItemProperties { get; set; }
	
}

public class SourceValue{
	[JsonPropertyName("modifiedInSchemaUId")]
    public Guid? ModifiedInSchemaUId { get; set; }
	
	[JsonPropertyName("value")]
	public string Value { get; set; }
	
	[JsonPropertyName("source")]
	public int Source { get; set; }
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
		{} when dataValueTypeUId == CompositeObjectListDataValueTypeUId => typeof(List<object>),
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
	internal static readonly Guid CompositeObjectListDataValueTypeUId  =
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

public enum FlowTypeSequence{
	Sequence,
	Default,
	Conditional,
	Data,
	Message,
	Association
}


/// <summary>
/// Maps managerItemUId to EventType
/// </summary>
/// <seealso cref="Terrasoft.Core.Process.ProcessSchemaElementManager"/>
public static class ManagerMap{
	
	public enum EventType {
		SequenceFlow,
		ConditionalFlow,
		DefFlow,
		DataAssociation,
		MessageFlow,
		StartEvent,
		StartMessageEvent,
		StartSignalEvent,
		StartTimer,
		StartMessageNonInterruptingEvent,
		EndEvent,
		TerminateEvent,
		StartSignalNonInterruptingEvent,
		IntermediateCatchMessageEvent,
		IntermediateCatchMessageNonInterruptingEvent,
		IntermediateThrowMessageEvent,
		IntermediateThrowSignalEvent,
		IntermediateCatchSignalEvent,
		IntermediateCatchSignalNonInterruptingEvent,
		IntermediateCatchTimerEvent,
		TextAnnotation,
		Association,
		Group,
		LaneSet,
		Lane,
		ParallelGateway,
		InclusiveGateway,
		ExclusiveGateway,
		EventBasedGateway,
		ParallelEventBasedGateway,
		ExclusiveEventBasedGateway,
		SubProcess,
		EventSubProcess,
		UserTask,
		ScriptTask,
		FormulaTask,
		ServiceTask,
		WebServiceTask,
		DataObject,
		Unknown
	}
	
	/// <summary>
	/// Gets the sequence flow unique identifier.
	/// </summary>
	private static Guid SequenceFlowUId { get; } = new Guid("{0D8351F6-C2F4-4737-BDD9-6FBFE0837FEC}");

	/// <summary>
	/// Gets the conditional flow unique identifier.
	/// </summary>
	private static Guid ConditionalFlowUId { get; } = new Guid("{DAC675D4-EA84-4E44-9056-38BF918618E9}");

	/// <summary>
	/// Gets the default flow unique identifier.
	/// </summary>
	private static Guid DefFlowUId { get; } = new Guid("{573ED909-E069-4161-B193-AE8DD9437C68}");

	/// <summary>
	/// Gets the data association unique identifier.
	/// </summary>
	private static Guid DataAssociationUId { get; } = new Guid("{2EA8E835-692C-4907-8FD5-E28B3095783A}");

	/// <summary>
	/// Gets the message flow unique identifier.
	/// </summary>
	private static Guid MessageFlowUId { get; } = new Guid("{1125414E-AC56-4052-A220-77D9719DA348}");

	/// <summary>
	/// Gets the untyped start event unique identifier.
	/// </summary>
	private static Guid StartEventUId { get; } = new Guid("{53818048-7868-48f6-ADA0-0EBAA65AF628}");

	/// <summary>
	/// Gets the start message event unique identifier.
	/// </summary>
	private static Guid StartMessageEventUId { get; } = new Guid("{02340C80-8E75-4f7a-917B-04125BC07192}");

	/// <summary>
	/// Gets the start signal event unique identifier.
	/// </summary>
	private static Guid StartSignalEventUId { get; } = new Guid("{1129E72F-0E8C-445A-B2EA-463517E86395}");

	/// <summary>
	/// Gets the non-interrupting start message event unique identifier.
	/// </summary>
	private static Guid StartMessageNonInterruptingEventUId { get; } =
		new Guid("{429178F5-D44A-40D6-8B74-E72CAD04EE73}");

	/// <summary>
	/// Gets the end event unique identifier.
	/// </summary>
	private static Guid EndEventUId { get; } = new Guid("{45CEAAE2-4E1F-4c0c-86AA-CD4AEB4DA913}");

	/// <summary>
	/// Gets the terminate event unique identifier.
	/// </summary>
	private static Guid TerminateEventUId { get; } = new Guid("{1BD93619-0574-454E-BB4E-CF53B9EB9470}");

	/// <summary>
	/// Gets the non-interrupting start signal event unique identifier.
	/// </summary>
	private static Guid StartSignalNonInterruptingEventUId { get; } =
		new Guid("{933555C5-E7D2-456D-B5AB-5F94D62B693A}");

	/// <summary>
	/// Gets the intermediate catch message event unique identifier.
	/// </summary>
	private static Guid IntermediateCatchMessageEventUId { get; } =
		new Guid("{3CB9D737-779E-4085-AB4B-DB590853E266}");

	/// <summary>
	/// Gets the non-interrupting intermediate catch message event unique identifier.
	/// </summary>
	private static Guid IntermediateCatchMessageNonInterruptingEventUId { get; } =
		new Guid("{B851455C-ED5A-427c-831F-19DD15EB3E76}");

	/// <summary>
	/// Gets the intermediate throw message event unique identifier.
	/// </summary>
	private static Guid IntermediateThrowMessageEventUId { get; } =
		new Guid("{7B8B16FB-D4C6-4e8b-A519-988250AC636F}");

	/// <summary>
	/// Gets the intermediate throw signal event unique identifier.
	/// </summary>
	private static Guid IntermediateThrowSignalEventUId { get; } =
		new Guid("{1793BFF9-C5FA-49e7-A32D-CA73D270E137}");

	/// <summary>
	/// Gets the intermediate catch signal event unique identifier.
	/// </summary>
	private static Guid IntermediateCatchSignalEventUId { get; } =
		new Guid("{5CCAD23D-FC4B-4ec7-8051-E3A825B698FC}");

	/// <summary>
	/// Gets the non-interrupting intermediate catch signal event unique identifier.
	/// </summary>
	private static Guid IntermediateCatchSignalNonInterruptingEventUId { get; } =
		new Guid("{1140FFF1-32A9-40de-A28B-111797383C67}");

	/// <summary>
	/// Gets the intermediate catch timer event unique identifier.
	/// </summary>
	private static Guid IntermediateCatchTimerEventUId { get; } = new Guid("{97D1AF3D-EF13-465C-B6D8-5425F78BF000}");

	/// <summary>
	/// Gets the text annotation unique identifier.
	/// </summary>
	private static Guid TextAnnotationUId { get; } = new Guid("{9E3E3482-2552-47DB-96CC-2F6F84E0E61F}");

	/// <summary>
	/// Gets the association unique identifier.
	/// </summary>
	private static Guid AssociationUId { get; } = new Guid("{D1F3A4A0-34E6-4DAB-9315-CC9DE0CAB7FE}");

	/// <summary>
	/// Gets the group unique identifier.
	/// </summary>
	private static Guid GroupUId { get; } = new Guid("{EB7053A9-51E7-456f-9498-73CCC42BFEB6}");

	/// <summary>
	/// Gets the lane set unique identifier.
	/// </summary>
	private static Guid LaneSetUId { get; } = new Guid("{11A47CAF-A0D5-41fa-A274-A0B11F77447A}");

	/// <summary>
	/// Gets the lane unique identifier.
	/// </summary>
	private static Guid LaneUId { get; } = new Guid("{ABCD74B9-5912-414b-82AC-F1AA4DCD554E}");

	/// <summary>
	/// Gets the parallel gateway unique identifier.
	/// </summary>
	private static Guid ParallelGatewayUId { get; } = new Guid("{E9E1E6DE-7066-4eb1-BBB4-5B75B13D4F56}");

	/// <summary>
	/// Gets the inclusive gateway unique identifier.
	/// </summary>
	private static Guid InclusiveGatewayUId { get; } = new Guid("{FFA4A06A-5747-49d4-96C2-C32A727A3B14}");

	/// <summary>
	/// Gets the exclusive gateway unique identifier.
	/// </summary>
	private static Guid ExclusiveGatewayUId { get; } = new Guid("{BD9F7570-6C97-4f16-90E5-663A190C6C7C}");

	/// <summary>
	/// Gets the event-based gateway unique identifier.
	/// </summary>
	private static Guid EventBasedGatewayUId { get; } = new Guid("{0DDBDA75-9CAC-4e42-B94C-5CF1EDB45846}");

	/// <summary>
	/// Gets the parallel event-based gateway unique identifier.
	/// </summary>
	private static Guid ParallelEventBasedGatewayUId { get; } = new Guid("{B11EB76C-C34E-4a34-BA93-559B8B0A9D04}");

	/// <summary>
	/// Gets the exclusive event-based gateway unique identifier.
	/// </summary>
	private static Guid ExclusiveEventBasedGatewayUId { get; } = new Guid("{7A3D548C-E994-4d07-B1C0-F471E2CE5687}");

	/// <summary>
	/// Gets the subprocess unique identifier.
	/// </summary>
	private static Guid SubProcessUId { get; } = new Guid("{49EAFDBB-A89E-4BDF-A29D-7F17B1670A45}");

	/// <summary>
	/// Gets the event subprocess unique identifier.
	/// </summary>
	private static Guid EventSubProcessUId { get; } = new Guid("{0824AF03-1340-47A3-8787-EF542F566992}");

	/// <summary>
	/// Gets the user task unique identifier.
	/// </summary>
	private static Guid UserTaskUId { get; } = new Guid("{1418E61A-82C3-403E-8221-01088F52C125}");

	/// <summary>
	/// Gets the script task unique identifier.
	/// </summary>
	private static Guid ScriptTaskUId { get; } = new Guid("{0E490DDA-E140-4441-B600-6F5C64D024DF}");

	/// <summary>
	/// Gets the formula task unique identifier.
	/// </summary>
	private static Guid FormulaTaskUId { get; } = new Guid("{D334D28F-B11A-477E-9FF0-0A95FA73D53B}");

	/// <summary>
	/// Gets the formula task parameters edit page unique identifier.
	/// </summary>
	private static Guid FormulaTaskParametersEditPageUId { get; } =
		new Guid("{CF656BDD-D474-4488-AC1E-D43590D6D130}");

	/// <summary>
	/// Gets the condition expression edit page unique identifier.
	/// </summary>
	private static Guid ConditionExpressionEditPageUId { get; } = new Guid("{754BDAFD-B495-4E95-94A6-CE571E4CCD66}");

	/// <summary>
	/// Gets the lane user context edit page unique identifier.
	/// </summary>
	private static Guid LaneUserContextEditPageUId { get; } = new Guid("{AC4C3AF5-F3DF-452D-ADEC-C79A971E1ECB}");

	/// <summary>
	/// Gets the service task unique identifier.
	/// </summary>
	private static Guid ServiceTaskUId { get; } = new Guid("{24480389-FEC0-46C1-B4B3-1958DD332185}");

	/// <summary>
	/// Gets the web service task unique identifier.
	/// </summary>
	private static Guid WebServiceTaskUId { get; } = new Guid("{652B598D-CDD5-49D5-A86D-AAFAC88213F3}");

	/// <summary>
	/// Gets the data object unique identifier.
	/// </summary>
	private static Guid DataObjectUId { get; } = new Guid("{7197FE74-0FA0-4a7a-8F4B-86569678AA8C}");

	
	
	private static Guid StartTimerEventUId { get; } = new Guid("{c735ed92-e545-4699-b3c6-f8f57dd8c529}");
	
	
	/// <summary>
	/// Gets the intermediate catch signal parameters edit page schema unique identifier.
	/// </summary>
	private static Guid IntermediateCatchSignalParametersEditPageSchemaUId { get; } =
		new Guid("969A791D-250A-417E-9DE8-BC4FAEFD9ED4");

	
	private static readonly Dictionary<Guid, EventType> Map = new() {
	    { SequenceFlowUId, EventType.SequenceFlow },
	    { ConditionalFlowUId, EventType.ConditionalFlow },
	    { DefFlowUId, EventType.DefFlow },
	    { DataAssociationUId, EventType.DataAssociation },
	    { MessageFlowUId, EventType.MessageFlow },
	    { StartEventUId, EventType.StartEvent },
	    { StartTimerEventUId, EventType.StartTimer },
	    { StartMessageEventUId, EventType.StartMessageEvent },
	    { StartSignalEventUId, EventType.StartSignalEvent },
	    { StartMessageNonInterruptingEventUId, EventType.StartMessageNonInterruptingEvent },
	    { EndEventUId, EventType.EndEvent },
	    { TerminateEventUId, EventType.TerminateEvent },
	    { StartSignalNonInterruptingEventUId, EventType.StartSignalNonInterruptingEvent },
	    { IntermediateCatchMessageEventUId, EventType.IntermediateCatchMessageEvent },
	    { IntermediateCatchMessageNonInterruptingEventUId, EventType.IntermediateCatchMessageNonInterruptingEvent },
	    { IntermediateThrowMessageEventUId, EventType.IntermediateThrowMessageEvent },
	    { IntermediateThrowSignalEventUId, EventType.IntermediateThrowSignalEvent },
	    { IntermediateCatchSignalEventUId, EventType.IntermediateCatchSignalEvent },
	    { IntermediateCatchSignalNonInterruptingEventUId, EventType.IntermediateCatchSignalNonInterruptingEvent },
	    { IntermediateCatchTimerEventUId, EventType.IntermediateCatchTimerEvent },
	    { TextAnnotationUId, EventType.TextAnnotation },
	    { AssociationUId, EventType.Association },
	    { GroupUId, EventType.Group },
	    { LaneSetUId, EventType.LaneSet },
	    { LaneUId, EventType.Lane },
	    { ParallelGatewayUId, EventType.ParallelGateway },
	    { InclusiveGatewayUId, EventType.InclusiveGateway },
	    { ExclusiveGatewayUId, EventType.ExclusiveGateway },
	    { EventBasedGatewayUId, EventType.EventBasedGateway },
	    { ParallelEventBasedGatewayUId, EventType.ParallelEventBasedGateway },
	    { ExclusiveEventBasedGatewayUId, EventType.ExclusiveEventBasedGateway },
	    { SubProcessUId, EventType.SubProcess },
	    { EventSubProcessUId, EventType.EventSubProcess },
	    { UserTaskUId, EventType.UserTask },
	    { ScriptTaskUId, EventType.ScriptTask },
	    { FormulaTaskUId, EventType.FormulaTask },
	    { ServiceTaskUId, EventType.ServiceTask },
	    { WebServiceTaskUId, EventType.WebServiceTask },
	    { DataObjectUId, EventType.DataObject }
	};
	
	public static EventType Resolve(Guid managerItemUId) {
		return Map.GetValueOrDefault(managerItemUId, EventType.Unknown);
	}
}


public class TypeJsonConverter : JsonConverter<Type>{
	public override Type Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
		var typeName = reader.GetString();
		if (string.IsNullOrWhiteSpace(typeName)) {
			return null;
		}
		return Type.GetType(typeName, throwOnError: false);
	}

	public override void Write(Utf8JsonWriter writer, Type value, JsonSerializerOptions options)
	{
		writer.WriteStringValue(value?.FullName);
	}
}


public class TimeZoneInfoJsonConverter : JsonConverter<TimeZoneInfo>
{
	public override TimeZoneInfo Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		var timeZoneId = reader.GetString();
		if (string.IsNullOrWhiteSpace(timeZoneId))
		{
			return TimeZoneInfo.Utc;
		}
		try
		{
			return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
		}
		catch (TimeZoneNotFoundException)
		{
			return TimeZoneInfo.Utc;
		}
		catch (InvalidTimeZoneException)
		{
			return TimeZoneInfo.Utc;
		}
	}

	public override void Write(Utf8JsonWriter writer, TimeZoneInfo value, JsonSerializerOptions options)
	{
		writer.WriteStringValue(value?.Id ?? "UTC");
	}
}