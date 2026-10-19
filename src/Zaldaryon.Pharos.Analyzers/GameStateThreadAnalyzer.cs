using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Zaldaryon.Pharos.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class GameStateThreadAnalyzer : DiagnosticAnalyzer
{
    private static readonly LocalizableString Title = "Game state accessed outside game thread";
    private static readonly LocalizableString MessageFormat = "'{0}' is accessed outside the {1} thread; wrap it in {2}";
    private static readonly LocalizableString Description = "Vintage Story client and server game state must be accessed from their respective game threads using RunOnClientThread or RunOnGameThread.";
    private const string Category = "Usage";

    public static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.GameStateAccessedOutsideGameThread,
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

        context.RegisterSyntaxNodeAction(AnalyzeMemberAccess, SyntaxKind.SimpleMemberAccessExpression);
    }

    private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
    {
        var memberAccess = (MemberAccessExpressionSyntax)context.Node;
        string memberName = memberAccess.Name.Identifier.ValueText;

        // Check if accessing World
        if (memberName != "World")
        {
            return;
        }

        string fullAccess = memberAccess.ToString();
        // Target patterns like Client.Client.World, Client.World, client.World, Server.Api.World, Api.World
        bool isClientAccess = fullAccess.IndexOf("Client", StringComparison.OrdinalIgnoreCase) >= 0;
        bool isServerAccess = fullAccess.IndexOf("Server", StringComparison.OrdinalIgnoreCase) >= 0 ||
                              fullAccess.IndexOf("Api", StringComparison.OrdinalIgnoreCase) >= 0;

        if (!isClientAccess && !isServerAccess)
        {
            return;
        }

        // Check if inside a lambda passed to RunOnClientThread / RunOnGameThread / OnClientThread / OnGameThread
        if (IsInsideGameThreadRunner(memberAccess, out string? runnerName))
        {
            return;
        }

        string threadName = isClientAccess ? "client" : "server";
        string wrapMethod = isClientAccess ? "RunOnClientThread" : "RunOnGameThread";

        var diagnostic = Diagnostic.Create(Rule, memberAccess.GetLocation(), fullAccess, threadName, wrapMethod);
        context.ReportDiagnostic(diagnostic);
    }

    private static bool IsInsideGameThreadRunner(SyntaxNode node, out string? runnerName)
    {
        runnerName = null;
        for (SyntaxNode? current = node.Parent; current != null; current = current.Parent)
        {
            if (current is LambdaExpressionSyntax or AnonymousMethodExpressionSyntax)
            {
                // Check the containing invocation
                if (current.Parent is ArgumentSyntax argument &&
                    argument.Parent is ArgumentListSyntax argList &&
                    argList.Parent is InvocationExpressionSyntax invocation)
                {
                    string invokedName = GetInvokedMethodName(invocation);
                    if (invokedName is "RunOnClientThread" or "RunOnGameThread" or "OnClientThread" or "OnGameThread")
                    {
                        runnerName = invokedName;
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static string GetInvokedMethodName(InvocationExpressionSyntax invocation)
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
