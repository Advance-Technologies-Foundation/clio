using System;
using System.Collections.Generic;

namespace Clio.Command.Quiz;

internal sealed class PeopleRenderer
{
	private const int Width = 96;
	private const int Height = 34;
	private const int PlayerX = Width / 2;
	private const int PlayerY = Height - 7;
	private const int DialogTop = 12;
	private const int DialogHeight = 11;

	private readonly char[,] _canvas = new char[Height, Width];
	private readonly ConsoleColor?[,] _colors = new ConsoleColor?[Height, Width];
	private readonly char[,] _previousCanvas = new char[Height, Width];
	private readonly ConsoleColor?[,] _previousColors = new ConsoleColor?[Height, Width];
	private bool _hasPreviousFrame;

	public void ConfigureConsole()
	{
		Console.CursorVisible = false;
		Console.OutputEncoding = System.Text.Encoding.UTF8;

		try
		{
			if (OperatingSystem.IsWindows() &&
				(Console.WindowWidth < Width || Console.WindowHeight < Height + 2))
			{
				Console.SetWindowSize(
					Math.Max(Console.WindowWidth, Width),
					Math.Max(Console.WindowHeight, Height + 2));
			}

			if (OperatingSystem.IsWindows() &&
				(Console.BufferWidth < Width || Console.BufferHeight < Height + 2))
			{
				Console.SetBufferSize(
					Math.Max(Console.BufferWidth, Width),
					Math.Max(Console.BufferHeight, Height + 2));
			}
		}
		catch
		{
			// Some terminals do not allow resizing; game still works with clipping.
		}
	}

	public void RestoreConsole()
	{
		Console.ResetColor();
		Console.Clear();
		Console.CursorVisible = true;
		_hasPreviousFrame = false;
	}

	public void ShowIntro()
	{
		ResetBackBuffer();
		Console.Clear();
		Console.ForegroundColor = ConsoleColor.Cyan;
		Console.WriteLine("CREATIO TWIN'S QUIZ");
		Console.ResetColor();
		Console.WriteLine();
		Console.WriteLine("Your colleagues will walk over and ask simple work questions.");
		Console.WriteLine("Answer with LEFT or RIGHT.");
		Console.WriteLine();
		Console.WriteLine("Each colleague has a progress bar above them.");
		Console.WriteLine("Green means a correct answer, red means a wrong answer.");
		Console.WriteLine();
		Console.WriteLine("Reach 10 correct answers before 10 wrong answers.");
		Console.WriteLine();
		Console.WriteLine("Press any key to begin...");
		Console.ReadKey(true);
	}

	public PeopleSessionResult ShowEndScreen(bool victory, int correctCount, int wrongCount)
	{
		ResetBackBuffer();
		Console.Clear();
		Console.ForegroundColor = victory ? ConsoleColor.Green : ConsoleColor.Red;
		Console.WriteLine(victory ? "TEAM TRUST WON" : "TOO MANY WRONG ANSWERS");
		Console.ResetColor();
		Console.WriteLine();
		Console.WriteLine(victory
			? "You handled the conversations well."
			: "The team needs better answers next time.");
		Console.WriteLine($"Correct meter: {correctCount}/10");
		Console.WriteLine($"Wrong meter:   {wrongCount}/10");
		Console.WriteLine();
		Console.WriteLine("Press R to restart or Q to quit.");

		while (true)
		{
			var key = Console.ReadKey(true).Key;
			if (key == ConsoleKey.R)
			{
				return PeopleSessionResult.Restart;
			}

			if (key == ConsoleKey.Q || key == ConsoleKey.Escape)
			{
				return PeopleSessionResult.Exit;
			}
		}
	}

