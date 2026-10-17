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

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ScenarioBaseInheritanceCodeFixProvider)), Shared]
public sealed class ScenarioBaseInheritanceCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(DiagnosticIds.ScenarioAttributeWithoutBaseClass);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null) return;

        var diagnostic = context.Diagnostics[0];
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        var node = root.FindNode(diagnosticSpan);
        var classDecl = node.FirstAncestorOrSelf<ClassDeclarationSyntax>();
        if (classDecl == null) return;

        string message = diagnostic.GetMessage();
        // Format: Class 'X' uses scenario attribute 'Y' but does not inherit from 'Z'
        int lastFrom = message.LastIndexOf("from '", System.StringComparison.Ordinal);
        if (lastFrom < 0) return;

        string requiredBase = message.Substring(lastFrom + 6).TrimEnd('\'');

        context.RegisterCodeFix(
            CodeAction.Create(
                title: $"Inherit from '{requiredBase}'",
                createChangedDocument: c => AddBaseClassAsync(context.Document, root, classDecl, requiredBase, c),
                equivalenceKey: $"Inherit_{requiredBase}"),
            diagnostic);
    }

    private static Task<Document> AddBaseClassAsync(
        Document document,
        SyntaxNode root,
        ClassDeclarationSyntax classDecl,
        string baseClassName,
        CancellationToken cancellationToken)
    {
        var baseType = SyntaxFactory.SimpleBaseType(SyntaxFactory.IdentifierName(baseClassName));
        ClassDeclarationSyntax newClassDecl;

        if (classDecl.BaseList == null)
        {
            newClassDecl = classDecl.WithBaseList(
                SyntaxFactory.BaseList(SyntaxFactory.SingletonSeparatedList<BaseTypeSyntax>(baseType)));
        }
        else
        {
            // Insert at beginning of base list
            var newTypes = classDecl.BaseList.Types.Insert(0, baseType);
            newClassDecl = classDecl.WithBaseList(classDecl.BaseList.WithTypes(newTypes));
        }

        var newRoot = root.ReplaceNode(classDecl, newClassDecl);
        return Task.FromResult(document.WithSyntaxRoot(newRoot));
    }
}
