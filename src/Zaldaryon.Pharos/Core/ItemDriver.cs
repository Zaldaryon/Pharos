using System.Text.RegularExpressions;
using OpenTK.Graphics.OpenGL;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.Client.NoObf;

namespace Zaldaryon.Pharos.Core;

/// <summary>The tooltip the game shows for a stack.</summary>
/// <param name="Title">The stack's name, the tooltip's title.</param>
/// <param name="Text">The description under it, as the game builds it, markup included.</param>
public sealed partial record ItemTooltip(string Title, string Text)
{
    /// <summary>
    /// The description with its markup tags (<c>&lt;font&gt;</c>, <c>&lt;a&gt;</c> and so on) taken out,
    /// <c>&lt;br&gt;</c> as a line break and the markup's entities decoded.
    /// </summary>
    public string PlainText => ToPlain(Text);

    /// <summary>The lines of <see cref="PlainText"/>, without trailing blank lines.</summary>
    public IReadOnlyList<string> Lines => SplitLines(PlainText);

    /// <summary>Whether the title or the plain text contains <paramref name="text"/>.</summary>
    public bool Contains(string text) => Title.Contains(text, StringComparison.Ordinal) || PlainText.Contains(text, StringComparison.Ordinal);

    public override string ToString() => Title + "\n" + PlainText;

    internal static IReadOnlyList<string> SplitLines(string text)
    {
        List<string> lines = [.. text.Replace("\r", "").Split('\n').Select(line => line.TrimEnd())];
        while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        return lines;
    }

    internal static string ToPlain(string text)
    {
        string plain = LineBreak().Replace(text, "\n");
        plain = Tag().Replace(plain, "");
        return plain.Replace("&nbsp;", " ").Replace("&lt;", "<").Replace("&gt;", ">").Replace("&amp;", "&");
    }

