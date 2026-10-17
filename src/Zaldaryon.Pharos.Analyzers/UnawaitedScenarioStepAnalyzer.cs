using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Zaldaryon.Pharos.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UnawaitedScenarioStepAnalyzer : DiagnosticAnalyzer
{
    private static readonly LocalizableString Title = "Asynchronous scenario step is not awaited";
    private static readonly LocalizableString MessageFormat = "The Task returned by '{0}' is not awaited";
    private static readonly LocalizableString Description = "Asynchronous methods in scenarios advance simulation ticks or frames and must be awaited.";
    private const string Category = "Usage";

    public static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.UnawaitedScenarioStep,
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

        context.RegisterSyntaxNodeAction(AnalyzeExpressionStatement, SyntaxKind.ExpressionStatement);
    }

    private static void AnalyzeExpressionStatement(SyntaxNodeAnalysisContext context)
    {
        var statement = (ExpressionStatementSyntax)context.Node;

        // If the statement is already an await expression (e.g. await StepAsync(...)), it's safe
        if (statement.Expression is AwaitExpressionSyntax)
        {
            return;
        }

        // Direct invocation (e.g. StepAsync(...) or session.StepUntilAsync(...))
        if (statement.Expression is not InvocationExpressionSyntax invocation)
        {
            return;
        }

        string methodName = GetMethodName(invocation);
        if (string.IsNullOrEmpty(methodName))
        {
            return;
        }

        // Check if method is an asynchronous step/waiting method
        bool isTargetAsyncMethod = IsStepOrAsyncMethod(methodName);

        // Also check semantic model if available
        if (!isTargetAsyncMethod && context.SemanticModel.GetSymbolInfo(invocation).Symbol is IMethodSymbol methodSymbol)
        {
            if (methodSymbol.ReturnType is INamedTypeSymbol returnType &&
                (returnType.Name == "Task" || returnType.Name == "ValueTask"))
            {
                isTargetAsyncMethod = true;
            }
        }

        if (isTargetAsyncMethod)
        {
            var diagnostic = Diagnostic.Create(Rule, invocation.GetLocation(), methodName);
            context.ReportDiagnostic(diagnostic);
        }
    }

    private static bool IsStepOrAsyncMethod(string methodName)
    {
        if (methodName.EndsWith("Async", StringComparison.Ordinal))
        {
            return true;
        }

        return methodName is "Step" or "StepFrames" or "StepUntil" or "TickUntil";
    }

    private static string GetMethodName(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            return memberAccess.Name.Identifier.ValueText;
        }
        if (invocation.Expression is IdentifierNameSyntax identifier)
        {
            return identifier.Identifier.ValueText;
        }
        return string.Empty;
    }
}
