using Xunit.Abstractions;
using Zaldaryon.Pharos.Reporting;
using Zaldaryon.Pharos.Server;

namespace Zaldaryon.Pharos.Content;

/// <summary>Which kind of server content a <see cref="ContentSweep"/> case inspects.</summary>
public enum ContentKind
{
    /// <summary>A block type in <c>World.Blocks</c>.</summary>
    Block,
    /// <summary>An item type in <c>World.Items</c>.</summary>
    Item,
    /// <summary>An entity type in <c>World.EntityTypes</c>.</summary>
    Entity,
    /// <summary>All live content in a domain, including types registered by code.</summary>
    Domain,
}

/// <summary>A content code passed to a content sweep theory row.</summary>
/// <remarks>
/// <para><see cref="Code"/> is a fully-qualified Vintage Story code, such as <c>mymod:oak-door</c>.</para>
/// <para>
/// The type is xUnit-serializable so a <c>[ContentSweep]</c> row can be shown by test explorers and
/// run by Pharos workers.
/// </para>
/// </remarks>
public sealed record ContentCode : IXunitSerializable
{
    /// <summary>For xUnit deserialization.</summary>
    public ContentCode()
    {
    }

    /// <summary>Describes one content code.</summary>
    /// <param name="kind">Whether the code is a block, item, entity, or a whole domain.</param>
    /// <param name="code">The fully-qualified code, such as <c>mymod:oak-door</c>.</param>
    /// <param name="steps">The sweep steps this case should run.</param>
    /// <param name="variants">The variant selector, written as comma-separated <c>key=value</c> pairs.</param>
    public ContentCode(ContentKind kind, string code, SweepSteps steps = SweepSteps.All, string variants = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Kind = kind;
        Code = code.Contains(':', StringComparison.Ordinal) ? code : "game:" + code;
        Steps = steps;
        Variants = variants;
    }

    /// <summary>The kind of content.</summary>
    public ContentKind Kind { get; private set; }

    /// <summary>The fully-qualified Vintage Story code.</summary>
    public string Code { get; private set; } = "";

    /// <summary>The steps to run for this theory row.</summary>
    public SweepSteps Steps { get; private set; } = SweepSteps.All;

    /// <summary>Variant values for filters, written as comma-separated <c>key=value</c> pairs.</summary>
    public string Variants { get; private set; } = "";

    /// <summary>The domain before the colon in <see cref="Code"/>.</summary>
    public string Domain => Code[..Code.IndexOf(':')];

    /// <summary>The path after the colon in <see cref="Code"/>.</summary>
    public string Path => Code[(Code.IndexOf(':') + 1)..];

    /// <inheritdoc />
    public override string ToString() => Kind == ContentKind.Domain ? $"{Domain}:*" : $"{Kind}:{Code}";

    void IXunitSerializable.Serialize(IXunitSerializationInfo info)
    {
        info.AddValue(nameof(Kind), (int)Kind);
        info.AddValue(nameof(Code), Code);
        info.AddValue(nameof(Steps), (int)Steps);
        info.AddValue(nameof(Variants), Variants);
    }

    void IXunitSerializable.Deserialize(IXunitSerializationInfo info)
    {
        Kind = (ContentKind)info.GetValue<int>(nameof(Kind));
        Code = info.GetValue<string>(nameof(Code));
        Steps = (SweepSteps)info.GetValue<int>(nameof(Steps));
        Variants = info.GetValue<string>(nameof(Variants));
    }
}

