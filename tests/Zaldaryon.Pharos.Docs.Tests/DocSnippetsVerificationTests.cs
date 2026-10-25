using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Zaldaryon.Pharos.Docs.Tests;

public class DocSnippetsVerificationTests
{
    [Fact]
    public void AllDocumentationSnippets_AreExtractedAndGenerated()
    {
        string currentDir = AppContext.BaseDirectory;
        string? repoRoot = null;

        var dir = new DirectoryInfo(currentDir);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Pharos.sln")))
            {
                repoRoot = dir.FullName;
                break;
            }
            dir = dir.Parent;
        }

        Assert.NotNull(repoRoot);

        string docsDir = Path.Combine(repoRoot, "docs");
        string generatedDir = Path.Combine(repoRoot, "tests", "Zaldaryon.Pharos.Docs.Tests", "Generated");

        Assert.True(Directory.Exists(docsDir), $"Docs directory not found at {docsDir}");
        Assert.True(Directory.Exists(generatedDir), $"Generated tests directory not found at {generatedDir}");

        var docFiles = Directory.GetFiles(docsDir, "*.md");
        int totalSnippets = 0;

        foreach (string docFile in docFiles)
        {
            string content = File.ReadAllText(docFile);
            var matches = Regex.Matches(content, @"```csharp\s*([\s\S]*?)```");
            if (matches.Count > 0)
            {
                totalSnippets += matches.Count;
                string slug = Path.GetFileNameWithoutExtension(docFile).Replace('-', '_');
                string expectedGenerated = Path.Combine(generatedDir, $"{slug}.g.cs");
                Assert.True(File.Exists(expectedGenerated), $"Missing generated file for {docFile}: {expectedGenerated}");
            }
        }

        Assert.True(totalSnippets > 100, $"Expected over 100 snippets extracted across docs, found {totalSnippets}");

        foreach (string docFile in docFiles)
        {
            string slug = Path.GetFileNameWithoutExtension(docFile).Replace('-', '_');
            string generatedFile = Path.Combine(generatedDir, $"{slug}.g.cs");
            if (!File.Exists(generatedFile)) continue;
            string[] generatedLines = File.ReadAllLines(generatedFile);
            for (int lineIndex = 0; lineIndex < generatedLines.Length; lineIndex++)
            {
                string line = generatedLines[lineIndex];
                Assert.True(line.Length == 0 || line.Trim().Length > 0,
                    $"Whitespace-only line {lineIndex + 1} in generated file {generatedFile}");
            }
        }
    }
}
