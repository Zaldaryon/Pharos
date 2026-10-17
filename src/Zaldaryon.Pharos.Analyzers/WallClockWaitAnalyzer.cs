using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Zaldaryon.Pharos.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class WallClockWaitAnalyzer : DiagnosticAnalyzer
{
    private static readonly LocalizableString Title = "Wall-clock delay used in scenario";
    private static readonly LocalizableString MessageFormat = "'{0}' in a scenario waits on the wall clock; use StepUntilAsync or TickUntilAsync";
    private static readonly LocalizableString Description = "Scenarios must use deterministic simulation stepping instead of real-time wall-clock delays like Task.Delay or Thread.Sleep.";
    private const string Category = "Reliability";

    public static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.WallClockWaitInScenario,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: Description);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        string expressionText = invocation.Expression.ToString();

        bool isWallClock = false;
        string callName = string.Empty;

        if (expressionText.EndsWith("Task.Delay", StringComparison.Ordinal) ||
            expressionText == "Delay")
        {
            isWallClock = true;
            callName = "Task.Delay";
        }
        else if (expressionText.EndsWith("Thread.Sleep", StringComparison.Ordinal) ||
                 expressionText == "Sleep")
        {
            isWallClock = true;
            callName = "Thread.Sleep";
        }

        if (!isWallClock)
        {
            return;
        }

        // Check if inside a scenario class or test method
        if (IsInsideScenarioOrTest(invocation))
        {
            var diagnostic = Diagnostic.Create(Rule, invocation.GetLocation(), callName);
            context.ReportDiagnostic(diagnostic);
        }
    }

    private static bool IsInsideScenarioOrTest(SyntaxNode node)
    {
        for (SyntaxNode? current = node.Parent; current != null; current = current.Parent)
        {
            if (current is MethodDeclarationSyntax method)
            {
                foreach (var attrList in method.AttributeLists)
                {
                    foreach (var attr in attrList.Attributes)
                    {
                        string name = attr.Name.ToString();
                        if (name.Contains("Scenario") || name.Contains("Fact") || name.Contains("Theory"))
                        {
                            return true;
                        }
                    }
                }
            }

            if (current is ClassDeclarationSyntax classDecl)
            {
                if (classDecl.BaseList != null)
                {
                    foreach (var baseType in classDecl.BaseList.Types)
                    {
                        string baseName = baseType.Type.ToString();
                        if (baseName.Contains("Scenario") || baseName.Contains("ServerScenarioBase") ||
                            baseName.Contains("ClientScenarioBase") || baseName.Contains("ClientServerScenarioBase"))
                        {
                            return true;
                        }
                    }
                }
            }
        }

        return false;
    }
}
