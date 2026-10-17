using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Zaldaryon.Pharos.Analyzers;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(RequireOptimumCodeFixProvider)), Shared]
public sealed class RequireOptimumCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(DiagnosticIds.RequireOptimumOnPlainFact);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null) return;

        var diagnostic = context.Diagnostics[0];
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        var node = root.FindNode(diagnosticSpan);
        var method = node.FirstAncestorOrSelf<MethodDeclarationSyntax>();
        if (method == null) return;

        // Code fix 1: Replace [Fact] with [ClientScenario]
        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Replace '[Fact]' with '[ClientScenario]'",
                createChangedDocument: c => ReplaceFactWithClientScenarioAsync(context.Document, root, method, c),
                equivalenceKey: "ReplaceFactWithClientScenario"),
            diagnostic);

        // Code fix 2: Remove [RequireOptimum]
        var attr = node.FirstAncestorOrSelf<AttributeSyntax>();
        if (attr != null)
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: "Remove '[RequireOptimum]'",
                    createChangedDocument: c => RemoveAttributeAsync(context.Document, root, attr, c),
                    equivalenceKey: "RemoveRequireOptimum"),
                diagnostic);
        }
    }

    private static Task<Document> ReplaceFactWithClientScenarioAsync(
        Document document,
        SyntaxNode root,
        MethodDeclarationSyntax method,
        CancellationToken cancellationToken)
    {
        var newAttributeLists = new SyntaxList<AttributeListSyntax>();

        foreach (var attrList in method.AttributeLists)
        {
            var newAttributes = new SeparatedSyntaxList<AttributeSyntax>();
            foreach (var attr in attrList.Attributes)
            {
                if (attr.Name.ToString() is "Fact" or "FactAttribute")
                {
                    newAttributes = newAttributes.Add(SyntaxFactory.Attribute(SyntaxFactory.IdentifierName("ClientScenario")));
                }
                else
                {
                    newAttributes = newAttributes.Add(attr);
                }
            }
            newAttributeLists = newAttributeLists.Add(attrList.WithAttributes(newAttributes));
        }

        var newMethod = method.WithAttributeLists(newAttributeLists);
        var newRoot = root.ReplaceNode(method, newMethod);
        return Task.FromResult(document.WithSyntaxRoot(newRoot));
    }

    private static Task<Document> RemoveAttributeAsync(
        Document document,
        SyntaxNode root,
        AttributeSyntax attr,
        CancellationToken cancellationToken)
    {
        SyntaxNode newRoot;
        if (attr.Parent is AttributeListSyntax attrList && attrList.Attributes.Count == 1)
        {
            newRoot = root.RemoveNode(attrList, SyntaxRemoveOptions.KeepNoTrivia)!;
        }
        else
        {
            newRoot = root.RemoveNode(attr, SyntaxRemoveOptions.KeepNoTrivia)!;
        }

        return Task.FromResult(document.WithSyntaxRoot(newRoot));
    }
}
