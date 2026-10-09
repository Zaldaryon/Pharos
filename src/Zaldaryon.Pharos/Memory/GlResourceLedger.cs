using System.Diagnostics;

namespace Zaldaryon.Pharos.Memory;

/// <summary>The kinds of OpenGL objects Pharos tracks.</summary>
[Flags]
public enum GlResourceKind
{
    /// <summary>No kind.</summary>
    None = 0,

    /// <summary>Buffers: <c>glGenBuffers</c> and <c>glDeleteBuffers</c>.</summary>
    Buffer = 1,

    /// <summary>Vertex arrays: <c>glGenVertexArrays</c> and <c>glDeleteVertexArrays</c>.</summary>
    VertexArray = 2,

    /// <summary>Textures: <c>glGenTextures</c> and <c>glDeleteTextures</c>.</summary>
    Texture = 4,

    /// <summary>Framebuffers: <c>glGenFramebuffers</c> and <c>glDeleteFramebuffers</c>.</summary>
    Framebuffer = 8,

    /// <summary>Renderbuffers: <c>glGenRenderbuffers</c> and <c>glDeleteRenderbuffers</c>.</summary>
    Renderbuffer = 16,

    /// <summary>Every kind.</summary>
    All = Buffer | VertexArray | Texture | Framebuffer | Renderbuffer,
}

/// <summary>
/// The OpenGL objects created and not yet deleted on one client's thread while at least one
/// <see cref="GlResourceScope"/> is open, each with a generation number that tells a reused id
/// from the object that had it before.
/// </summary>
/// <remarks>
/// Each GL context has ids of its own, so the ledger takes only the calls made on the thread of
/// the client whose scope opened it. Calls from any other thread are counted and otherwise left out.
/// </remarks>
internal sealed class GlResourceLedger
{
    private static readonly object s_gate = new();
    private static GlResourceLedger? s_live;
    private static int s_scopes;

    private readonly object _lock = new();
    private readonly Dictionary<(GlResourceKind Kind, uint Id), Entry> _live = [];
    private readonly Func<long> _frame;
    private long _generation;
    private int _stackScopes;

    private GlResourceLedger(int threadId, Func<long> frame)
    {
        ThreadId = threadId;
        _frame = frame;
    }

    /// <summary>An object a scope saw created and not deleted.</summary>
    internal sealed record Entry(GlResourceKind Kind, uint Id, long Generation, long Frame, StackTrace? Stack);

    /// <summary>The ledger the hooks write to, or null while no scope is open.</summary>
    public static GlResourceLedger? Live => Volatile.Read(ref s_live);

    /// <summary>The client thread whose GL calls the ledger takes.</summary>
    public int ThreadId { get; }

    /// <summary>Ids the driver returned as 0: no context, or an error.</summary>
    public int ZeroIds { get; private set; }

    /// <summary>Deletions of objects created before the ledger started.</summary>
    public int PreexistingDeleted { get; private set; }

    /// <summary>Ids handed out again while the ledger still had them live: deleted where it could not see.</summary>
    public int Reissued { get; private set; }

    /// <summary>Calls made on another thread, which the ledger left out.</summary>
    public int OtherThreadCalls { get; private set; }

    /// <summary>The generation of the last object created.</summary>
    public long Generation
    {
        get
        {
            lock (_lock) return _generation;
        }
    }

    /// <summary>Opens a scope: starts the ledger if it is the first, on <paramref name="threadId"/>.</summary>
    /// <exception cref="InvalidOperationException">A scope is open for another client thread.</exception>
    public static GlResourceLedger Open(int threadId, Func<long> frame, bool captureStacks)
    {
        lock (s_gate)
        {
            GlResourceLedger ledger = s_live ??= new GlResourceLedger(threadId, frame);
            if (ledger.ThreadId != threadId)
            {
                throw new InvalidOperationException("GL resources are already tracked for another client; close that scope first.");
            }

            s_scopes++;
            if (captureStacks) Interlocked.Increment(ref ledger._stackScopes);
            return ledger;
        }
    }

    /// <summary>Closes a scope: the last one stops the ledger.</summary>
    public static void Close(GlResourceLedger ledger, bool captureStacks)
    {
        lock (s_gate)
        {
            if (captureStacks) Interlocked.Decrement(ref ledger._stackScopes);
            if (--s_scopes == 0 && ReferenceEquals(s_live, ledger)) Volatile.Write(ref s_live, null);
        }
    }

    public void OnCreated(GlResourceKind kind, uint id, int threadId)
    {
        if (!OnOwnThread(threadId)) return;
        StackTrace? stack = Volatile.Read(ref _stackScopes) > 0 ? new StackTrace(0, fNeedFileInfo: true) : null;
        lock (_lock)
        {
            if (id == 0)
            {
                ZeroIds++;
                return;
            }

            if (_live.ContainsKey((kind, id))) Reissued++;
            _live[(kind, id)] = new Entry(kind, id, ++_generation, _frame(), stack);
        }
    }

    public void OnDeleted(GlResourceKind kind, uint id, int threadId)
    {
        if (id == 0 || !OnOwnThread(threadId)) return;
        lock (_lock)
        {
            if (!_live.Remove((kind, id))) PreexistingDeleted++;
        }
    }

    /// <summary>The objects created after <paramref name="sinceGeneration"/> that are still live.</summary>
    public IReadOnlyList<Entry> LiveSince(long sinceGeneration, GlResourceKind kinds)
    {
        lock (_lock)
        {
            return [.. _live.Values.Where(e => e.Generation > sinceGeneration && (e.Kind & kinds) != 0).OrderBy(e => e.Generation)];
        }
    }

    private bool OnOwnThread(int threadId)
    {
        if (threadId == ThreadId) return true;
        lock (_lock) OtherThreadCalls++;
        return false;
    }
}
