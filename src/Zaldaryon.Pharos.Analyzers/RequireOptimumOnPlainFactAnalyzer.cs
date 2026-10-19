using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Zaldaryon.Pharos.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RequireOptimumOnPlainFactAnalyzer : DiagnosticAnalyzer
{
    private static readonly LocalizableString Title = "RequireOptimum attribute has no effect on plain test attribute";
    private static readonly LocalizableString MessageFormat = "'[RequireOptimum]' on '{0}' has no effect because it is not a Pharos scenario test";
    private static readonly LocalizableString Description = "[RequireOptimum] only gates Pharos scenario tests with headless client fixtures. Plain xUnit [Fact] or [Theory] tests do not inspect or initialize Optimum.";
    private const string Category = "Usage";

    public static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.RequireOptimumOnPlainFact,
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

        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
    }

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext context)
    {
        var method = (MethodDeclarationSyntax)context.Node;

        AttributeSyntax? requireOptimumAttr = null;
        bool hasPlainFactOrTheory = false;
        bool hasPharosScenario = false;

        foreach (var attrList in method.AttributeLists)
        {
            foreach (var attr in attrList.Attributes)
            {
                string name = attr.Name.ToString();
                if (name is "RequireOptimum" or "RequireOptimumAttribute")
                {
                    requireOptimumAttr = attr;
                }
                else if (name is "Fact" or "FactAttribute" or "Theory" or "TheoryAttribute")
                {
                    hasPlainFactOrTheory = true;
                }
                else if (name.Contains("Scenario") || name.Contains("ClientTheory") || name.Contains("ServerTheory"))
                {
                    hasPharosScenario = true;
                }
            }
        }

        if (requireOptimumAttr == null)
        {
            return;
        }

        // If it has a Pharos scenario attribute, it is valid
        if (hasPharosScenario)
        {
            return;
        }

        // Check if class derives from a scenario base
        if (method.Parent is ClassDeclarationSyntax classDecl && classDecl.BaseList != null)
        {
            foreach (var baseType in classDecl.BaseList.Types)
            {
                string baseName = baseType.Type.ToString();
                if (baseName.Contains("ScenarioBase"))
                {
                    return;
                }
            }
        }

        if (hasPlainFactOrTheory)
        {
            var diagnostic = Diagnostic.Create(Rule, requireOptimumAttr.GetLocation(), method.Identifier.ValueText);
            context.ReportDiagnostic(diagnostic);
        }
    }
}
