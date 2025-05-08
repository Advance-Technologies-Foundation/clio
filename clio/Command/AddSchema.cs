using System.Net.Security;

using Clio.Common;
using CommandLine;

namespace Clio.Command;

[Verb("add-schema", Aliases = new string[] { }, HelpText = "Add schema to package")]
public class AddSchemaOptions
{
    [Option('p', "package", Required = true, HelpText = "Package path or name")]
    public string Package { get; set; }

    [Value(0, Required = true, HelpText = "Schema Name")]
    public string SchemaName { get; set; }

    [Option('t', "type", Required = true, HelpText = "Schema type")]
    public string SchemaType { get; set; }
}

public class AddSchemaCommand(ISchemaBuilder schemaBuilder, ILogger logger): Command<AddSchemaOptions>
{
    private readonly ISchemaBuilder _schemaBuilder = schemaBuilder;
    private readonly ILogger _logger = logger;

    public override int Execute(AddSchemaOptions options)
    {
        _schemaBuilder.AddSchema(options.SchemaType, options.SchemaName, options.Package);
        _logger.WriteInfo("Done");
        return 0;
    }
}
