using System;
using Clio.UserEnvironment;
using CommandLine;

namespace Clio.Command;

[Verb("unreg-web-app", Aliases = new[]
{
    "unreg"
}, HelpText = "Unregister application's settings from the list")]
public class UnregAppOptions : EnvironmentOptions
{

    #region Properties: Public

    [Value(0, MetaName = "Name", Required = false, HelpText = "Application name")]
    public string Name { get; set; }

    [Option("all", Required = false, HelpText = "Try login after registration")]
    public bool UnregAll { get; set; }

    #endregion

}

public class UnregAppCommand : Command<UnregAppOptions>
{

    #region Fields: Private

    private readonly ISettingsRepository _settingsRepository;

    #endregion

    #region Constructors: Public

    public UnregAppCommand(ISettingsRepository settingsRepository)
    {
        _settingsRepository = settingsRepository;
    }

    #endregion

    #region Methods: Public

    public override int Execute(UnregAppOptions options)
    {
        try
        {
            if (options.UnregAll)
            {
                _settingsRepository.RemoveAllEnvironment();
            }
            else
            {
                if (string.IsNullOrEmpty(options.Name))
                {
                    throw new ArgumentException("Name cannot be empty");
                }
                _settingsRepository.RemoveEnvironment(options.Name);
                Console.WriteLine($"Envronment {options.Name} was deleted...");
                Console.WriteLine();
                Console.WriteLine("Done");
            }
            return 0;
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
            return 1;
        }
    }

    #endregion

}
