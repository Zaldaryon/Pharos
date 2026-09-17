using System;
using System.Text.RegularExpressions;
using OpenTK.Graphics.OpenGL;
using Zaldaryon.Pharos.Bootstrap;

namespace Zaldaryon.Pharos.Platform;

/// <summary>
/// Encapsulates OpenGL driver and renderer diagnostics for headless execution.
/// </summary>
public sealed record GlRendererInfo(
    string Vendor,
    string Renderer,
    string Version,
    string ShadingLanguageVersion,
    Version ParsedVersion,
    bool IsSoftwareRenderer)
{
    public bool SupportsCoreVersion(int major, int minor)
    {
        return ParsedVersion >= new Version(major, minor);
    }
}

/// <summary>
/// Manages headless Linux environment configuration including Mesa llvmpipe and virtual display servers.
/// </summary>
public static class LinuxHeadlessEnvironment
{
    private static readonly Regex VersionRegex = new(@"\b(?<major>\d+)\.(?<minor>\d+)", RegexOptions.Compiled);

    /// <summary>
    /// Configures environment variables for Mesa software rasterization (llvmpipe) and OpenGL version overrides.
    /// </summary>
    public static void ConfigureMesaEnvironment(HeadlessClientOptions? options = null)
    {
        options ??= new HeadlessClientOptions();

        if (OperatingSystem.IsLinux() || options.ForceSoftwareRendering)
        {
            Environment.SetEnvironmentVariable("LIBGL_ALWAYS_SOFTWARE", "1");
            Environment.SetEnvironmentVariable("GALLIUM_DRIVER", "llvmpipe");
            Environment.SetEnvironmentVariable("MESA_LOADER_DRIVER_OVERRIDE", "llvmpipe");
            Environment.SetEnvironmentVariable("LIBGL_ALWAYS_INDIRECT", "0");
            Environment.SetEnvironmentVariable("MESA_GL_VERSION_OVERRIDE", options.MesaGlVersionOverride);
            Environment.SetEnvironmentVariable("MESA_GLSL_VERSION_OVERRIDE", options.MesaGlslVersionOverride);
        }
    }

    /// <summary>
    /// Validates that a display server (X11 DISPLAY or WAYLAND_DISPLAY) is accessible when running on Linux.
    /// </summary>
    public static void ValidateDisplayServer(string? displayOverride = null)
    {
        if (OperatingSystem.IsLinux() && !string.IsNullOrWhiteSpace(displayOverride))
        {
            Environment.SetEnvironmentVariable("DISPLAY", displayOverride.Trim());
            return;
        }

        if (OperatingSystem.IsLinux())
        {
            string? display = Environment.GetEnvironmentVariable("DISPLAY");
            string? waylandDisplay = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");

            if (string.IsNullOrWhiteSpace(display) && string.IsNullOrWhiteSpace(waylandDisplay))
            {
                throw new InvalidOperationException(
                    "No display server found on Linux. Headless execution requires a virtual display server " +
                    "(e.g. xvfb-run or DISPLAY=:99) or an active X11/Wayland session.");
            }
        }
    }

    /// <summary>
    /// Determines whether the specified renderer string indicates a software rasterizer.
    /// </summary>
    public static bool IsSoftwareRenderer(string? renderer)
    {
        if (string.IsNullOrWhiteSpace(renderer))
        {
            return false;
        }

        return renderer.Contains("llvmpipe", StringComparison.OrdinalIgnoreCase)
            || renderer.Contains("softpipe", StringComparison.OrdinalIgnoreCase)
            || renderer.Contains("swrast", StringComparison.OrdinalIgnoreCase)
            || renderer.Contains("Software Rasterizer", StringComparison.OrdinalIgnoreCase)
            || renderer.Contains("Microsoft Basic Render Driver", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Parses the major and minor version numbers from an OpenGL version string.
    /// </summary>
    public static Version ParseGlVersion(string? versionString)
    {
        if (string.IsNullOrWhiteSpace(versionString))
        {
            return new Version(0, 0);
        }

        Match match = VersionRegex.Match(versionString);
        if (match.Success
            && int.TryParse(match.Groups["major"].Value, out int major)
            && int.TryParse(match.Groups["minor"].Value, out int minor))
        {
            return new Version(major, minor);
        }

        return new Version(0, 0);
    }

    /// <summary>
    /// Queries the active OpenGL context for vendor, renderer, and version metadata.
    /// </summary>
    public static GlRendererInfo QueryRendererInfo()
    {
        string vendor = GL.GetString(StringName.Vendor) ?? string.Empty;
        string renderer = GL.GetString(StringName.Renderer) ?? string.Empty;
        string version = GL.GetString(StringName.Version) ?? string.Empty;
        string glslVersion = GL.GetString(StringName.ShadingLanguageVersion) ?? string.Empty;

        Version parsedVersion = ParseGlVersion(version);
        bool isSoftware = IsSoftwareRenderer(renderer);

        return new GlRendererInfo(vendor, renderer, version, glslVersion, parsedVersion, isSoftware);
    }
}
