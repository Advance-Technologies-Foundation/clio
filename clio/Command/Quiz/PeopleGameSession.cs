using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Clio.Command.Quiz;

internal sealed class PeopleGameSession
{
	private const int MeterTarget = 10;
	private const int FrameDelayMs = 55;
	private const int ApproachFrames = 10;
	private const int ResolutionFrames = 16;

	private readonly QuestionRepository _repository;
	private readonly PeopleRenderer _renderer;
	private readonly Random _random = new();
	private readonly Dictionary<PeopleRole, HashSet<QuestionItem>> _usedQuestions = new();
	private readonly Dictionary<PeopleRole, List<bool>> _roleProgress = new();
	private readonly IReadOnlyList<Colleague> _colleagues;

	private PeopleGameState _state = PeopleGameState.Idle;
	private Colleague? _activeColleague;
	private QuestionItem? _activeQuestion;
	private AnswerSide _selectedAnswer;
	private bool _lastAnswerCorrect;
	private int _phaseFrame;
	private int _correctCount;
	private int _wrongCount;
	private PeopleRole? _lastColleagueRole;

	public PeopleGameSession(QuestionRepository repository, PeopleRenderer renderer)
	{
		_repository = repository;
		_renderer = renderer;
		_colleagues = PeopleRenderer.CreateColleagues();

		foreach (var role in Enum.GetValues<PeopleRole>())
		{
			_usedQuestions[role] = [];
			_roleProgress[role] = [];
		}
	}

	public PeopleSessionResult Run()
	{
		while (true)
		{
			switch (_state)
			{
				case PeopleGameState.Idle:
					StartNextQuestion();
					break;

				case PeopleGameState.ColleagueApproaching:
					if (RunApproachFrame())
					{
						return PeopleSessionResult.Exit;
					}

					break;

				case PeopleGameState.QuestionActive:
					var questionResult = RunQuestionState();
					if (questionResult == PeopleSessionResult.Exit)
					{
						return PeopleSessionResult.Exit;
					}

					break;

				case PeopleGameState.AnswerResolution:
					RunResolutionFrame();
					break;

				case PeopleGameState.ColleagueReturning:
					RunReturnFrame();
					break;

				case PeopleGameState.Victory:
				case PeopleGameState.Defeat:
					return RunEndGameState();

				default:
					return PeopleSessionResult.Exit;
			}
		}
	}

	private void StartNextQuestion()
	{
		var pool = _colleagues
			.Where(colleague => colleague.Role != _lastColleagueRole || _colleagues.Count == 1)
			.ToList();

		_activeColleague = pool[_random.Next(pool.Count)];
		_lastColleagueRole = _activeColleague.Role;
		_activeQuestion = _repository.TakeRandomQuestion(_activeColleague.Role, _usedQuestions[_activeColleague.Role], _random);
		_phaseFrame = 0;
		_state = PeopleGameState.ColleagueApproaching;
	}

	private bool RunApproachFrame()
	{
		if (ReadNonBlockingKey() == ConsoleKey.Escape)
		{
			return true;
		}

		_phaseFrame++;
		_renderer.RenderScene(
			_colleagues,
			_activeColleague,
			_activeQuestion,
			_roleProgress,
			PeopleGameState.ColleagueApproaching,
			_phaseFrame / (double)ApproachFrames,
			null,
			null);

		Thread.Sleep(FrameDelayMs);

		if (_phaseFrame >= ApproachFrames)
		{
			_state = PeopleGameState.QuestionActive;
		}

		return false;
	}

	private PeopleSessionResult RunQuestionState()
	{
		while (true)
		{
			_renderer.RenderScene(
				_colleagues,
				_activeColleague,
				_activeQuestion,
				_roleProgress,
				PeopleGameState.QuestionActive,
				1,
				null,
				null);

			var key = Console.ReadKey(true).Key;
			if (key == ConsoleKey.Escape)
			{
				return PeopleSessionResult.Exit;
			}

			if (key != ConsoleKey.LeftArrow && key != ConsoleKey.RightArrow)
			{
				continue;
			}

			_selectedAnswer = key == ConsoleKey.LeftArrow ? AnswerSide.Left : AnswerSide.Right;
			_lastAnswerCorrect = _selectedAnswer == _activeQuestion!.CorrectSide;

			if (_lastAnswerCorrect)
			{
				_correctCount++;
			}
			else
			{
				_wrongCount++;
			}

			_roleProgress[_activeColleague!.Role].Add(_lastAnswerCorrect);

			_phaseFrame = 0;
			_state = PeopleGameState.AnswerResolution;
			return PeopleSessionResult.Restart;
		}
	}

	private void RunResolutionFrame()
	{
		_phaseFrame++;
		_renderer.RenderScene(
			_colleagues,
			_activeColleague,
			_activeQuestion,
			_roleProgress,
			PeopleGameState.AnswerResolution,
			1,
			_selectedAnswer,
			_lastAnswerCorrect);

		Thread.Sleep(FrameDelayMs);

		if (_phaseFrame < ResolutionFrames)
		{
			return;
		}

		if (_correctCount >= MeterTarget)
		{
			_state = PeopleGameState.Victory;
			return;
		}

		if (_wrongCount >= MeterTarget)
		{
			_state = PeopleGameState.Defeat;
			return;
		}

		_phaseFrame = 0;
		_state = PeopleGameState.ColleagueReturning;
	}

	private void RunReturnFrame()
	{
		_phaseFrame++;
		var progress = 1 - (_phaseFrame / (double)ApproachFrames);

		_renderer.RenderScene(
			_colleagues,
			_activeColleague,
			_activeQuestion,
			_roleProgress,
			PeopleGameState.ColleagueReturning,
			progress,
			null,
			null);

		Thread.Sleep(FrameDelayMs);

		if (_phaseFrame >= ApproachFrames)
		{
			_activeColleague = null;
			_activeQuestion = null;
			_state = PeopleGameState.Idle;
		}
	}

	private PeopleSessionResult RunEndGameState()
	{
		while (true)
		{
			_renderer.RenderScene(
				_colleagues,
				null,
				null,
				_roleProgress,
				_state,
				0,
				null,
				null);

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

	private static ConsoleKey? ReadNonBlockingKey()
	{
		if (!Console.KeyAvailable)
		{
			return null;
		}

		return Console.ReadKey(true).Key;
	}
}