	public void RenderScene(
		IReadOnlyList<Colleague> colleagues,
		Colleague? activeColleague,
		QuestionItem? question,
		IReadOnlyDictionary<PeopleRole, List<bool>> roleProgress,
		PeopleGameState state,
		double movementProgress,
		AnswerSide? selectedAnswer,
		bool? wasCorrect)
	{
		Clear();
		DrawBackground();
		DrawPlayer();

		foreach (var colleague in colleagues)
		{
			var isActive = activeColleague?.Role == colleague.Role;
			var x = colleague.HomeX;
			var y = colleague.HomeY;

			if (isActive)
			{
				var targetY = PlayerY - 2;
				y = Lerp(colleague.HomeY, targetY, movementProgress);
			}

			DrawRoleMeter(colleague, roleProgress[colleague.Role]);
			DrawColleague(x, y, colleague, isActive);
		}

		if (question is not null &&
			state is PeopleGameState.QuestionActive)
		{
			DrawDialog(question, selectedAnswer, wasCorrect);
		}
		else if (state == PeopleGameState.AnswerResolution && activeColleague is not null)
		{
			DrawAnswerToast(activeColleague, wasCorrect == true);
		}
		else if (state == PeopleGameState.Victory)
		{
			DrawBigMessage("Congratulations", ConsoleColor.Green);
			WriteText(Width / 2 - 15, Height / 2 + 4, "Press R to restart or Q to quit", ConsoleColor.White);
		}
		else if (state == PeopleGameState.Defeat)
		{
			DrawBigMessage("You can do it next time", ConsoleColor.Red);
			WriteText(Width / 2 - 15, Height / 2 + 4, "Press R to restart or Q to quit", ConsoleColor.White);
		}

		Present();
	}

	public static IReadOnlyList<Colleague> CreateColleagues()
	{
		return
		[
			new Colleague { Role = PeopleRole.Sales, Label = "Sales", HomeX = 16, HomeY = 5, Color = ConsoleColor.Cyan },
			new Colleague { Role = PeopleRole.Service, Label = "Service", HomeX = 34, HomeY = 7, Color = ConsoleColor.Green },
			new Colleague { Role = PeopleRole.Marketing, Label = "Marketing", HomeX = 62, HomeY = 7, Color = ConsoleColor.Magenta },
			new Colleague { Role = PeopleRole.Studio, Label = "Studio", HomeX = 80, HomeY = 5, Color = ConsoleColor.Yellow }
		];
	}

	private void Clear()
	{
		for (var y = 0; y < Height; y++)
		{
			for (var x = 0; x < Width; x++)
			{
				_canvas[y, x] = ' ';
				_colors[y, x] = null;
			}
		}
	}

	private void DrawBackground()
	{
		WriteText(2, 0, "CREATIO TWIN'S QUIZ", ConsoleColor.Cyan);

		for (var x = 0; x < Width; x++)
		{
			Plot(x, Height - 3, '=', ConsoleColor.DarkGray);
		}
	}

	private void DrawPlayer()
	{
		WriteText(PlayerX - 5, PlayerY - 3, "Andry Baker", ConsoleColor.Cyan);
		WriteText(PlayerX - 2, PlayerY - 1, "/^^\\", ConsoleColor.White);
		WriteText(PlayerX - 2, PlayerY, "|AB|", ConsoleColor.Cyan);
		WriteText(PlayerX - 2, PlayerY + 1, "/==\\", ConsoleColor.White);
	}

	private void DrawColleague(int x, int y, Colleague colleague, bool active)
	{
		var color = active ? ConsoleColor.White : colleague.Color;
		WriteText(x - 3, y - 2, colleague.Label, colleague.Color);
		WriteText(x - 2, y - 1, " .-. ", color);
		WriteText(x - 2, y, " ( ) ", color);
		WriteText(x - 2, y + 1, " /|\\ ", color);
		WriteText(x - 2, y + 2, " / \\ ", color);
	}

	private void DrawRoleMeter(Colleague colleague, IReadOnlyList<bool> progress)
	{
		const int slots = 10;
		var x = colleague.HomeX - 5;
		var y = colleague.HomeY - 4;

		Plot(x - 1, y, '[', ConsoleColor.Gray);
		Plot(x + slots, y, ']', ConsoleColor.Gray);

		for (var i = 0; i < slots; i++)
		{
			if (i < progress.Count)
			{
				Plot(x + i, y, '#', progress[i] ? ConsoleColor.Green : ConsoleColor.Red);
			}
			else
			{
				Plot(x + i, y, '-', ConsoleColor.DarkGray);
			}
		}
	}

