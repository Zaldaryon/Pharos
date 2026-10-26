using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace Zaldaryon.Pharos.Cli;

/// <summary>
/// <c>pharos init</c>: writes a minimal test project for a mod and then runs the doctor on it.
/// The <c>pharos-mod-tests</c> template of Zaldaryon.Pharos.Templates writes a fuller one.
/// </summary>
public static class InitCommand
{
    /// <summary>The help for <c>pharos init</c>.</summary>
    public const string HelpText = """
        pharos init - write a test project for a mod

        Usage: pharos init [options]

        Options:
          --dir <path>          Where the project goes (default: <mod>.Tests or Pharos.Tests)
          --mod-project <path>  The mod project the tests reference
          --name <namespace>    The namespace of the test class (default: the folder name)
          --force               Write over an existing project
          -h, --help            Show this help

        The project references Zaldaryon.Pharos.XUnit and holds one server scenario. After writing
        it, init runs pharos doctor on the new project. For a project that also smoke-tests the mod
        and joins a client, use the template: dotnet new install Zaldaryon.Pharos.Templates, then
        dotnet new pharos-mod-tests --mod-project <path>.
        """;

    /// <summary>Runs <c>pharos init</c> with the arguments that follow <c>init</c>.</summary>
    public static int Run(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        if (args.Contains("-h") || args.Contains("--help"))
        {
            stdout.WriteLine(HelpText);
            return 0;
        }

        try
        {
            string? directory = null;
            string? modProject = null;
            string? name = null;
            bool force = false;
            for (int i = 0; i < args.Count; i++)
            {
                string arg = args[i];
                string Value() => i + 1 < args.Count ? args[++i] : throw new ArgumentException($"{arg} needs a value.");
                switch (arg)
                {
                    case "--dir":
                        directory = Value();
                        break;
                    case "--mod-project":
                        modProject = Value();
                        break;
                    case "--name":
                        name = Value();
                        break;
                    case "--force":
                        force = true;
                        break;
                    default:
                        throw new ArgumentException($"Unknown option '{arg}'.");
                }
            }

            string? mod = modProject == null ? null : Path.GetFullPath(modProject);
            if (mod != null && !File.Exists(mod)) throw new ArgumentException($"The mod project '{mod}' does not exist.");

            string target = Path.GetFullPath(directory ?? DefaultDirectory(mod));
            if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any() && !force)
            {
                throw new ArgumentException($"'{target}' is not empty. Pass --force to write over it.");
            }

            string namespaceName = Namespace(name ?? Path.GetFileName(target));
            string projectName = Path.GetFileName(target);
            Directory.CreateDirectory(target);
            string csproj = Path.Combine(target, projectName + ".csproj");
            string test = Path.Combine(target, "SmokeTests.cs");
            string runner = Path.Combine(target, "xunit.runner.json");

            File.WriteAllText(csproj, ProjectFile(mod, target));
            File.WriteAllText(test, TestFile(namespaceName));
            File.WriteAllText(runner, """
                {
                  "$schema": "https://xunit.net/schema/current/xunit.runner.schema.json",
                  "parallelizeTestCollections": false,
                  "maxParallelThreads": 1
                }
                """);

            stdout.WriteLine($"Created {csproj}");
            stdout.WriteLine($"Created {test}");
            stdout.WriteLine($"Created {runner}");
            stdout.WriteLine();

            // A missing game or display is the machine's state, not a reason to leave the project unwritten.
            DoctorCommand.Run(["--project", csproj], stdout, stderr);
            stdout.WriteLine();
            stdout.WriteLine("Next: dotnet test " + Path.GetRelativePath(Directory.GetCurrentDirectory(), csproj));
            return 0;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            stderr.WriteLine($"pharos init: {ex.Message}");
            return 2;
        }
    }

    private static string DefaultDirectory(string? modProject)
    {
        string name = modProject == null ? "Pharos.Tests" : Path.GetFileNameWithoutExtension(modProject) + ".Tests";
        return name;
    }

    private static string ProjectFile(string? modProject, string target)
    {
        StringBuilder project = new();
        project.AppendLine("<Project Sdk=\"Microsoft.NET.Sdk\">");
        project.AppendLine("  <PropertyGroup>");
        project.AppendLine("    <TargetFramework>net10.0</TargetFramework>");
        project.AppendLine("    <LangVersion>latest</LangVersion>");
        project.AppendLine("    <Nullable>enable</Nullable>");
        project.AppendLine("    <ImplicitUsings>enable</ImplicitUsings>");
        project.AppendLine("    <IsPackable>false</IsPackable>");
        project.AppendLine("    <IsTestProject>true</IsTestProject>");
        project.AppendLine("    <VintageStoryPath Condition=\"'$(VintageStoryPath)' == ''\">$(VINTAGE_STORY)</VintageStoryPath>");
        project.AppendLine("  </PropertyGroup>");
        project.AppendLine();
        project.AppendLine("  <ItemGroup>");
        project.AppendLine("    <PackageReference Include=\"Microsoft.NET.Test.Sdk\" Version=\"17.11.1\" />");
        project.AppendLine("    <PackageReference Include=\"xunit\" Version=\"2.9.3\" />");
        project.AppendLine("    <PackageReference Include=\"xunit.runner.visualstudio\" Version=\"2.8.2\" />");
        project.AppendLine("    <PackageReference Include=\"Zaldaryon.Pharos.XUnit\" Version=\"0.8.0\" />");
        project.AppendLine("  </ItemGroup>");
        project.AppendLine();
        project.AppendLine("  <!-- Pharos runs the game in the test process, so the tests reference the install's assemblies. -->");
        project.AppendLine("  <ItemGroup>");
        project.AppendLine("    <Reference Include=\"VintagestoryAPI\"><HintPath>$(VintageStoryPath)/VintagestoryAPI.dll</HintPath><Private>true</Private></Reference>");
        project.AppendLine("    <Reference Include=\"VintagestoryLib\"><HintPath>$(VintageStoryPath)/VintagestoryLib.dll</HintPath><Private>true</Private></Reference>");
        project.AppendLine("    <Reference Include=\"0Harmony\"><HintPath>$(VintageStoryPath)/Lib/0Harmony.dll</HintPath><Private>true</Private></Reference>");
        project.AppendLine("    <Reference Include=\"protobuf-net\"><HintPath>$(VintageStoryPath)/Lib/protobuf-net.dll</HintPath><Private>true</Private></Reference>");
        project.AppendLine("  </ItemGroup>");
        if (modProject != null)
        {
            string relative = Path.GetRelativePath(target, modProject).Replace('\\', '/');
            project.AppendLine();
            project.AppendLine("  <ItemGroup>");
            project.AppendLine(CultureInfo.InvariantCulture, $"    <ProjectReference Include=\"{System.Security.SecurityElement.Escape(relative)}\" />");
            project.AppendLine("  </ItemGroup>");
        }
        project.AppendLine("</Project>");
        return project.ToString();
    }

    private static string TestFile(string namespaceName) => $$"""
        using Xunit;
        using Zaldaryon.Pharos.XUnit;

        namespace {{namespaceName}};

        /// <summary>The first test: the server boots and says it is there.</summary>
        [Collection("Sequential")]
        [Trait(PharosTraits.Category, PharosTraits.Live)]
        public sealed class SmokeTests : ServerScenarioBase
        {
            [ServerScenario]
            public Task TheServer_Starts()
            {
                Assert.NotNull(Server.Api);
                return Task.CompletedTask;
            }
        }
        """;

    private static string Namespace(string name)
    {
        string[] parts = name.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "PharosTests";

        List<string> segments = [];
        foreach (string part in parts)
        {
            StringBuilder result = new(part.Length);
            foreach (char c in part)
            {
                result.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
            }
            string text = result.ToString();
            if (text.Length == 0) text = "_";
            if (char.IsDigit(text[0])) text = "_" + text;
            segments.Add(text);
        }
        return string.Join('.', segments);
    }
}
