using System.Collections.Generic;
using System.Reflection;
using Xunit;
using Zaldaryon.Pharos.Graphics;

namespace Zaldaryon.Pharos.Tests;

/// <summary>
/// Serializes all ShaderInspector tests to prevent parallel execution.
/// ShaderInspector uses a static _active field; concurrent Enable() calls
/// on separate instances would race.
/// </summary>
[CollectionDefinition("ShaderInspector", DisableParallelization = true)]
public sealed class ShaderInspectorTestCollection { }

/// <summary>
/// Unit tests for <see cref="ShaderInspector"/> and <see cref="ShaderSnapshot"/>.
/// None of these tests require a live OpenGL context. They exercise the recording logic
/// by calling the internal helper methods directly, following the same pattern as
/// <see cref="GlCommandProxyTests"/>.
/// </summary>
[Collection("ShaderInspector")]
public sealed class ShaderInspectorTests : IDisposable
{
    private readonly ShaderInspector _inspector = new();

    public void Dispose()
    {
        _inspector.Disable();
    }

    // -------------------------------------------------------------------------
    // Lifecycle tests
    // -------------------------------------------------------------------------

    [Fact]
    public void ShaderInspector_IsDisabledByDefault()
    {
        Assert.False(_inspector.IsEnabled);
    }

    [Fact]
    public void ShaderInspector_Enable_SetsIsEnabledTrue()
    {
        _inspector.Enable();

        Assert.True(_inspector.IsEnabled);
    }

    [Fact]
    public void ShaderInspector_Disable_AfterEnable_SetsIsEnabledFalse()
    {
        _inspector.Enable();
        _inspector.Disable();

        Assert.False(_inspector.IsEnabled);
    }

    [Fact]
    public void ShaderInspector_Enable_IsIdempotent()
    {
        _inspector.Enable();
        _inspector.Enable(); // second call must not throw

        Assert.True(_inspector.IsEnabled);
    }

    [Fact]
    public void ShaderInspector_Disable_WhenAlreadyDisabled_DoesNotThrow()
    {
        // Must not throw
        _inspector.Disable();

        Assert.False(_inspector.IsEnabled);
    }

    // -------------------------------------------------------------------------
    // Snapshot state tests
    // -------------------------------------------------------------------------

    [Fact]
    public void ShaderInspector_Snapshot_WhenDisabled_ReturnsEmpty()
    {
        ShaderSnapshot snap = _inspector.Snapshot();

        Assert.Equal(0, snap.TotalUploads);
        Assert.Equal(0, snap.RedundantUploadCount);
        Assert.Empty(snap.Uniforms);
        Assert.Empty(snap.BoundTextures);
        Assert.Empty(snap.RedundantUploads);
    }

    // -------------------------------------------------------------------------
    // Uniform recording tests (via internal helpers, no GL context needed)
    // -------------------------------------------------------------------------

    [Fact]
    public void ShaderInspector_RecordUniform_Float_CanBeRetrieved()
    {
        _inspector.Enable();

        ShaderInspector.OnUniformFloat(MakeFakeProgram(42), "fogDensity", 0.75f);

        ShaderSnapshot snap = _inspector.Snapshot();
        Assert.True(snap.Uniforms.ContainsKey("fogDensity"));
        Assert.Equal(0.75f, (float)snap.Uniforms["fogDensity"]);
        Assert.Equal(1, snap.TotalUploads);
    }

    [Fact]
    public void ShaderInspector_RecordUniform_Vec4_CanBeRetrieved()
    {
        _inspector.Enable();

        var program = MakeFakeProgram(7);
        ShaderInspector.OnUniformFloatArray(program, "color", [0.1f, 0.2f, 0.3f, 1.0f]);

        ShaderSnapshot snap = _inspector.Snapshot();
        Assert.True(snap.Uniforms.ContainsKey("color"));
        float[] stored = (float[])snap.Uniforms["color"];
        Assert.Equal(4, stored.Length);
        Assert.Equal(0.1f, stored[0], precision: 5);
        Assert.Equal(1.0f, stored[3], precision: 5);
    }

    [Fact]
    public void ShaderInspector_RecordUniform_Matrix_CanBeRetrieved()
    {
        _inspector.Enable();

        float[] matrix = new float[16];
        matrix[0] = 1f; matrix[5] = 1f; matrix[10] = 1f; matrix[15] = 1f; // identity
        ShaderInspector.OnUniformFloatArray(MakeFakeProgram(1), "projectionMatrix", matrix);

        ShaderSnapshot snap = _inspector.Snapshot();
        float[] stored = (float[])snap.Uniforms["projectionMatrix"];
        Assert.Equal(16, stored.Length);
        Assert.Equal(1f, stored[0]);
        Assert.Equal(1f, stored[15]);
    }

    // -------------------------------------------------------------------------
    // Redundant upload detection tests
    // -------------------------------------------------------------------------

    [Fact]
    public void ShaderInspector_RedundantUpload_DetectedOnSameValue()
    {
        _inspector.Enable();

        var prog = MakeFakeProgram(5);
        ShaderInspector.OnUniformFloat(prog, "alpha", 0.5f);
        ShaderInspector.OnUniformFloat(prog, "alpha", 0.5f); // same value = redundant

        ShaderSnapshot snap = _inspector.Snapshot();
        Assert.Equal(2, snap.TotalUploads);
        Assert.Equal(1, snap.RedundantUploadCount);
        Assert.True(snap.RedundantUploads.ContainsKey("alpha"));
        Assert.Equal(1, snap.RedundantUploads["alpha"]);
    }