/// <summary>The individual steps <see cref="ContentSweep"/> can run.</summary>
[Flags]
public enum SweepSteps
{
    /// <summary>Do not run any steps.</summary>
    None = 0,
    /// <summary>Place the block through the server's player interaction path.</summary>
    BlockPlace = 1 << 0,
    /// <summary>Tick the placed block and its block entity.</summary>
    BlockTick = 1 << 1,
    /// <summary>Call the block's neighbour-change handler.</summary>
    BlockNeighbourChange = 1 << 2,
    /// <summary>Round-trip the placed block entity's tree attributes.</summary>
    BlockEntityRoundTrip = 1 << 3,
    /// <summary>Resolve the block's drops while holding a suitable tool.</summary>
    BlockDrops = 1 << 4,
    /// <summary>Break the placed block through the server's player interaction path.</summary>
    BlockBreak = 1 << 5,
    /// <summary>Give the item to the test player's hotbar.</summary>
    ItemGive = 1 << 6,
    /// <summary>Call the item's held-idle handler over server ticks.</summary>
    ItemHeldIdle = 1 << 7,
    /// <summary>Call the item's ground-idle handler on a dropped entity item.</summary>
    ItemGroundIdle = 1 << 8,
    /// <summary>Drop the item as an entity and let the test player collect it.</summary>
    ItemCollect = 1 << 9,
    /// <summary>Build the item's held-item information text.</summary>
    ItemHeldItemInfo = 1 << 10,
    /// <summary>Resolve the item's transition states.</summary>
    ItemTransitions = 1 << 11,
    /// <summary>Damage a durable item to zero durability.</summary>
    ItemDurability = 1 << 12,
    /// <summary>Create and initialize the entity.</summary>
    EntitySpawn = 1 << 13,
    /// <summary>Tick the entity and its behaviors.</summary>
    EntityTick = 1 << 14,
    /// <summary>Call the entity's damage handler.</summary>
    EntityDamage = 1 << 15,
    /// <summary>Call the entity's death handler.</summary>
    EntityDie = 1 << 16,
    /// <summary>Despawn the entity from the world.</summary>
    EntityDespawn = 1 << 17,
    /// <summary>Round-trip the entity through its binary save format.</summary>
    EntityRoundTrip = 1 << 18,
    /// <summary>Run every defined step that applies to the content kind.</summary>
    All = (1 << 19) - 1,
}

/// <summary>The result categories in a content sweep report.</summary>
public enum SweepStep
{
    /// <summary>Resolve the code or prepare the test world for its sweep.</summary>
    Resolve,
    /// <inheritdoc cref="SweepSteps.BlockPlace" />
    BlockPlace,
    /// <inheritdoc cref="SweepSteps.BlockTick" />
    BlockTick,
    /// <inheritdoc cref="SweepSteps.BlockNeighbourChange" />
    BlockNeighbourChange,
    /// <inheritdoc cref="SweepSteps.BlockEntityRoundTrip" />
    BlockEntityRoundTrip,
    /// <inheritdoc cref="SweepSteps.BlockDrops" />
    BlockDrops,
    /// <inheritdoc cref="SweepSteps.BlockBreak" />
    BlockBreak,
    /// <inheritdoc cref="SweepSteps.ItemGive" />
    ItemGive,
    /// <inheritdoc cref="SweepSteps.ItemHeldIdle" />
    ItemHeldIdle,
    /// <inheritdoc cref="SweepSteps.ItemGroundIdle" />
    ItemGroundIdle,
    /// <inheritdoc cref="SweepSteps.ItemCollect" />
    ItemCollect,
    /// <inheritdoc cref="SweepSteps.ItemHeldItemInfo" />
    ItemHeldItemInfo,
    /// <inheritdoc cref="SweepSteps.ItemTransitions" />
    ItemTransitions,
    /// <inheritdoc cref="SweepSteps.ItemDurability" />
    ItemDurability,
    /// <inheritdoc cref="SweepSteps.EntitySpawn" />
    EntitySpawn,
    /// <inheritdoc cref="SweepSteps.EntityTick" />
    EntityTick,
    /// <inheritdoc cref="SweepSteps.EntityDamage" />
    EntityDamage,
    /// <inheritdoc cref="SweepSteps.EntityDie" />
    EntityDie,
    /// <inheritdoc cref="SweepSteps.EntityDespawn" />
    EntityDespawn,
    /// <inheritdoc cref="SweepSteps.EntityRoundTrip" />
    EntityRoundTrip,
    /// <summary>Remove the temporary content and compare tick listeners.</summary>
    Cleanup,
}

