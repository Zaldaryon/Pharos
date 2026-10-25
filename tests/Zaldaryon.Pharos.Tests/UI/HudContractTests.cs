using System.Runtime.CompilerServices;
using Vintagestory.API.Client;
using Xunit;
using Zaldaryon.Pharos.UI;

namespace Zaldaryon.Pharos.Tests.UI;

public class HudContractTests
{
    [Fact]
    public void Statbar_PrivateContract_Exists_AndNullableFutureIsPreserved()
    {
        var bar = (GuiElementStatbar)RuntimeHelpers.GetUninitializedObject(typeof(GuiElementStatbar));
        Assert.Equal(0, HudDriver.RequiredField<float>(bar, "maxValue"));
        Assert.Null(HudDriver.RequiredField<float?>(bar, "futureValue"));
        Assert.Throws<InvalidOperationException>(() => HudDriver.RequiredField<int>(bar, "maxValue"));
        var absent = Assert.Throws<InvalidOperationException>(() => HudDriver.RequiredField<float>(bar, "renamed-value"));
        Assert.Contains("renamed-value", absent.Message);
        Assert.Contains("compatibility", absent.Message);
    }

    [Fact]
    public void PrivateFields_InBaseTypes_AreReadWithoutSilentlyInventingValues()
    {
        var instance = new Derived();
        Assert.Equal(instance.Number, HudDriver.RequiredField<int>(instance, "_number"));
        Assert.Throws<InvalidOperationException>(() => HudDriver.RequiredField<string>(instance, "_text"));
        Assert.Throws<InvalidOperationException>(() => HudDriver.RequiredField<float?>(instance, "_text"));
    }

    private class Parent
    {
        private readonly int _number = 4;
        public int Number => _number;
        private readonly string? _text = null;
        public string? Text => _text;
    }

    private sealed class Derived : Parent;
}
