using CommandLine;

namespace Clio.Command;

[Verb("delete-pkg-remote", Aliases = new[]
{
    "delete"
}, HelpText = "Delete package from a web application")]
public class DeletePkgOptions : RemoteCommandOptions
{

    #region Properties: Public

    [Value(0, MetaName = "Name", Required = true, HelpText = "Package name")]
    public string Name { get; set; }

    #endregion

}
