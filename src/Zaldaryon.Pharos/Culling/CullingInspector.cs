using System.Collections.Generic;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using Zaldaryon.Pharos.Timing;

namespace Zaldaryon.Pharos.Culling;

/// <summary>
/// On-demand inspector for per-frame frustum culling state of the headless client.
/// Call <see cref="Snapshot"/> after each frame to capture which chunks were visible
/// or culled. No background tracking: all work happens inside <see cref="Snapshot"/>.
/// </summary>
public sealed class CullingInspector
{
    private readonly ClientMain _client;

    // --- Cached reflection accessors (computed once per type, not per call) ---
    private static readonly FieldInfo? s_frustumCullerField =
        typeof(ClientMain).GetField("frustumCuller", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

    private static readonly FieldInfo? s_frustumField =
        typeof(FrustumCulling).GetField("frustum", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

    private static readonly FieldInfo? s_chunksField =
        typeof(ClientWorldMap).GetField("chunks", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

    private static readonly FieldInfo? s_chunksLockField =
        typeof(ClientWorldMap).GetField("chunksLock", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

    public CullingInspector(ClientMain client)
    {
        _client = client;
    }

    /// <summary>
    /// Reads current frustum and chunk state and returns an immutable snapshot.
    /// Safe to call from the test thread after a frame step.
    /// </summary>
    public CullingSnapshot Snapshot()
    {
        FrustumCulling? culler = s_frustumCullerField?.GetValue(_client) as FrustumCulling;

        IReadOnlyList<FrustumPlane> planes = ReadFrustumPlanes(culler);

        ClientWorldMap worldMap = (ClientWorldMap)_client.WorldMap;
        if (worldMap == null)
        {
            return new CullingSnapshot
            {
                FrustumPlanes = planes,
            };
        }

        Dictionary<long, ClientChunk>? chunksDict =
            s_chunksField?.GetValue(worldMap) as Dictionary<long, ClientChunk>;

        if (chunksDict == null)
        {
            return new CullingSnapshot
            {
                FrustumPlanes = planes,
            };
        }

        List<ChunkPos> visible = new();
        List<ChunkPos> culled = new();
        List<ChunkPos> occluded = new();
        int testCount = 0;

        object? chunksLock = s_chunksLockField?.GetValue(worldMap) ?? new object();
        long mulX = worldMap.index3dMulX;
        long mulZ = worldMap.index3dMulZ;
        Vec3i pos = new();

        lock (chunksLock)
        {
            foreach (KeyValuePair<long, ClientChunk> kvp in chunksDict)
            {
                ClientChunk chunk = kvp.Value;
                if (chunk == null) continue;

                MapUtil.PosInt3d(kvp.Key, mulX, mulZ, pos);
                ChunkPos chunkPos = new(pos.X, pos.Y, pos.Z);

                testCount++;

                bool inFrustum;
                if (culler != null)
                {
                    Sphere sphere = Sphere.BoundingSphereForCube(
                        pos.X * 32, pos.Y * 32, pos.Z * 32, 32);
                    inFrustum = culler.InFrustumAndRange(sphere, chunk.CullVisible[ClientChunk.bufIndex], 1);
                }
                else
                {
                    // No frustum available: treat all chunks as visible
                    inFrustum = true;
                }

                if (inFrustum)
                {
                    visible.Add(chunkPos);
                    // Occlusion (cave) culling: the chunk passed the frustum but the
                    // ChunkCuller has marked it invisible via the CullVisible double-buffer.
                    if (!chunk.CullVisible[ClientChunk.bufIndex])
                    {
                        occluded.Add(chunkPos);
                    }
                }
                else
                {
                    culled.Add(chunkPos);
                }
            }
        }

        return new CullingSnapshot
        {
            VisibleChunks = visible,
            CulledChunks = culled,
            OcclusionCulledChunks = occluded,
            FrustumPlanes = planes,
            TestCount = testCount,
            TotalChunks = testCount,
        };
    }

    /// <summary>
    /// Returns the current six frustum plane equations via reflection.
    /// Returns an empty list when no frustum culler is active.
    /// </summary>
    public IReadOnlyList<FrustumPlane> GetFrustumPlanes()
    {
        FrustumCulling? culler = s_frustumCullerField?.GetValue(_client) as FrustumCulling;
        return ReadFrustumPlanes(culler);
    }

    /// <summary>
    /// Returns BFS visibility traversal statistics. In headless test scenarios where
    /// live BFS state is not accessible, returns <see cref="BfsVisibilityStats.Empty"/>.
    /// Use <see cref="BfsDebugHook"/> for simulated BFS traversal in unit tests.
    /// </summary>
    public BfsVisibilityStats BfsSnapshot()
    {
        // Live BFS state is not directly accessible via reflection in headless mode.
        // Tests should use BfsDebugHook for simulated BFS traversal validation.
        return BfsVisibilityStats.Empty;
    }

    private static IReadOnlyList<FrustumPlane> ReadFrustumPlanes(FrustumCulling? culler)
    {
        if (culler == null) return [];

        Plane[]? planes = s_frustumField?.GetValue(culler) as Plane[];
        if (planes == null || planes.Length == 0) return [];

        FrustumPlane[] result = new FrustumPlane[planes.Length];
        for (int i = 0; i < planes.Length; i++)
        {
            result[i] = new FrustumPlane(planes[i].normalX, planes[i].normalY, planes[i].normalZ, planes[i].D);
        }
        return result;
    }
}
