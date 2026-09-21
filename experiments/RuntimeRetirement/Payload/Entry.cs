using System.Runtime.CompilerServices;
namespace ProbePayload;
/// <summary>Separates entry activation from lazy private dependency use.</summary>
public static class Entry {
    public static string Start() => "entry-ok";
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static string Finish() => ProbeDependency.Value.Read();
}
