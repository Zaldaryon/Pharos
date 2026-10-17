using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Docs.Tests;

public class MyPing { }
public class MyMod { }
public class MyModSystem
{
    public static MyModSystem Instance { get; } = new();
    public void ReloadFromWorld() { }
    public void OnTick() { }
}

public class DoorMock
{
    public bool Open => true;
}

[AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
public class DocExampleAttribute : Attribute
{
    public DocExampleAttribute(params object?[] _) { }
}

public abstract class DocModSmokeTest : ClientServerScenarioBase
{
    protected virtual int SmokeFrames => 600;
    protected virtual IReadOnlyList<string> SmokeCommands => [];
    protected virtual bool SmokeSaveReload => false;
    public Task ModBootsJoinsAndPlays() => Task.CompletedTask;
}

public static class PlayerNames
{
    public static string Next() => "TestPlayer";
}