/// <summary>A content code and step that completed without an exception or logged error.</summary>
public sealed record SweepStepResult(ContentCode Code, SweepStep Step);

/// <summary>A content code and step that failed or produced a logged error.</summary>
/// <param name="Code">The content code under test.</param>
/// <param name="Step">The step that failed.</param>
/// <param name="Message">The failure description.</param>
/// <param name="Exception">The exception, when the step threw.</param>
/// <param name="LoggedErrors">Errors the server logged while running the step.</param>
public sealed record SweepFailure(
    ContentCode Code,
    SweepStep Step,
    string Message,
    Exception? Exception,
    IReadOnlyList<LogEntry> LoggedErrors);

/// <summary>A game tick listener left registered after sweeping one content code.</summary>
/// <param name="Code">The content code whose sweep left the listener.</param>
/// <param name="Listener">The listener's id, handler, owner mod and position.</param>
public sealed record LeakedTickListener(ContentCode Code, ServerTickListenerInfo Listener);

/// <summary>Options for a server-side content sweep.</summary>
public sealed record ContentSweepOptions
{
    /// <summary>The steps to run for each content kind.</summary>
    public SweepSteps Steps { get; init; } = SweepSteps.All;

    /// <summary>Code patterns to omit. <c>*</c> matches any run of characters and <c>?</c> one character.</summary>
    public IReadOnlyList<string> Skip { get; init; } = [];

    /// <summary>
    /// Variant selectors to include, written as <c>key=value</c> pairs. An empty list includes every
    /// variant; a selector may contain <c>*</c> or <c>?</c> in its value.
    /// </summary>
    public IReadOnlyList<string> Variants { get; init; } = [];

    /// <summary>How many server ticks to run for held-idle, entity-tick and post-placement checks.</summary>
    public int Ticks { get; init; } = 3;
}

/// <summary>Results from sweeping one or more blocks, items and entity types.</summary>
public sealed class SweepReport
{
    private readonly IReadOnlyList<SweepStepResult> _passedSteps;

    internal SweepReport(
        IReadOnlyList<ContentCode> blocks,
        IReadOnlyList<ContentCode> items,
        IReadOnlyList<ContentCode> entities,
        IReadOnlyList<ContentCode> withoutBlockEntity,
        IReadOnlyList<SweepStepResult> passedSteps,
        IReadOnlyList<SweepFailure> failures,
        IReadOnlyList<LeakedTickListener> leakedTickListeners)
    {
        Blocks = blocks;
        Items = items;
        Entities = entities;
        WithoutBlockEntity = withoutBlockEntity;
        _passedSteps = passedSteps;
        Failures = failures;
        LeakedTickListeners = leakedTickListeners;
    }

    /// <summary>The block codes included in the sweep.</summary>
    public IReadOnlyList<ContentCode> Blocks { get; }

    /// <summary>The item codes included in the sweep.</summary>
    public IReadOnlyList<ContentCode> Items { get; }

    /// <summary>The entity codes included in the sweep.</summary>
    public IReadOnlyList<ContentCode> Entities { get; }

    /// <summary>Blocks that did not create a block entity when placed.</summary>
    public IReadOnlyList<ContentCode> WithoutBlockEntity { get; }

    /// <summary>Steps that completed without exceptions or logged errors.</summary>
    public IReadOnlyList<SweepStepResult> PassedSteps => _passedSteps;

    /// <summary>Exceptions, rejected interactions and logged errors, with the code and step that produced them.</summary>
    public IReadOnlyList<SweepFailure> Failures { get; }

    /// <summary>Tick listeners registered during a sweep and still present after cleanup.</summary>
    public IReadOnlyList<LeakedTickListener> LeakedTickListeners { get; }

    /// <summary>The distinct content codes that passed <paramref name="step"/>.</summary>
    public IReadOnlyList<ContentCode> Passed(SweepStep step) => _passedSteps
        .Where(result => result.Step == step)
        .Select(result => result.Code)
        .Distinct()
        .ToArray();
}
