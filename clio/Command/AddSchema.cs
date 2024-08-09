using Clio.Common;
using CommandLine;

namespace Clio.Command;


[Verb("add-schema", Aliases = new string[] {  }, HelpText = "Add schema to package")]
public class AddSchemaOptions
{
	
	[Option('t',"type", Required = true, HelpText = "Get versions for all known components")]
	public string SchemaType
	{
		get; set;
	}

	[Option('p',"package", Required = true, HelpText = "Package path or name")]
	public string Package
	{
		get; set;
	}

	[Value(0, Required = true, HelpText = "Schema Name")]
	public string SchemaName { get; set; }

}

public class AddSchemaCommand : Command<AddSchemaOptions>
{

	private readonly ISchemaBuilder _schemaBuilder;
	private readonly ILogger _logger;

	public AddSchemaCommand(ISchemaBuilder schemaBuilder, ILogger logger){
		_schemaBuilder = schemaBuilder;
		_logger = logger;
	}
	public override int Execute(AddSchemaOptions options){
		_schemaBuilder.AddSchema(options.SchemaType, options.SchemaName, options.Package);
		_logger.WriteInfo("Done");
		return 0;
	}

}