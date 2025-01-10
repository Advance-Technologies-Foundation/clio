using Clio.Common;
using CommandLine;
using System.Net.Security;

namespace Clio.Command;

[Verb("add-schema", Aliases = new string[] { }, HelpText = "Add schema to package")]
public class AddSchemaOptions
{

	#region Properties: Public

	[Option('p', "package", Required = true, HelpText = "Package path or name")]
	public string Package { get; set; }

	[Value(0, Required = true, HelpText = "Schema Name")]
	public string SchemaName { get; set; }

	[Option('t', "type", Required = true, HelpText = "Schema type")]
	public string SchemaType { get; set; }

	#endregion

}

public class AddSchemaCommand : Command<AddSchemaOptions>
{

	#region Fields: Private

	private readonly ISchemaBuilder _schemaBuilder;
	private readonly ILogger _logger;

	#endregion

	#region Constructors: Public

	public AddSchemaCommand(ISchemaBuilder schemaBuilder, ILogger logger){
		_schemaBuilder = schemaBuilder;
		_logger = logger;
	}

	#endregion

	#region Methods: Public

	public override int Execute(AddSchemaOptions options){
		_schemaBuilder.AddSchema(options.SchemaType, options.SchemaName, options.Package);
		_logger.WriteInfo("Done");
		return 0;
	}

	#endregion
	
	
}