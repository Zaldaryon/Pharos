using System;
using System.Collections.Generic;
using System.Linq;

namespace Zaldaryon.Pharos.World;

/// <summary>
/// Simulates block placement, breaking, and tool usage deterministically.
/// Supports both mock mode for pure-logic testing and live client mode.
/// </summary>
public sealed class BlockInteractionSimulator
{
    private readonly object _lock = new();

    // Mock state for pure-logic testing without live client
    private readonly Dictionary<SimBlockPos, string> _mockBlocks = new();
    private readonly Dictionary<string, string> _blockDrops = new();
    private readonly HashSet<string> _liquidBlocks = new();
    private readonly HashSet<string> _solidBlocks = new();
    private readonly HashSet<string> _activatableBlocks = new();
    private bool _useMockState = true;

    /// <summary>
    /// Creates a BlockInteractionSimulator in mock mode for testing.
    /// </summary>
    public BlockInteractionSimulator()
    {
        // Register common air block
        _liquidBlocks.Add("game:water");
        _liquidBlocks.Add("game:lava");
    }

    /// <summary>
    /// Gets whether mock mode is enabled.
    /// </summary>
    public bool IsMockMode
    {
        get
        {
            lock (_lock)
            {
                return _useMockState;
            }
        }
    }

    /// <summary>
    /// Enables mock mode for pure-logic testing.
    /// </summary>
    public void EnableMockMode()
    {
        lock (_lock)
        {
            _useMockState = true;
        }
    }

    /// <summary>
    /// Disables mock mode (would use live client if available).
    /// </summary>
    public void DisableMockMode()
    {
        lock (_lock)
        {
            _useMockState = false;
        }
    }

    /// <summary>
    /// Registers a block code as a liquid type.
    /// </summary>
    public void RegisterLiquidBlock(string blockCode)
    {
        if (string.IsNullOrEmpty(blockCode)) return;

        lock (_lock)
        {
            _liquidBlocks.Add(blockCode);
        }
    }

    /// <summary>
    /// Registers a block code as a solid type.
    /// </summary>
    public void RegisterSolidBlock(string blockCode)
    {
        if (string.IsNullOrEmpty(blockCode)) return;

        lock (_lock)
        {
            _solidBlocks.Add(blockCode);
        }
    }

    /// <summary>
    /// Registers a block code as activatable (right-click interaction).
    /// </summary>
    public void RegisterActivatableBlock(string blockCode)
    {
        if (string.IsNullOrEmpty(blockCode)) return;

        lock (_lock)
        {
            _activatableBlocks.Add(blockCode);
        }
    }

    /// <summary>
    /// Registers what item a block drops when broken.
    /// </summary>
    public void RegisterBlockDrop(string blockCode, string droppedItemCode)
    {
        if (string.IsNullOrEmpty(blockCode)) return;

        lock (_lock)
        {
            if (string.IsNullOrEmpty(droppedItemCode))
            {
                _blockDrops.Remove(blockCode);
            }
            else
            {
                _blockDrops[blockCode] = droppedItemCode;
            }
        }
    }

    /// <summary>
    /// Places a block at the specified position.
    /// </summary>
    /// <param name="pos">Target position.</param>
    /// <param name="blockCode">Block code to place.</param>
    /// <returns>True if placement succeeded.</returns>
    public bool PlaceBlock(SimBlockPos pos, string blockCode)
    {
        if (string.IsNullOrEmpty(blockCode))
        {
            return false;
        }

        lock (_lock)
        {
            if (!_useMockState)
            {
                // Would delegate to live client here
                return false;
            }

            // Check if position already has a non-replaceable block
            if (_mockBlocks.TryGetValue(pos, out var existingBlock))
            {
                // Can replace air, liquids, or non-solid blocks
                if (existingBlock != "air" && existingBlock != "game:air" &&
                    !_liquidBlocks.Contains(existingBlock))
                {
                    return false; // Position occupied by solid block
                }
            }

            // Check if trying to place liquid in occupied space
            if (_liquidBlocks.Contains(blockCode) && _mockBlocks.TryGetValue(pos, out var current))
            {
                if (_solidBlocks.Contains(current))
                {
                    return false; // Cannot place liquid in solid
                }
            }

            _mockBlocks[pos] = blockCode;
            return true;
        }
    }