	private void DrawDialog(QuestionItem question, AnswerSide? selectedAnswer, bool? wasCorrect)
	{
		DrawBox(8, DialogTop, Width - 16, DialogHeight, ConsoleColor.DarkBlue);
		WriteText(12, DialogTop + 1, $"{question.Role} asks:", GetRoleColor(question.Role));
		WriteWrappedText(12, DialogTop + 3, Width - 24, question.Question, ConsoleColor.White);

		var leftColor = selectedAnswer == AnswerSide.Left
			? wasCorrect == true ? ConsoleColor.Green : ConsoleColor.Red
			: ConsoleColor.Cyan;
		var rightColor = selectedAnswer == AnswerSide.Right
			? wasCorrect == true ? ConsoleColor.Green : ConsoleColor.Red
			: ConsoleColor.Magenta;

		WriteWrappedText(12, DialogTop + 6, 34, "<- " + question.LeftAnswer, leftColor);
		WriteWrappedText(50, DialogTop + 6, 34, "-> " + question.RightAnswer, rightColor);

		if (wasCorrect.HasValue)
		{
			WriteText(12, DialogTop + 9, wasCorrect.Value ? "Correct answer." : "Wrong answer.", wasCorrect.Value ? ConsoleColor.Green : ConsoleColor.Red);
		}
		else
		{
			WriteText(12, DialogTop + 9, "Choose with LEFT or RIGHT.", ConsoleColor.DarkYellow);
		}
	}

	private void DrawAnswerToast(Colleague colleague, bool correct)
	{
		var text = correct ? "Correct!" : "Wrong!";
		var x = colleague.HomeX - 4;
		var y = PlayerY - 9;
		WriteText(x, y, text, correct ? ConsoleColor.Green : ConsoleColor.Red);
	}

	private void DrawBigMessage(string message, ConsoleColor color)
	{
		// ASCII art for victory and defeat messages
		string[] asciiArt;
		
		if (message == "Congratulations")
		{
			asciiArt = new[]
			{
				" ██████╗ ██████╗ ███╗   ██╗ ██████╗ ██████╗  █████╗ ████████╗███████╗",
				"██╔════╝██╔═══██╗████╗  ██║██╔════╝ ██╔══██╗██╔══██╗╚══██╔══╝██╔════╝",
				"██║     ██║   ██║██╔██╗ ██║██║  ███╗██████╔╝███████║   ██║   ███████╗",
				"██║     ██║   ██║██║╚██╗██║██║   ██║██╔══██╗██╔══██║   ██║   ╚════██║",
				"╚██████╗╚██████╔╝██║ ╚████║╚██████╔╝██║  ██║██║  ██║   ██║   ███████║",
				" ╚═════╝ ╚═════╝ ╚═╝  ╚═══╝ ╚═════╝ ╚═╝  ╚═╝╚═╝  ╚═╝   ╚═╝   ╚══════╝"
			};
		}
		else // "You can do it next time"
		{
			asciiArt = new[]
			{
				"████████╗██████╗ ██╗   ██╗     █████╗  ██████╗  █████╗ ██╗███╗   ██╗",
				"╚══██╔══╝██╔══██╗╚██╗ ██╔╝    ██╔══██╗██╔════╝ ██╔══██╗██║████╗  ██║",
				"   ██║   ██████╔╝ ╚████╔╝     ███████║██║  ███╗███████║██║██╔██╗ ██║",
				"   ██║   ██╔══██╗  ╚██╔╝      ██╔══██║██║   ██║██╔══██║██║██║╚██╗██║",
				"   ██║   ██║  ██║   ██║       ██║  ██║╚██████╔╝██║  ██║██║██║ ╚████║",
				"   ╚═╝   ╚═╝  ╚═╝   ╚═╝       ╚═╝  ╚═╝ ╚═════╝ ╚═╝  ╚═╝╚═╝╚═╝  ╚═══╝"
			};
		}

		// Calculate dimensions
		var artWidth = asciiArt[0].Length;
		var artHeight = asciiArt.Length;
		var boxWidth = Math.Min(artWidth + 4, Width - 2);
		var boxHeight = artHeight + 4;
		var boxX = (Width - boxWidth) / 2;
		var boxY = (Height - boxHeight) / 2;

		// Draw background box with border
		for (var y = boxY; y < boxY + boxHeight; y++)
		{
			for (var x = boxX; x < boxX + boxWidth; x++)
			{
				if (y == boxY || y == boxY + boxHeight - 1 ||
					x == boxX || x == boxX + boxWidth - 1)
				{
					Plot(x, y, '█', ConsoleColor.DarkGray);
				}
				else
				{
					Plot(x, y, ' ', ConsoleColor.Black);
				}
			}
		}

		// Draw ASCII art in the center
		var artX = (Width - artWidth) / 2;
		var artY = boxY + 2;
		
		for (var i = 0; i < asciiArt.Length; i++)
		{
			WriteText(artX, artY + i, asciiArt[i], color);
		}
	}

