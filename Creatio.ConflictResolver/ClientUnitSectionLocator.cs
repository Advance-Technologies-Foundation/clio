using Acornima;
using Acornima.Ast;

namespace Creatio.ConflictResolver;

internal static class ClientUnitSectionLocator
{
	public static bool TryExtract(string source, string marker, out ClientUnitSectionSlice slice)
	{
		slice = default;
		var token = $"/**{marker}*/";
		var first = source.IndexOf(token, StringComparison.Ordinal);
		if (first < 0)
		{
			return false;
		}

		var second = source.IndexOf(token, first + token.Length, StringComparison.Ordinal);
		if (second < 0)
		{
			return false;
		}
		if (source.IndexOf(token, second + token.Length, StringComparison.Ordinal) >= 0)
		{
			return false;
		}

		var start = first + token.Length;
		while (start < second && char.IsWhiteSpace(source[start]))
		{
			start++;
		}

		if (start >= second || source[start] is not ('[' or '{'))
		{
			return false;
		}

		try
		{
			var expression = new Parser().ParseExpression(source, start, second - start);
			if (expression.Start != start || expression.End > second ||
				!ContainsOnlyWhitespace(source, expression.End, second) ||
				(source[start] == '[' && expression is not ArrayExpression) ||
				(source[start] == '{' && expression is not ObjectExpression))
			{
				return false;
			}

			var length = expression.End - start;
			slice = new ClientUnitSectionSlice(start, length, source.Substring(start, length));
			return true;
		}
		catch (SyntaxErrorException)
		{
			return false;
		}
		catch (InsufficientExecutionStackException)
		{
			return false;
		}
	}

	private static bool ContainsOnlyWhitespace(string source, int start, int end)
	{
		for (var index = start; index < end; index++)
		{
			if (!char.IsWhiteSpace(source[index]))
			{
				return false;
			}
		}

		return true;
	}
}

internal readonly record struct ClientUnitSectionSlice(int Start, int Length, string Json);
