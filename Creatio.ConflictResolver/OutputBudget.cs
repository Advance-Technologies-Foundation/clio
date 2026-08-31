using System.Text;

namespace Creatio.ConflictResolver;

internal static class OutputBudget
{
	public const int MaximumBytes = 4 * 1024 * 1024;

	public static void EnsureIndentedTextFits(
		string normalizedText,
		string outputNewline,
		int newlineCount,
		string repeatedIndent,
		int indentCount)
	{
		long projectedBytes = Encoding.UTF8.GetByteCount(normalizedText);
		projectedBytes += (long)newlineCount * (Encoding.UTF8.GetByteCount(outputNewline) - 1);
		projectedBytes += (long)indentCount * Encoding.UTF8.GetByteCount(repeatedIndent);
		if (projectedBytes > MaximumBytes)
		{
			throw new MergeOutputLimitExceededException();
		}
	}
}
