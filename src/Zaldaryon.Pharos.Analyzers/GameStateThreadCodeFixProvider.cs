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

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(GameStateThreadCodeFixProvider)), Shared]
public sealed class GameStateThreadCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(DiagnosticIds.GameStateAccessedOutsideGameThread);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null) return;

        var diagnostic = context.Diagnostics[0];
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        var node = root.FindNode(diagnosticSpan);
        var statement = node.FirstAncestorOrSelf<StatementSyntax>();
        if (statement is ExpressionStatementSyntax exprStatement)
        {
            string runner = diagnostic.GetMessage().Contains("RunOnClientThread")
                ? "RunOnClientThread"
                : "RunOnGameThread";

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: $"Wrap in {runner}",
                    createChangedDocument: c => WrapInRunnerAsync(context.Document, root, exprStatement, runner, c),
                    equivalenceKey: $"Wrap_{runner}"),
                diagnostic);
        }
    }

    private static Task<Document> WrapInRunnerAsync(
        Document document,
        SyntaxNode root,
        ExpressionStatementSyntax statement,
        string runner,
        CancellationToken cancellationToken)
    {
        var lambda = SyntaxFactory.ParenthesizedLambdaExpression(statement.Expression);
        var invocation = SyntaxFactory.InvocationExpression(
            SyntaxFactory.IdentifierName(runner),
            SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(lambda))));

        var newStatement = SyntaxFactory.ExpressionStatement(invocation)
            .WithLeadingTrivia(statement.GetLeadingTrivia())
            .WithTrailingTrivia(statement.GetTrailingTrivia());

        var newRoot = root.ReplaceNode(statement, newStatement);
        return Task.FromResult(document.WithSyntaxRoot(newRoot));
    }
}
