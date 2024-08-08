using Clio.Common;
using CommandLine;

namespace Clio.Command;


[Verb("info", Aliases = new string[] {  }, HelpText = "Add schema to package")]
public class AddSchemaOptions
{
	
	[Option("type", Required = true, HelpText = "Get versions for all known components")]
	public string SchemaType
	{
		get; set;
	}

	[Option("package-name", Required = true, HelpText = "Package name")]
	public string Package
	{
		get; set;
	}

	[Option("schema-name", Required = true, HelpText = "Schema Name")]
	public string SchemaName { get; set; }

}

public class AddSchemaCommand : Command<AddSchemaOptions>
{

	private readonly ISchemaBuilder _schemaBuilder;

	public AddSchemaCommand(ISchemaBuilder schemaBuilder){
		_schemaBuilder = schemaBuilder;
	}
	public override int Execute(AddSchemaOptions options){
		_schemaBuilder.AddSchema(options.SchemaType, options.SchemaName, options.Package);
		return 0;
	}

}