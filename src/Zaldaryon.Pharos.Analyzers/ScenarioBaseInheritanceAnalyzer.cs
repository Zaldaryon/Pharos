using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Zaldaryon.Pharos.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ScenarioBaseInheritanceAnalyzer : DiagnosticAnalyzer
{
    private static readonly LocalizableString Title = "Scenario attribute on class without scenario base";
    private static readonly LocalizableString MessageFormat = "Class '{0}' uses scenario attribute '{1}' but does not inherit from '{2}'";
    private static readonly LocalizableString Description = "Test classes containing Pharos scenario test methods must derive from the corresponding scenario base class.";
    private const string Category = "Usage";

    public static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.ScenarioAttributeWithoutBaseClass,
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
        if (method.Parent is not ClassDeclarationSyntax classDecl)
        {
            return;
        }

        foreach (var attrList in method.AttributeLists)
        {
            foreach (var attr in attrList.Attributes)
            {
                string attrName = attr.Name.ToString();

                string? requiredBase = null;
                string? canonicalAttr = null;

                if (attrName is "ClientServerScenario" or "ClientServerScenarioAttribute" or
                                "ClientServerTheory" or "ClientServerTheoryAttribute")
                {
                    canonicalAttr = attrName.Replace("Attribute", "");
                    requiredBase = "ClientServerScenarioBase";
                }
                else if (attrName is "ClientScenario" or "ClientScenarioAttribute" or
                                     "ClientTheory" or "ClientTheoryAttribute")
                {
                    canonicalAttr = attrName.Replace("Attribute", "");
                    requiredBase = "ClientScenarioBase";
                }
                else if (attrName is "ServerScenario" or "ServerScenarioAttribute" or
                                     "ServerTheory" or "ServerTheoryAttribute" or
                                     "ContentSweep" or "ContentSweepAttribute")
                {
                    canonicalAttr = attrName.Replace("Attribute", "");
                    requiredBase = "ServerScenarioBase";
                }

                if (requiredBase != null && canonicalAttr != null)
                {
                    if (!InheritsFrom(classDecl, requiredBase))
                    {
                        var diagnostic = Diagnostic.Create(
                            Rule,
                            attr.GetLocation(),
                            classDecl.Identifier.ValueText,
                            canonicalAttr,
                            requiredBase);
                        context.ReportDiagnostic(diagnostic);
                    }
                }
            }
        }
    }

    private static bool InheritsFrom(ClassDeclarationSyntax classDecl, string expectedBase)
    {
        if (classDecl.BaseList == null)
        {
            return false;
        }

        foreach (var baseType in classDecl.BaseList.Types)
        {
            string name = baseType.Type.ToString();
            if (name == expectedBase)
            {
                return true;
            }

            // ClientServerScenarioBase satisfies ClientScenarioBase and ServerScenarioBase requirements
            if (name == "ClientServerScenarioBase" &&
                (expectedBase == "ClientScenarioBase" || expectedBase == "ServerScenarioBase"))
            {
                return true;
            }

            // ModSmokeTest and ModSmokeTestBase derive from ClientServerScenarioBase
            if (name is "ModSmokeTest" or "ModSmokeTestBase")
            {
                return true;
            }
        }

        return false;
    }
}