    /// <summary>
    /// Breaks a block at the specified position.
    /// </summary>
    /// <param name="pos">Target position.</param>
    /// <returns>Result of the break operation including any drops.</returns>
    public BlockInteractionResult BreakBlock(SimBlockPos pos)
    {
        lock (_lock)
        {
            if (!_useMockState)
            {
                return BlockInteractionResult.Failed(pos, null, "Mock mode disabled, no live client available");
            }

            if (!_mockBlocks.TryGetValue(pos, out var blockCode))
            {
                return BlockInteractionResult.Failed(pos, "air", "No block at position");
            }

            if (blockCode == "air" || blockCode == "game:air")
            {
                return BlockInteractionResult.Failed(pos, blockCode, "Cannot break air");
            }

            // Check for liquid blocks - they can be "broken" (removed) but don't drop items
            bool isLiquid = _liquidBlocks.Contains(blockCode);

            // Remove the block
            _mockBlocks.Remove(pos);

            // Get drop if any (liquids don't drop)
            string? droppedItem = null;
            if (!isLiquid)
            {
                _blockDrops.TryGetValue(blockCode, out droppedItem);
            }

            return BlockInteractionResult.Succeeded(pos, blockCode, droppedItem);
        }
    }

    /// <summary>
    /// Activates a block at the specified position (right-click interaction).
    /// </summary>
    /// <param name="pos">Target position.</param>
    /// <returns>True if activation succeeded.</returns>
    public bool ActivateBlock(SimBlockPos pos)
    {
        lock (_lock)
        {
            if (!_useMockState)
            {
                return false;
            }

            if (!_mockBlocks.TryGetValue(pos, out var blockCode))
            {
                return false; // No block to activate
            }

            // Check if block is activatable
            return _activatableBlocks.Contains(blockCode);
        }
    }

    /// <summary>
    /// Gets the block code at the specified position.
    /// </summary>
    /// <param name="pos">Target position.</param>
    /// <returns>Block code or "air" if empty.</returns>
    public string GetBlock(SimBlockPos pos)
    {
        lock (_lock)
        {
            if (!_useMockState)
            {
                return "air"; // Would query live client
            }

            return _mockBlocks.TryGetValue(pos, out var blockCode) ? blockCode : "air";
        }
    }

    /// <summary>
    /// Checks if a block at the position is a liquid.
    /// </summary>
    public bool IsLiquid(SimBlockPos pos)
    {
        lock (_lock)
        {
            if (_mockBlocks.TryGetValue(pos, out var blockCode))
            {
                return _liquidBlocks.Contains(blockCode);
            }
            return false;
        }
    }

    /// <summary>
    /// Checks if a block at the position is solid.
    /// </summary>
    public bool IsSolid(SimBlockPos pos)
    {
        lock (_lock)
        {
            if (_mockBlocks.TryGetValue(pos, out var blockCode))
            {
                return _solidBlocks.Contains(blockCode);
            }
            return false;
        }
    }

    /// <summary>
    /// Checks if a block at the position is activatable.
    /// </summary>
    public bool IsActivatable(SimBlockPos pos)
    {
        lock (_lock)
        {
            if (_mockBlocks.TryGetValue(pos, out var blockCode))
            {
                return _activatableBlocks.Contains(blockCode);
            }
            return false;
        }
    }

    /// <summary>
    /// Checks if a position is empty (air or no block placed).
    /// </summary>
    public bool IsEmpty(SimBlockPos pos)
    {
        lock (_lock)
        {
            if (!_mockBlocks.TryGetValue(pos, out var blockCode))
            {
                return true;
            }
            return blockCode == "air" || blockCode == "game:air";
        }
    }

    /// <summary>
    /// Gets the total number of placed blocks in mock state.
    /// </summary>
    public int MockBlockCount
    {
        get
        {
            lock (_lock)
            {
                return _mockBlocks.Count;
            }
        }
    }

    /// <summary>
    /// Clears all mock block state.
    /// </summary>
    public void ClearMockBlocks()
    {
        lock (_lock)
        {
            _mockBlocks.Clear();
        }
    }

    /// <summary>
    /// Resets all mock state including blocks, registrations, and drops.
    /// </summary>
    public void ResetMockState()
    {
        lock (_lock)
        {
            _mockBlocks.Clear();
            _blockDrops.Clear();
            _liquidBlocks.Clear();
            _solidBlocks.Clear();
            _activatableBlocks.Clear();

            // Re-register common liquids
            _liquidBlocks.Add("game:water");
            _liquidBlocks.Add("game:lava");
        }
    }

    /// <summary>
    /// Sets a block directly in mock state without placement validation.
    /// Useful for setting up test scenarios.
    /// </summary>
    public void SetMockBlock(SimBlockPos pos, string blockCode)
    {
        lock (_lock)
        {
            if (string.IsNullOrEmpty(blockCode) || blockCode == "air" || blockCode == "game:air")
            {
                _mockBlocks.Remove(pos);
            }
            else
            {
                _mockBlocks[pos] = blockCode;
            }
        }
    }

    /// <summary>
    /// Gets all block positions in the mock world.
    /// </summary>
    public IReadOnlyList<SimBlockPos> GetAllMockBlockPositions()
    {
        lock (_lock)
        {
            return _mockBlocks.Keys.ToArray();
        }
    }
}
