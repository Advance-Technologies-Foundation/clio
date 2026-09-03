using Clio.Common;
using Clio.UserEnvironment;
using CommandLine;

namespace Clio.Command.Update;

[Verb("autoupdate", HelpText = "Enable or disable automatic clio updates on startup")]
public class SetAutoupdateOptions {

	[Option("enable", SetName = "enable", HelpText = "Enable automatic updates on startup (default behavior)")]
	public bool Enable { get; set; }

	[Option("disable", SetName = "disable", HelpText = "Disable automatic updates on startup")]
	public bool Disable { get; set; }

}

public class SetAutoupdateCommand : Command<SetAutoupdateOptions> {

	private readonly ISettingsRepository _settingsRepository;
	private readonly ILogger _logger;

	public SetAutoupdateCommand(ISettingsRepository settingsRepository, ILogger logger) {
		_settingsRepository = settingsRepository;
		_logger = logger;
	}

	public override int Execute(SetAutoupdateOptions options) {
		if (options.Enable) {
			_settingsRepository.SetAutoupdate(true);
			_logger.WriteInfo("Automatic clio updates enabled.");
			return 0;
		}
		if (options.Disable) {
			_settingsRepository.SetAutoupdate(false);
			_logger.WriteInfo("Automatic clio updates disabled. Run 'clio update' to update manually.");
			return 0;
		}
		bool current = _settingsRepository.GetAutoupdate();
		_logger.WriteInfo($"Automatic clio updates are currently {(current ? "enabled" : "disabled")}.");
		_logger.WriteInfo("Use --enable or --disable to change.");
		return 0;
	}

}
