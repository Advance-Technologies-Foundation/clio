using CommandLine;

namespace Clio;

public class BaseCommandOptions
{
    [Option("--fail-on-error", Required = false, HelpText = "Return fail code on errors")]
    public bool FailOnError
    {
        get => GlobalContext.FailOnError;
        set => GlobalContext.FailOnError = value;
    }

    [Option("--fail-on-warning", Required = false, HelpText = "Return fail code on warnings ")]
    public bool FailOnWarning
    {
        get => GlobalContext.FailOnWarning;
        set => GlobalContext.FailOnWarning = value;
    }
}