	private void DrawBox(int x, int y, int width, int height, ConsoleColor borderColor)
	{
		for (var ix = x; ix < x + width; ix++)
		{
			Plot(ix, y, '-', borderColor);
			Plot(ix, y + height - 1, '-', borderColor);
		}

		for (var iy = y; iy < y + height; iy++)
		{
			Plot(x, iy, '|', borderColor);
			Plot(x + width - 1, iy, '|', borderColor);
		}

		Plot(x, y, '+', borderColor);
		Plot(x + width - 1, y, '+', borderColor);
		Plot(x, y + height - 1, '+', borderColor);
		Plot(x + width - 1, y + height - 1, '+', borderColor);
	}

	private void WriteWrappedText(int x, int y, int maxWidth, string text, ConsoleColor color)
	{
		var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		var line = string.Empty;
		var lineOffset = 0;

		foreach (var word in words)
		{
			var next = string.IsNullOrEmpty(line) ? word : $"{line} {word}";
			if (next.Length <= maxWidth)
			{
				line = next;
				continue;
			}

			WriteText(x, y + lineOffset, line, color);
			line = word;
			lineOffset++;
		}

		if (!string.IsNullOrEmpty(line))
		{
			WriteText(x, y + lineOffset, line, color);
		}
	}

	private void Plot(int x, int y, char symbol, ConsoleColor color)
	{
		if (x < 0 || y < 0 || x >= Width || y >= Height)
		{
			return;
		}

		_canvas[y, x] = symbol;
		_colors[y, x] = color;
	}

	private void WriteText(int x, int y, string text, ConsoleColor color)
	{
		if (y < 0 || y >= Height)
		{
			return;
		}

		for (var i = 0; i < text.Length; i++)
		{
			var px = x + i;
			if (px < 0 || px >= Width)
			{
				continue;
			}

			Plot(px, y, text[i], color);
		}
	}

	private void Present()
	{
		for (var y = 0; y < Height; y++)
		{
			for (var x = 0; x < Width; x++)
			{
				if (_hasPreviousFrame &&
					_previousCanvas[y, x] == _canvas[y, x] &&
					_previousColors[y, x] == _colors[y, x])
				{
					continue;
				}

				Console.SetCursorPosition(x, y);
				Console.ForegroundColor = _colors[y, x] ?? ConsoleColor.Gray;
				Console.Write(_canvas[y, x]);
				_previousCanvas[y, x] = _canvas[y, x];
				_previousColors[y, x] = _colors[y, x];
			}
		}

		Console.ResetColor();
		_hasPreviousFrame = true;
	}

	private static ConsoleColor GetRoleColor(PeopleRole role)
	{
		return role switch
		{
			PeopleRole.Sales => ConsoleColor.Cyan,
			PeopleRole.Service => ConsoleColor.Green,
			PeopleRole.Marketing => ConsoleColor.Magenta,
			PeopleRole.Studio => ConsoleColor.Yellow,
			_ => ConsoleColor.Gray
		};
	}

	private static int Lerp(int from, int to, double progress)
	{
		progress = Math.Clamp(progress, 0, 1);
		return (int)Math.Round(from + ((to - from) * progress));
	}

	private void ResetBackBuffer()
	{
		Array.Clear(_previousCanvas);
		Array.Clear(_previousColors);
		_hasPreviousFrame = false;
	}
}
