using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Zaldaryon.Pharos.Docs.Tests;

/// <summary>
/// The documentation generator must never change what a snippet MEANS: whitespace inside verbatim
/// and raw strings is data, and text that looks like attributes, type names or using directives
/// inside a string literal is still string data. These tests run the real generator in an isolated
/// directory and compare both the exact text and the Roslyn-parsed literal values.
/// </summary>
public class GeneratorLiteralPreservationTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Pharos.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir.FullName;
    }

    private static string Generate(string markdown)
    {
        string root = Path.Combine(Path.GetTempPath(), "pharos-gen-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "docs"));
            File.WriteAllText(Path.Combine(root, "docs", "proof.md"), "# Proof\n\n```csharp\n" + markdown + "\n```\n");
            var psi = new ProcessStartInfo("python3", Path.Combine(RepoRoot(), "scripts", "generate-doc-tests.py"))
            {
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var process = Process.Start(psi)!;
            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            Assert.True(process.ExitCode == 0, $"generator exit {process.ExitCode}: {stderr}");
            string generated = Path.Combine(root, "tests", "Zaldaryon.Pharos.Docs.Tests", "Generated", "proof.g.cs");
            Assert.True(File.Exists(generated), $"generator produced no proof.g.cs: {stdout}");
            return File.ReadAllText(generated);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static string ParsedLiteralValue(string source, string variableName)
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(source);
        var literal = tree.GetRoot().DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.VariableDeclaratorSyntax>()
            .Single(v => v.Identifier.Text == variableName)
            .Initializer!.Value;
        var compilation = CSharpCompilation.Create("probe",
            [tree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var constant = compilation.GetSemanticModel(tree).GetConstantValue(literal);
        Assert.True(constant.HasValue, $"literal for {variableName} has no constant value");
        return (string)constant.Value!;
    }

    [Fact]
    public void VerbatimString_WhitespaceOnlyContentLine_IsPreserved()
    {
        string snippet = "string text = @\"alpha\n     \nomega\";";
        string generated = Generate(snippet);
        Assert.Contains(snippet, generated);
        Assert.Equal("alpha\n     \nomega", ParsedLiteralValue(generated, "text"));
    }

    [Fact]
    public void OrdinaryString_AttributeAndTypeNamesInside_AreNotRewritten()
    {
        string snippet = "string text = \"[Fact] ModSmokeTest\";";
        string generated = Generate(snippet);
        Assert.Contains(snippet, generated);
        Assert.Equal("[Fact] ModSmokeTest", ParsedLiteralValue(generated, "text"));
    }

    [Fact]
    public void RawString_AttributeAndTypeNamesInside_AreNotRewritten()
    {
        string snippet = "string text = \"\"\"\n[Fact] ModSmokeTest\n\"\"\";";
        string generated = Generate(snippet);
        Assert.Contains(snippet, generated);
        Assert.Equal("[Fact] ModSmokeTest", ParsedLiteralValue(generated, "text"));
    }

    [Fact]
    public void VerbatimString_UsingDirectiveInside_IsNotExtracted()
    {
        string snippet = "string text = @\"alpha\nusing Lost.Space;\nomega\";";
        string generated = Generate(snippet);
        Assert.Contains(snippet, generated);
        Assert.Equal("alpha\nusing Lost.Space;\nomega", ParsedLiteralValue(generated, "text"));
    }

    [Fact]
    public void InterpolatedVerbatimString_AttributeTextInside_IsNotRewritten()
    {
        string snippet = "string name = \"x\";\nstring text = $@\"[Fact] {name} ModSmokeTest\";";
        string generated = Generate(snippet);
        Assert.Contains("$@\"[Fact] {name} ModSmokeTest\"", generated);
    }
}
