using System.Reflection;
using Xunit;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Server;

/// <summary>The privilege matrix attribute, and the chat markup the recorded messages carry.</summary>
public sealed class CommandPrivilegeMatrixTests
{
    [Fact]
    public void TheMatrix_GivesOneRowPerRole()
    {
        List<object[]> rows = [.. new CommandPrivilegeMatrixAttribute("/mycmd reset", "suplayer", "admin").GetData(Method())];

        Assert.Equal(2, rows.Count);
        CommandPrivilegeCase first = Assert.IsType<CommandPrivilegeCase>(rows[0][0]);
        Assert.Equal("/mycmd reset", first.Command);
        Assert.Equal("suplayer", first.Role);
        Assert.Null(first.Expected);
    }

    [Fact]
    public void TheExpectedOutcomes_FollowTheRoles()
    {
        List<object[]> rows = [.. new CommandPrivilegeMatrixAttribute("/mycmd reset", "suplayer", "admin") { Expected = "denied,allowed" }.GetData(Method())];

        Assert.Equal("denied", ((CommandPrivilegeCase)rows[0][0]).Expected);
        Assert.Equal("allowed", ((CommandPrivilegeCase)rows[1][0]).Expected);
    }

    [Fact]
    public void AMatrix_NeedsRoles_AndAMethodWithOneCase()
    {
        Assert.Throws<ArgumentException>(() => new CommandPrivilegeMatrixAttribute("/mycmd reset"));
        Assert.Throws<ArgumentException>(() => new CommandPrivilegeMatrixAttribute("/mycmd reset", "suplayer", " "));

        Assert.Throws<ArgumentException>(() => new CommandPrivilegeMatrixAttribute("/mycmd reset", "suplayer").GetData(MethodWithoutCase()));
        Assert.Throws<ArgumentException>(() => new CommandPrivilegeMatrixAttribute("/mycmd reset", "suplayer") { Expected = "allowed,denied" }.GetData(Method()));
    }

    [Fact]
    public void TheChatMarkup_IsStrippedFromTheText()
    {
        Assert.Equal("hello world", ChatText.Plain("hello <b>world</b>"));
        Assert.Equal("a > b", ChatText.Plain("a &gt; b"));
        Assert.Equal("a & b", ChatText.Plain("a &amp; b"));
        Assert.Equal("plain", ChatText.Plain("plain"));
    }

    private static MethodInfo Method() => typeof(CommandPrivilegeMatrixTests).GetMethod("TheRow", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static MethodInfo MethodWithoutCase() => typeof(CommandPrivilegeMatrixTests).GetMethod("TheOtherRow", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private void TheRow(CommandPrivilegeCase privilege)
    {
    }

    private void TheOtherRow()
    {
    }
}
