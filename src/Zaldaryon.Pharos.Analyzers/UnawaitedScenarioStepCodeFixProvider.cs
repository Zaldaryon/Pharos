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

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UnawaitedScenarioStepCodeFixProvider)), Shared]
public sealed class UnawaitedScenarioStepCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(DiagnosticIds.UnawaitedScenarioStep);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null) return;

        var diagnostic = context.Diagnostics[0];
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        var node = root.FindNode(diagnosticSpan);
        if (node is InvocationExpressionSyntax invocation &&
            invocation.Parent is ExpressionStatementSyntax statement)
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: "Add 'await'",
                    createChangedDocument: c => AddAwaitAsync(context.Document, root, statement, invocation, c),
                    equivalenceKey: "AddAwait"),
                diagnostic);
        }
    }

    private static Task<Document> AddAwaitAsync(
        Document document,
        SyntaxNode root,
        ExpressionStatementSyntax statement,
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken)
    {
        var awaitExpression = SyntaxFactory.AwaitExpression(invocation)
            .WithLeadingTrivia(invocation.GetLeadingTrivia())
            .WithTrailingTrivia(invocation.GetTrailingTrivia());

        var newStatement = statement.WithExpression(awaitExpression);
        var newRoot = root.ReplaceNode(statement, newStatement);

        // Also check if containing method needs 'async' modifier
        var method = newRoot.FindNode(statement.Span).FirstAncestorOrSelf<MethodDeclarationSyntax>();
        if (method != null && !method.Modifiers.Any(SyntaxKind.AsyncKeyword))
        {
            var asyncModifier = SyntaxFactory.Token(SyntaxKind.AsyncKeyword).WithTrailingTrivia(SyntaxFactory.Space);
            var newModifiers = method.Modifiers.Add(asyncModifier);

            TypeSyntax returnType = method.ReturnType;
            if (returnType is PredefinedTypeSyntax predefined && predefined.Keyword.IsKind(SyntaxKind.VoidKeyword))
            {
                returnType = SyntaxFactory.IdentifierName("Task").WithTrailingTrivia(SyntaxFactory.Space);
            }

            var updatedMethod = method.WithModifiers(newModifiers).WithReturnType(returnType);
            newRoot = newRoot.ReplaceNode(method, updatedMethod);
        }

        return Task.FromResult(document.WithSyntaxRoot(newRoot));
    }
}
