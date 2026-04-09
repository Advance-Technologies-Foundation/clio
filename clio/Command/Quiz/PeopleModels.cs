using System;

namespace Clio.Command.Quiz;

internal enum PeopleRole
{
	Sales,
	Service,
	Marketing,
	Studio
}

internal enum AnswerSide
{
	Left,
	Right
}

internal enum PeopleGameState
{
	Idle,
	ColleagueApproaching,
	QuestionActive,
	AnswerResolution,
	ColleagueReturning,
	Victory,
	Defeat,
	Exit
}

internal enum PeopleSessionResult
{
	Restart,
	Exit
}

internal sealed class Colleague
{
	public required PeopleRole Role { get; init; }

	public required string Label { get; init; }

	public required int HomeX { get; init; }

	public required int HomeY { get; init; }

	public required ConsoleColor Color { get; init; }
}

internal sealed class QuestionItem
{
	public required PeopleRole Role { get; init; }

	public required string Question { get; init; }

	public required string LeftAnswer { get; init; }

	public required string RightAnswer { get; init; }

	public required AnswerSide CorrectSide { get; init; }
}
