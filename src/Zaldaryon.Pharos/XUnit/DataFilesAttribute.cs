using Vintagestory.API.Common;

namespace Zaldaryon.Pharos.XUnit;

/// <summary>
/// Copies a file into the server's or the client's data folder before they boot, such as a mod's
/// config into <c>ModConfig</c>, so the mod starts with it. See <c>docs/data-files.md</c>.
/// </summary>
/// <remarks>
/// <para>
/// A <c>{{pharos:port:NAME}}</c> placeholder in the file is replaced with a free port; the test
/// reads it back with <c>DataFilePort("NAME")</c>. The same name gets the same port in every file
/// of the test, on both sides.
/// </para>
/// <para>
/// Put it on the class, or on a test to add or replace a file for that test alone, which boots it
/// hosts of its own. A test's file replaces the class's file for the same side and destination.
/// In a class that rolls back between tests, files a test changes are put back after it.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class DataFilesAttribute : Attribute
{
    /// <param name="source">
    /// The file to copy: relative to the test's working folder, which is its output folder under
    /// <c>dotnet test</c>, or absolute. Copy fixtures to the output with
    /// <c>&lt;None Include="Fixtures\**" CopyToOutputDirectory="PreserveNewest" /&gt;</c>.
    /// </param>
    public DataFilesAttribute(string source)
    {
        Source = source;
    }

    /// <summary>The file to copy.</summary>
    public string Source { get; }

    /// <summary>
    /// Where it goes, relative to the data folder. Defaults to <c>ModConfig/</c> and the file's name.
    /// </summary>
    public string? To { get; set; }

    /// <summary>Which side's data folder it goes into: the server's, the client's, or both (the default).</summary>
    public EnumAppSide Side { get; set; } = EnumAppSide.Universal;
}
