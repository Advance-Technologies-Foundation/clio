using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Clio.Command.Quiz;

internal sealed class QuestionRepository
{
	private readonly Dictionary<PeopleRole, List<QuestionItem>> _questionsByRole;

	private QuestionRepository(Dictionary<PeopleRole, List<QuestionItem>> questionsByRole)
	{
		_questionsByRole = questionsByRole;
	}

	public static QuestionRepository Load(string filePath)
	{
		if (!File.Exists(filePath))
		{
			throw new FileNotFoundException($"Question file not found: {filePath}");
		}

		var text = File.ReadAllText(filePath);
		var entries = text.Split(
			[Environment.NewLine + Environment.NewLine, "\n\n", "\r\n\r\n"],
			StringSplitOptions.RemoveEmptyEntries);

		var questionsByRole = Enum
			.GetValues<PeopleRole>()
			.ToDictionary(role => role, _ => new List<QuestionItem>());

		foreach (var entry in entries)
		{
			var fields = entry
				.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
				.Select(line => line.Trim())
				.Where(line => !string.IsNullOrWhiteSpace(line))
				.ToList();

			var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (var field in fields)
			{
				var separatorIndex = field.IndexOf(':');
				if (separatorIndex <= 0 || separatorIndex == field.Length - 1)
				{
					throw new InvalidDataException($"Invalid question row: {field}");
				}

				var key = field[..separatorIndex].Trim();
				var value = field[(separatorIndex + 1)..].Trim();
				map[key] = value;
			}

			var role = ParseRole(GetRequired(map, "role"));
			var correct = ParseSide(GetRequired(map, "correct"));

			var item = new QuestionItem
			{
				Role = role,
				Question = GetRequired(map, "question"),
				LeftAnswer = GetRequired(map, "left"),
				RightAnswer = GetRequired(map, "right"),
				CorrectSide = correct
			};

			questionsByRole[role].Add(item);
		}

		foreach (var (role, questions) in questionsByRole)
		{
			if (questions.Count == 0)
			{
				throw new InvalidDataException($"No questions found for role {role}.");
			}
		}

		return new QuestionRepository(questionsByRole);
	}

	public QuestionItem TakeRandomQuestion(PeopleRole role, HashSet<QuestionItem> usedQuestions, Random random)
	{
		var questions = _questionsByRole[role];
		var available = questions.Where(question => !usedQuestions.Contains(question)).ToList();
		if (available.Count == 0)
		{
			usedQuestions.Clear();
			available = questions.ToList();
		}

		var question = available[random.Next(available.Count)];
		usedQuestions.Add(question);
		return question;
	}

	private static string GetRequired(Dictionary<string, string> map, string key)
	{
		if (!map.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
		{
			throw new InvalidDataException($"Missing required field '{key}'.");
		}

		return value;
	}

	private static PeopleRole ParseRole(string value)
	{
		return value.Trim().ToLowerInvariant() switch
		{
			"sales" => PeopleRole.Sales,
			"service" => PeopleRole.Service,
			"marketing" => PeopleRole.Marketing,
			"studio" => PeopleRole.Studio,
			_ => throw new InvalidDataException($"Unknown role: {value}")
		};
	}

	private static AnswerSide ParseSide(string value)
	{
		return value.Trim().ToLowerInvariant() switch
		{
			"left" => AnswerSide.Left,
			"right" => AnswerSide.Right,
			_ => throw new InvalidDataException($"Unknown answer side: {value}")
		};
	}
}
