namespace Creatio.ConflictResolver;

/// <summary>Identifies the supported Creatio package artifact shapes.</summary>
public enum ConflictFileType
{
	/// <summary>Schema metadata stored in <c>metadata.json</c>.</summary>
	MetadataJson = 0,
	/// <summary>Schema identity and package descriptor JSON.</summary>
	DescriptorJson = 1,
	/// <summary>Localizable resource XML for a supported schema.</summary>
	ResourceXml = 2,
	/// <summary>Freedom UI client module JavaScript with marked semantic sections.</summary>
	ClientUnitJs = 3,
	/// <summary>Creatio package data binding JSON.</summary>
	DataBinding = 4,
	/// <summary>Package <c>properties.json</c>.</summary>
	PropertiesJson = 5,
	/// <summary>C# source code that requires manual handling.</summary>
	SourceCode = 6,
	/// <summary>SQL source that requires manual handling.</summary>
	SqlScript = 7,
	/// <summary>BusinessProcess schema metadata that is recognized but not implemented.</summary>
	ProcessMetadataJson = 8,
	/// <summary>BusinessProcess resource XML that is recognized but not implemented.</summary>
	ProcessResourceXml = 9
}