    [Fact]
    public void ShaderInspector_RedundantUpload_NotDetectedOnDifferentValue()
    {
        _inspector.Enable();

        var prog = MakeFakeProgram(5);
        ShaderInspector.OnUniformFloat(prog, "alpha", 0.5f);
        ShaderInspector.OnUniformFloat(prog, "alpha", 0.9f); // different value

        ShaderSnapshot snap = _inspector.Snapshot();
        Assert.Equal(2, snap.TotalUploads);
        Assert.Equal(0, snap.RedundantUploadCount);
        Assert.Empty(snap.RedundantUploads);
    }

    [Fact]
    public void ShaderInspector_RedundantUpload_DetectedForArrayUniform()
    {
        _inspector.Enable();

        var prog = MakeFakeProgram(3);
        float[] val = [1f, 2f, 3f, 4f];
        ShaderInspector.OnUniformFloatArray(prog, "lightDir", val);
        ShaderInspector.OnUniformFloatArray(prog, "lightDir", val); // same content

        ShaderSnapshot snap = _inspector.Snapshot();
        Assert.Equal(1, snap.RedundantUploadCount);
    }

    // -------------------------------------------------------------------------
    // Reset tests
    // -------------------------------------------------------------------------

    [Fact]
    public void ShaderInspector_Reset_ClearsAllCounters()
    {
        _inspector.Enable();

        var prog = MakeFakeProgram(9);
        ShaderInspector.OnUniformFloat(prog, "time", 1.5f);
        ShaderInspector.OnUniformFloat(prog, "time", 1.5f);
        ShaderInspector.OnBindTexture(0, 99);

        _inspector.Reset();
        ShaderSnapshot snap = _inspector.Snapshot();

        Assert.Equal(0, snap.TotalUploads);
        Assert.Equal(0, snap.RedundantUploadCount);
        Assert.Empty(snap.Uniforms);
        Assert.Empty(snap.BoundTextures);
        Assert.Empty(snap.RedundantUploads);
    }

    // -------------------------------------------------------------------------
    // Texture tracking tests
    // -------------------------------------------------------------------------

    [Fact]
    public void ShaderInspector_BoundTexture_TrackedByUnit()
    {
        _inspector.Enable();

        ShaderInspector.OnBindTexture(0, 101);
        ShaderInspector.OnBindTexture(1, 202);

        ShaderSnapshot snap = _inspector.Snapshot();
        Assert.Equal(101, snap.BoundTextures[0]);
        Assert.Equal(202, snap.BoundTextures[1]);
    }

    [Fact]
    public void ShaderInspector_BoundTexture_LastWriteWinsPerUnit()
    {
        _inspector.Enable();

        ShaderInspector.OnBindTexture(0, 10);
        ShaderInspector.OnBindTexture(0, 20); // overwrites

        ShaderSnapshot snap = _inspector.Snapshot();
        Assert.Equal(20, snap.BoundTextures[0]);
        Assert.Single(snap.BoundTextures);
    }

    // -------------------------------------------------------------------------
    // ShaderSnapshot.Empty tests
    // -------------------------------------------------------------------------

    [Fact]
    public void ShaderSnapshot_Empty_HasAllZeros()
    {
        ShaderSnapshot empty = ShaderSnapshot.Empty;

        Assert.Equal(0, empty.ActiveProgramId);
        Assert.Equal(string.Empty, empty.ActiveProgramName);
        Assert.Equal(0, empty.TotalUploads);
        Assert.Equal(0, empty.RedundantUploadCount);
        Assert.Empty(empty.Uniforms);
        Assert.Empty(empty.BoundTextures);
        Assert.Empty(empty.RedundantUploads);
    }

    // -------------------------------------------------------------------------
    // HeadlessClient integration check (no live client required)
    // -------------------------------------------------------------------------

    [Fact]
    public void HeadlessClient_HasShadersProperty_OfCorrectType()
    {
        // Verify the property exists with the correct type via reflection.
        // No live headless client is created; this is a compile-time contract check at runtime.
        Type clientType = typeof(Zaldaryon.Pharos.Core.HeadlessClient);
        PropertyInfo? prop = clientType.GetProperty(
            "Shaders",
            BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(prop);
        Assert.Equal(typeof(ShaderInspector), prop!.PropertyType);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates a minimal fake ShaderProgramBase-derived instance with only ProgramId set,
    /// using reflection. This avoids needing a live GL context for unit tests.
    /// </summary>
    private static Vintagestory.Client.NoObf.ShaderProgramBase MakeFakeProgram(int programId)
    {
        // ShaderProgram inherits ShaderProgramBase and is concrete (Compile is implemented).
        // We create one and force its ProgramId via the public field.
        var prog = new Vintagestory.Client.NoObf.ShaderProgram();
        prog.ProgramId = programId;
        prog.PassName = $"test_shader_{programId}";
        prog.PassId = programId;
        return prog;
    }
}
