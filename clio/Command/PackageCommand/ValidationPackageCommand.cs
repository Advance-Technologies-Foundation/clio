using CommandLine;

namespace Clio.Command.PackageCommand;

[Verb("validation-pkg", Aliases = new[]
{
    "validation"
}, HelpText = "Validation package")]
public class ValidationPkgOptions
{

    #region Properties: Public

    [Option('d', "DestinationResult", Required = false, HelpText = "Destination path for result validation")]
    public string DestinationResult { get; set; }

    [Value(0, MetaName = "Name", Required = false, HelpText = "Name of the package for validation")]
    public string Name { get; set; }

    #endregion

}

internal class ValidationPackageCommand
{

    #region Methods: Public

    public static int Validate(ValidationPkgOptions opts)
    {
        return 1;
    }

    #endregion

}
