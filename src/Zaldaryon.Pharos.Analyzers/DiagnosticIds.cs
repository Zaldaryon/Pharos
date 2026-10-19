namespace Zaldaryon.Pharos.Analyzers;

public static class DiagnosticIds
{
    public const string GameStateAccessedOutsideGameThread = "PHAROS001";
    public const string UnawaitedScenarioStep = "PHAROS002";
    public const string WallClockWaitInScenario = "PHAROS003";
    public const string ScenarioAttributeWithoutBaseClass = "PHAROS004";
    public const string RequireOptimumOnPlainFact = "PHAROS005";
}