    [GeneratedRegex("<br\\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreak();

    // Tags only: "< 5" or "a > b" in plain text is left alone.
    [GeneratedRegex("</?[A-Za-z][\\w-]*(\\s[^<>]*)?/?>")]
    private static partial Regex Tag();
}

/// <summary>A block or an item, as an icon sweep names it.</summary>
public sealed record IconKey(AssetLocation Code, EnumItemClass Class)
{
    public override string ToString() => $"{Class.ToString().ToLowerInvariant()} {Code}";
}

/// <summary>
/// An engine-mode client's items: stacks by code, the tooltips the game builds for them, and their
/// GUI icons rendered as the game's own <c>.exponepng</c> renders them.
/// </summary>
/// <remarks>
/// <para>
/// Icons are rendered in the client's GUI pass of a frame, the 2D overlay stage, so each
/// <see cref="RenderIcon"/> call advances the client by one frame, game tick included. They are
/// rendered at a GUI scale of 1, whatever the client's setting, so they do not change with it.
/// </para>
/// <para>
/// An icon's bytes are the framebuffer's: RGBA, top row first. Fully opaque and fully transparent
/// pixels match the game's export at GUI scale 1; edges blended over the transparent background do not, byte for
/// byte, so make golden images with <see cref="SaveIcons"/> or <see cref="RenderIcon"/>, not with
/// <c>.exponepng</c>.
/// </para>
/// </remarks>
public sealed class ItemDriver
{
    // How many icons are rendered, and held, per frame: up to 256, within about 64 MiB of pixels.
    private static int IconsPerFrame(int size) => Math.Clamp(64 * 1024 * 1024 / (size * size * 4), 1, 256);
    private readonly HeadlessClient _client;

    internal ItemDriver(HeadlessClient client) => _client = client;

    private ClientMain Game => _client.Client;

    /// <summary>
    /// A stack of the item or block with <paramref name="code"/> ("game:" when it has no domain).
    /// When an item and a block share the code, name the one you mean with <paramref name="type"/>.
    /// </summary>
    /// <exception cref="ArgumentException">The game has no such item or block, or both.</exception>
    public ItemStack Stack(string code, int quantity = 1, EnumItemClass? type = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        RequireJoined();
        AssetLocation location = new(code);
        return _client.RunOnClientThread(() =>
        {
            Item? item = type is null or EnumItemClass.Item ? Game.World.GetItem(location) : null;
            Block? block = type is null or EnumItemClass.Block ? Game.World.GetBlock(location) : null;
            if (item?.Code == null) item = null;
            if (block?.Code == null || block.Id == 0 && location.Path != "air") block = null;
            return (item, block) switch
            {
                ({ } i, null) => new ItemStack(i, quantity),
                (null, { } b) => new ItemStack(b, quantity),
                (null, null) => throw new ArgumentException($"The game has no {(type?.ToString().ToLowerInvariant() ?? "item or block")} '{location}'.", nameof(code)),
                _ => throw new ArgumentException($"'{location}' is both an item and a block; pass type: EnumItemClass.Item or EnumItemClass.Block.", nameof(code)),
            };
        });
    }

    /// <summary>
    /// The tooltip the game shows for <paramref name="stack"/>: its name and the description its
    /// collectible builds (<c>GetHeldItemInfo</c>), on the client thread. The stack is not changed.
    /// </summary>
    /// <param name="stack">The stack.</param>
    /// <param name="extendedInfo">Whether to add the lines the game adds with extended debug info on, such as the code.</param>
    /// <remarks>
    /// The game wraps the text to the tooltip's width when it draws it; the text here is unwrapped.
    /// Some collectibles read the extended debug info setting themselves, whatever
    /// <paramref name="extendedInfo"/> says.
    /// </remarks>
    public ItemTooltip Tooltip(ItemStack stack, bool extendedInfo = false)
    {
        ArgumentNullException.ThrowIfNull(stack);
        RequireJoined();
        return _client.RunOnClientThread(() =>
        {
            // A perishable's description updates its transition attributes: a copy keeps the caller's stack as it was.
            DummySlot slot = new(stack.Clone());
            return new ItemTooltip(slot.GetStackName() ?? "", slot.GetStackDescription((IClientWorldAccessor)Game.World, extendedInfo) ?? "");
        });
    }

    /// <summary>
    /// Renders the GUI icon of <paramref name="stack"/> as the game's <c>.exponepng</c> does, into
    /// an offscreen framebuffer <paramref name="size"/> pixels square, and returns it. Advances the
    /// client by one frame. The stack is not changed.
    /// </summary>
    /// <exception cref="InvalidOperationException">The client did not render its GUI pass that frame, or the stack has no GUI model.</exception>
    public FramebufferSnapshot RenderIcon(ItemStack stack, int size = 64)
    {
        ArgumentNullException.ThrowIfNull(stack);
        if (stack.Collectible?.Code == null) throw new ArgumentException("The stack has no item or block.", nameof(stack));
        CheckSize(size);
        List<(IconKey Key, ItemStack Stack)> one = [(new IconKey(stack.Collectible.Code, stack.Class), stack.Clone())];
        FramebufferSnapshot? icon = null;
        RenderBatch(one, size, (_, snapshot) => icon = snapshot, failures: null);
        return icon!;
    }

    /// <summary>
    /// Renders the GUI icon of every block and item of <paramref name="domain"/>: those in a creative
    /// inventory tab, or all of them, as the game's <c>blockitempngexport</c> picks them. Up to
    /// 256 are rendered per frame, fewer at large sizes.
    /// </summary>
    /// <exception cref="InvalidOperationException">Some have no GUI model; the message names them.</exception>
    public IReadOnlyDictionary<IconKey, FramebufferSnapshot> RenderIcons(string domain, int size = 64, bool creativeOnly = true)
    {
        Dictionary<IconKey, FramebufferSnapshot> icons = [];
        ForEachIcon(domain, size, creativeOnly, (key, icon) => icons[key] = icon);
        return icons;
    }

    /// <summary>
    /// Renders the icons <see cref="RenderIcons"/> would and writes them, frame by frame, to
    /// <c>block/&lt;path&gt;.png</c> or <c>item/&lt;path&gt;.png</c> under
    /// <paramref name="directory"/>, named as the game's export names them. Returns how many it wrote.
    /// </summary>
    public int SaveIcons(string domain, string directory, int size = 64, bool creativeOnly = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        int written = 0;
        ForEachIcon(domain, size, creativeOnly, (key, icon) =>
        {
            string folder = key.Class == EnumItemClass.Block ? "block" : "item";
            string name = key.Class == EnumItemClass.Block ? key.Code.Path : key.Code.Path.Replace('/', '-');
            icon.SaveToPng(Path.Combine(directory, folder, name + ".png"));
            written++;
        });
        return written;
    }

    private void ForEachIcon(string domain, int size, bool creativeOnly, Action<IconKey, FramebufferSnapshot> each)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        domain = domain.ToLowerInvariant();
        CheckSize(size);
        RequireJoined();
        List<(IconKey Key, ItemStack Stack)> stacks = _client.RunOnClientThread(() =>
        {
            List<(IconKey, ItemStack)> found = [];
            foreach (Block block in Game.Blocks)
            {
                if (block?.Code != null && block.Code.Domain == domain && (!creativeOnly || block.CreativeInventoryTabs?.Length > 0))
                {
                    found.Add((new IconKey(block.Code, EnumItemClass.Block), new ItemStack(block)));
                }
            }

            foreach (Item item in Game.Items)
            {
                if (item?.Code != null && item.Code.Domain == domain && (!creativeOnly || item.CreativeInventoryTabs?.Length > 0))
                {
                    found.Add((new IconKey(item.Code, EnumItemClass.Item), new ItemStack(item)));
                }
            }

            return found;
        });

        if (stacks.Count == 0) throw new ArgumentException($"The client has no {(creativeOnly ? "creative-inventory " : "")}item or block in the domain '{domain}'.", nameof(domain));

        List<IconKey> failures = [];
        int perFrame = IconsPerFrame(size);
        for (int i = 0; i < stacks.Count; i += perFrame)
        {
            RenderBatch(stacks.GetRange(i, Math.Min(perFrame, stacks.Count - i)), size, each, failures);
        }

        if (failures.Count > 0)
        {
            throw new InvalidOperationException(
                $"{failures.Count} of {stacks.Count} stacks of '{domain}' have no GUI model to render: {string.Join(", ", failures.Take(20))}{(failures.Count > 20 ? ", ..." : "")}.");
        }
    }

    // Renders the stacks in the GUI pass of one frame, reporting each icon as it is read back.
    private void RenderBatch(List<(IconKey Key, ItemStack Stack)> stacks, int size, Action<IconKey, FramebufferSnapshot> each, List<IconKey>? failures)
    {
        RequireJoined();
        if (_client.FrameController.IsSteppingOnThisThread)
        {
            throw new InvalidOperationException("Icons are rendered in a frame of their own: not from inside a frame.");
        }

        List<(IconKey, FramebufferSnapshot?)> rendered = [];
        Exception? error = null;
        IconRenderer renderer = new(() =>
        {
            try
            {
                RenderInGuiPass(stacks, size, rendered);
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });

        _client.RunOnClientThread(() => Game.eventManager.RegisterRenderer(renderer, EnumRenderStage.Ortho, "pharos-icons"));
        try
        {
            _client.FrameController.Step(1f / 60f);
        }
        finally
        {
            _client.RunOnClientThread(() => Game.eventManager.UnregisterRenderer(renderer, EnumRenderStage.Ortho));
        }

        if (error != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
        if (!renderer.Ran)
        {
            throw new InvalidOperationException(
                "The client did not render its GUI pass this frame, so no icon was rendered: the GUI is hidden, or the client has not joined a world.");
        }

        foreach ((IconKey key, FramebufferSnapshot? icon) in rendered)
        {
            if (icon != null)
            {
                each(key, icon);
            }
            else if (failures != null)
            {
                failures.Add(key);
            }
            else
            {
                throw new InvalidOperationException($"The {key} has no GUI model to render.");
            }
        }
    }

    // What ExportPNGfromItemstack does, with the GL state, the framebuffer and the matrix stacks put
    // back as they were: the rest of the frame's GUI pass draws on as if nothing happened.
    private void RenderInGuiPass(List<(IconKey Key, ItemStack Stack)> stacks, int size, List<(IconKey, FramebufferSnapshot?)> rendered)
    {
        ClientPlatformWindows platform = (ClientPlatformWindows)Game.Platform;
        FrameBufferRef? previous = platform.CurrentFrameBuffer;
        int[] viewport = new int[4];
        GL.GetInteger(GetPName.Viewport, viewport);
        bool depthTest = GL.IsEnabled(EnableCap.DepthTest);
        bool cullFace = GL.IsEnabled(EnableCap.CullFace);
        bool blend = GL.IsEnabled(EnableCap.Blend);
        GL.GetInteger(GetPName.BlendSrcRgb, out int blendSrcRgb);
        GL.GetInteger(GetPName.BlendDstRgb, out int blendDstRgb);
        GL.GetInteger(GetPName.BlendSrcAlpha, out int blendSrcAlpha);
        GL.GetInteger(GetPName.BlendDstAlpha, out int blendDstAlpha);
        int projectionDepth = Game.PMatrix.Count;
        int modelViewDepth = Game.MvMatrix.Count;
        bool projectionMode = Game.CurrentMatrixModeProjection;
        float guiScale = RuntimeEnv.GUIScale;

        bool scissor = GL.IsEnabled(EnableCap.ScissorTest);
        FrameBufferRef? framebuffer = null;
        try
        {
            framebuffer = platform.CreateFramebuffer(new FramebufferAttrs("PharosIcon", size, size)
            {
                Attachments =
                [
                    new FramebufferAttrsAttachment
                    {
                        AttachmentType = EnumFramebufferAttachment.ColorAttachment0,
                        Texture = new RawTexture { Width = size, Height = size, PixelFormat = EnumTexturePixelFormat.Rgba, PixelInternalFormat = EnumTextureInternalFormat.Rgba8 },
                    },
                    new FramebufferAttrsAttachment
                    {
                        AttachmentType = EnumFramebufferAttachment.DepthAttachment,
                        Texture = new RawTexture { Width = size, Height = size, PixelFormat = EnumTexturePixelFormat.DepthComponent, PixelInternalFormat = EnumTextureInternalFormat.DepthComponent32 },
                    },
                ],
            });

            RuntimeEnv.GUIScale = 1f;
            GL.Disable(EnableCap.ScissorTest);
            platform.CurrentFrameBuffer = framebuffer;
            platform.GlEnableDepthTest();
            platform.GlDisableCullFace();
            platform.GlToggleBlend(on: true);
            Game.OrthoMode(size, size);
            float[] transparent = new float[4];
            foreach ((IconKey key, ItemStack stack) in stacks)
            {
                DummySlot slot = new(stack);

                // Vanilla draws nothing, silently, without a GUI model or transform. Looking it up
                // first runs the collectible's before-render hooks twice; that is harmless.
                ItemRenderInfo info = InventoryItemRenderer.GetItemStackRenderInfo(Game, slot, EnumItemRenderTarget.Gui, 0f);
                if (info.ModelRef == null || info.Transform == null)
                {
                    rendered.Add((key, null));
                    continue;
                }

                platform.ClearFrameBuffer(framebuffer, transparent);
                try
                {
                    ((ICoreClientAPI)Game.api).Render.RenderItemstackToGui(slot, size / 2, size / 2, 500.0, size / 2, -1, 0f, shading: true, rotate: false, showStackSize: false);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Rendering the icon of the {key} failed: {ex.Message}", ex);
                }

                // A collectible's own GUI renderer may have bound another framebuffer.
                platform.CurrentFrameBuffer = framebuffer;
                byte[] pixels = new byte[size * size * 4];
                GL.ReadPixels(0, 0, size, size, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
                HeadlessFramebuffer.FlipVertically(pixels, size, size);
                rendered.Add((key, new FramebufferSnapshot(pixels, size, size)));
            }
        }
        finally
        {
            RuntimeEnv.GUIScale = guiScale;
            while (Game.PMatrix.Count > projectionDepth) Game.PMatrix.Pop();
            while (Game.MvMatrix.Count > modelViewDepth) Game.MvMatrix.Pop();
            Game.CurrentMatrixModeProjection = projectionMode;

            platform.CurrentFrameBuffer = previous;
            GL.Viewport(viewport[0], viewport[1], viewport[2], viewport[3]);
            Toggle(EnableCap.DepthTest, depthTest);
            Toggle(EnableCap.CullFace, cullFace);
            Toggle(EnableCap.Blend, blend);
            Toggle(EnableCap.ScissorTest, scissor);
            GL.BlendFuncSeparate((BlendingFactorSrc)blendSrcRgb, (BlendingFactorDest)blendDstRgb, (BlendingFactorSrc)blendSrcAlpha, (BlendingFactorDest)blendDstAlpha);
            if (framebuffer != null) platform.DisposeFrameBuffer(framebuffer);
        }
    }

    private static void Toggle(EnableCap cap, bool on)
    {
        if (on) GL.Enable(cap);
        else GL.Disable(cap);
    }

    private static void CheckSize(int size)
    {
        if (size is < 8 or > 1024) throw new ArgumentOutOfRangeException(nameof(size), size, "An icon is 8 to 1024 pixels square.");
    }

    private void RequireJoined()
    {
        if (!_client.IsEngineMode) throw new NotSupportedException("Items are inspected on an engine-mode client only.");
        if (!_client.IsJoined) throw new InvalidOperationException("The client has not joined a world yet.");
    }

    // A renderer that runs once, in the GUI pass of the frame it is registered for.
    private sealed class IconRenderer(Action render) : IRenderer
    {
        public bool Ran { get; private set; }

        public double RenderOrder => 0.49;

        public int RenderRange => 0;

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            // Ran means the pass came; an error while rendering is reported on its own.
            if (Ran) return;
            Ran = true;
            render();
        }

        public void Dispose()
        {
        }
    }
}
