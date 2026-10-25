namespace Zaldaryon.Pharos.Text;

/// <summary>Edge-case text strings for use in <c>[ClientTheory]</c> rows.</summary>
public static class TextCorpus
{
    /// <summary>Strings covering emoji, combining marks, ZWJ, CJK, Thai, Arabic, Hebrew, mixed-direction and long words.</summary>
    public static IEnumerable<object[]> EdgeCases()
    {
        yield return new object[] { "Hello, world!" };
        yield return new object[] { "😀🎉" }; // emoji
        yield return new object[] { "e\u0301" }; // combining mark
        yield return new object[] { "👨‍👩‍👧‍👦" }; // ZWJ
        yield return new object[] { "日本語テスト" }; // CJK
        yield return new object[] { "สวัสดี" }; // Thai
        yield return new object[] { "مرحبا بالعالم" }; // Arabic
        yield return new object[] { "שלום עולם" }; // Hebrew
        yield return new object[] { "Hello مرحبا שלום" }; // mixed direction
        yield return new object[] { new string('a', 1000) }; // long word
    }
}