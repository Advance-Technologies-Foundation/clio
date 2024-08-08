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

	[Option("package", Required = true, HelpText = "Package name")]
	public string Package
	{
		get; set;
	}

}

public class AddSchemaCommand : Command<AddSchemaOptions>
{

	public override int Execute(AddSchemaOptions options){
		//throw new System.NotImplementedException();
		return 0;
	}

}