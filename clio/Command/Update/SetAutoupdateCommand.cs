using Clio.Common;
using Clio.UserEnvironment;
using CommandLine;

namespace Clio.Command.Update;

/// <summary>Options for inspecting or changing automatic clio updates.</summary>
[Verb("autoupdate", HelpText = "Enable or disable automatic clio updates on startup")]
public class SetAutoupdateOptions {

	/// <summary>Gets or sets whether to opt in to automatic clio updates.</summary>
	[Option("enable", SetName = "enable", HelpText = "Enable automatic updates on startup")]
	public bool Enable { get; set; }

	/// <summary>Gets or sets whether to disable automatic clio updates.</summary>
	[Option("disable", SetName = "disable", HelpText = "Disable automatic updates on startup (default behavior)")]
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
