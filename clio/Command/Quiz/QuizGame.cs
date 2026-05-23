using System;
using System.Reflection;
using IAbstractionsFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Command.Quiz;

/// <summary>
/// Main entry point for the Creatio Quiz easter egg game.
/// </summary>
public static class QuizGame
{
	public static void Run(IAbstractionsFileSystem fileSystem)
	{
		// Check console size and show warning if too small
		const int optimalWidth = 96;
		const int optimalHeight = 36;
		
		if (Console.WindowWidth < optimalWidth || Console.WindowHeight < optimalHeight)
		{
			Console.ForegroundColor = ConsoleColor.Yellow;
			Console.WriteLine("CREATIO TWIN'S QUIZ");
			Console.ResetColor();
			Console.WriteLine();
			Console.WriteLine($"⚠ Warning: Optimal console size is {optimalWidth}×{optimalHeight} characters.");
			Console.WriteLine($"Current size: {Console.WindowWidth}×{Console.WindowHeight}");
			Console.WriteLine();
			Console.WriteLine("The game may not display correctly, but you can still play.");
			Console.WriteLine("For best experience, resize your terminal window.");
			Console.WriteLine();
			Console.WriteLine("Press any key to continue anyway...");
			Console.ReadKey(true);
		}

		var renderer = new PeopleRenderer();
		renderer.ConfigureConsole();

		try
		{
			var assemblyLocation = fileSystem.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
				?? throw new InvalidOperationException("Could not determine assembly location");
			var questionFilePath = fileSystem.Path.Combine(assemblyLocation, "resources", "quiz", "questions.txt");
			var repository = QuestionRepository.Load(questionFilePath, fileSystem);

			while (true)
			{
				renderer.ShowIntro();

				var session = new PeopleGameSession(repository, renderer);
				var result = session.Run();
				if (result == PeopleSessionResult.Exit)
				{
					break;
				}
			}
		}
		finally
		{
			renderer.RestoreConsole();
		}
	}
}
