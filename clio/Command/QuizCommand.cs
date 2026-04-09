using CommandLine;

namespace Clio.Command;

/// <summary>
/// Command options for the quiz easter egg game.
/// </summary>
[Verb("quiz", Hidden = true, HelpText = "Interactive quiz game easter egg")]
public class QuizCommandOptions
{
}

/// <summary>
/// Command handler for the Creatio Quiz easter egg game.
/// Launches an interactive console-based quiz where colleagues from different departments
/// ask questions that must be answered using left/right arrow keys.
/// </summary>
public class QuizCommand : Command<QuizCommandOptions>
{
	/// <summary>
	/// Executes the quiz game command.
	/// </summary>
	/// <param name="options">Command options (none required for this command).</param>
	/// <returns>Exit code (0 for success).</returns>
	public override int Execute(QuizCommandOptions options)
	{
		Quiz.QuizGame.Run();
		return 0;
	}
}
