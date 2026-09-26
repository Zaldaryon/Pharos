using System.Globalization;
using Xunit;
using Zaldaryon.Pharos.Assertions;
using Zaldaryon.Pharos.World;

namespace Zaldaryon.Pharos.Tests.World;

/// <summary>
/// Tests for animation LOD tick rate observability.
/// All tests use synthetic tick data and are headless-safe.
/// </summary>
public sealed class AnimatorTickCounterTests
{
    // -------------------------------------------------------------------------
    // AnimationLodTierResult record tests
    // -------------------------------------------------------------------------

    [Fact]
    public void AnimationLodTierResult_Empty_HasExpectedDefaults()
    {
        AnimationLodTierResult result = AnimationLodTierResult.Empty;

        Assert.Equal(0, result.NearTickCount);
        Assert.Equal(0, result.MidTickCount);
        Assert.Equal(0, result.FarTickCount);
        Assert.Equal(0, result.TotalTickCount);
        Assert.Equal(0.0, result.NearToFarRatio);
        Assert.False(result.ThrottleDetected);
        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void AnimationLodTierResult_Uniform_HasEqualCounts()
    {
        AnimationLodTierResult result = AnimationLodTierResult.Uniform(100);

        Assert.Equal(100, result.NearTickCount);
        Assert.Equal(100, result.MidTickCount);
        Assert.Equal(100, result.FarTickCount);
        Assert.Equal(300, result.TotalTickCount);
        Assert.Equal(1.0, result.NearToFarRatio);
        Assert.False(result.ThrottleDetected);
        Assert.False(result.IsEmpty);
    }

    [Fact]
    public void AnimationLodTierResult_Throttled_HasDecreasedCounts()
    {
        AnimationLodTierResult result = AnimationLodTierResult.Throttled(100);

        Assert.Equal(100, result.NearTickCount);
        Assert.Equal(50, result.MidTickCount);
        Assert.Equal(25, result.FarTickCount);
        Assert.True(result.ThrottleDetected);
        Assert.Equal(0.25, result.NearToFarRatio, precision: 5);
    }

    [Fact]
    public void AnimationLodTierResult_Throttled_CustomDivisors()
    {
        AnimationLodTierResult result = AnimationLodTierResult.Throttled(120, midDivisor: 3, farDivisor: 6);

        Assert.Equal(120, result.NearTickCount);
        Assert.Equal(40, result.MidTickCount);
        Assert.Equal(20, result.FarTickCount);
        Assert.True(result.ThrottleDetected);
    }

    [Fact]
    public void AnimationLodTierResult_ThrottleDetected_WhenFarLessThanHalfNear()
    {
        AnimationLodTierResult result = new(100, 60, 49); // Far < 50% of Near

        Assert.True(result.ThrottleDetected);
    }

    [Fact]
    public void AnimationLodTierResult_ThrottleNotDetected_WhenFarIsHalfOrMore()
    {
        AnimationLodTierResult result = new(100, 60, 50); // Far == 50% of Near

        Assert.False(result.ThrottleDetected);
    }

    [Fact]
    public void AnimationLodTierResult_ThrottleDelta_CalculatesCorrectly()
    {
        AnimationLodTierResult result = new(100, 60, 25);

        Assert.Equal(75, result.ThrottleDelta);
    }

    [Fact]
    public void AnimationLodTierResult_NearToFarRatio_ZeroWhenNearIsZero()
    {
        AnimationLodTierResult result = new(0, 50, 100);

        Assert.Equal(0.0, result.NearToFarRatio);
        Assert.False(result.ThrottleDetected); // NearTickCount == 0
    }

    [Fact]
    public void AnimationLodTierResult_ToString_FormatsCorrectly()
    {
        AnimationLodTierResult result = new(100, 50, 25);
        CultureInfo previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
            string str = result.ToString();

            Assert.Contains("Near=100", str);
            Assert.Contains("Mid=50", str);
            Assert.Contains("Far=25", str);
            Assert.Contains("Ratio=0.250", str);
            Assert.Contains("Throttled=True", str);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    // -------------------------------------------------------------------------
    // AnimatorTickCounter basic tests
    // -------------------------------------------------------------------------

    [Fact]
    public void AnimatorTickCounter_InitialState_Empty()
    {
        AnimatorTickCounter counter = new();

        Assert.Equal(0, counter.TrackedEntityCount);
        Assert.Equal(0, counter.TotalTickCount);
        Assert.Equal(0.0, counter.AverageTickCount);
    }

    [Fact]
    public void AnimatorTickCounter_TrackTick_IncrementsSingleEntity()
    {
        AnimatorTickCounter counter = new();

        counter.TrackTick(1);
        counter.TrackTick(1);
        counter.TrackTick(1);

        Assert.Equal(1, counter.TrackedEntityCount);
        Assert.Equal(3, counter.GetTickCount(1));
        Assert.Equal(3, counter.TotalTickCount);
    }

    [Fact]
    public void AnimatorTickCounter_TrackTick_TracksMultipleEntities()
    {
        AnimatorTickCounter counter = new();

        counter.TrackTick(1);
        counter.TrackTick(2);
        counter.TrackTick(3);

        Assert.Equal(3, counter.TrackedEntityCount);
        Assert.Equal(1, counter.GetTickCount(1));
        Assert.Equal(1, counter.GetTickCount(2));
        Assert.Equal(1, counter.GetTickCount(3));
    }

    [Fact]
    public void AnimatorTickCounter_TrackTicks_AddsBulkTicks()
    {
        AnimatorTickCounter counter = new();

        counter.TrackTicks(1, 10);
        counter.TrackTicks(1, 5);

        Assert.Equal(15, counter.GetTickCount(1));
    }

    [Fact]
    public void AnimatorTickCounter_TrackTicks_IgnoresZeroOrNegative()
    {
        AnimatorTickCounter counter = new();

        counter.TrackTicks(1, 0);
        counter.TrackTicks(2, -5);

        Assert.Equal(0, counter.TrackedEntityCount);
    }

    [Fact]
    public void AnimatorTickCounter_GetTickCount_ReturnsZeroForUnknownEntity()
    {
        AnimatorTickCounter counter = new();

        Assert.Equal(0, counter.GetTickCount(999));
    }

    [Fact]
    public void AnimatorTickCounter_Reset_ClearsAllData()
    {
        AnimatorTickCounter counter = new();
        counter.TrackTick(1);
        counter.TrackTick(2);

        counter.Reset();

        Assert.Equal(0, counter.TrackedEntityCount);
        Assert.Equal(0, counter.GetTickCount(1));
        Assert.Equal(0, counter.GetTickCount(2));
    }

    [Fact]
    public void AnimatorTickCounter_RemoveEntity_RemovesSpecificEntity()
    {
        AnimatorTickCounter counter = new();
        counter.TrackTick(1);
        counter.TrackTick(2);

        bool removed = counter.RemoveEntity(1);

        Assert.True(removed);
        Assert.Equal(1, counter.TrackedEntityCount);
        Assert.Equal(0, counter.GetTickCount(1));
        Assert.Equal(1, counter.GetTickCount(2));
    }

    [Fact]
    public void AnimatorTickCounter_RemoveEntity_ReturnsFalseForUnknown()
    {
        AnimatorTickCounter counter = new();

        bool removed = counter.RemoveEntity(999);

        Assert.False(removed);
    }

    [Fact]
    public void AnimatorTickCounter_GetAllCounts_ReturnsSnapshot()
    {
        AnimatorTickCounter counter = new();
        counter.TrackTicks(1, 10);
        counter.TrackTicks(2, 20);

        var counts = counter.GetAllCounts();

        Assert.Equal(2, counts.Count);
        Assert.Equal(10, counts[1]);
        Assert.Equal(20, counts[2]);
    }

    [Fact]
    public void AnimatorTickCounter_AverageTickCount_CalculatesCorrectly()
    {
        AnimatorTickCounter counter = new();
        counter.TrackTicks(1, 10);
        counter.TrackTicks(2, 30);

        Assert.Equal(20.0, counter.AverageTickCount);
    }

    // -------------------------------------------------------------------------
    // BuildTierResult tests
    // -------------------------------------------------------------------------

    [Fact]
    public void BuildTierResult_WithTierFunction_ClassifiesCorrectly()
    {
        AnimatorTickCounter counter = new();
        // Near entities: 1, 2
        counter.TrackTicks(1, 100);
        counter.TrackTicks(2, 100);
        // Mid entities: 3
        counter.TrackTicks(3, 50);
        // Far entities: 4, 5
        counter.TrackTicks(4, 25);
        counter.TrackTicks(5, 25);

        AnimationLodTierResult result = counter.BuildTierResult(entityId =>
            entityId switch
            {
                1 or 2 => 0, // Near
                3 => 1,      // Mid
                4 or 5 => 2, // Far
                _ => -1      // Unknown
            });

        Assert.Equal(200, result.NearTickCount);
        Assert.Equal(50, result.MidTickCount);
        Assert.Equal(50, result.FarTickCount);
    }

    [Fact]
    public void BuildTierResult_WithEntityLists_ClassifiesCorrectly()
    {
        AnimatorTickCounter counter = new();
        counter.TrackTicks(1, 100);
        counter.TrackTicks(2, 100);
        counter.TrackTicks(3, 50);
        counter.TrackTicks(4, 25);
        counter.TrackTicks(5, 25);

        AnimationLodTierResult result = counter.BuildTierResult(
            nearEntityIds: [1, 2],
            midEntityIds: [3],
            farEntityIds: [4, 5]);

        Assert.Equal(200, result.NearTickCount);
        Assert.Equal(50, result.MidTickCount);
        Assert.Equal(50, result.FarTickCount);
    }

    [Fact]
    public void BuildTierResult_UnknownEntities_Ignored()
    {
        AnimatorTickCounter counter = new();
        counter.TrackTicks(1, 100);

        AnimationLodTierResult result = counter.BuildTierResult(
            nearEntityIds: [1, 999], // 999 doesn't exist
            midEntityIds: [],
            farEntityIds: []);

        Assert.Equal(100, result.NearTickCount);
    }

    // -------------------------------------------------------------------------
    // CreateSynthetic tests
    // -------------------------------------------------------------------------

    [Fact]
    public void CreateSynthetic_CreatesPrePopulatedCounter()
    {
        var counter = AnimatorTickCounter.CreateSynthetic([
            (1, 100),
            (2, 50),
            (3, 25)
        ]);

        Assert.Equal(3, counter.TrackedEntityCount);
        Assert.Equal(100, counter.GetTickCount(1));
        Assert.Equal(50, counter.GetTickCount(2));
        Assert.Equal(25, counter.GetTickCount(3));
    }

    [Fact]
    public void CreateSynthetic_IgnoresZeroAndNegativeCounts()
    {
        var counter = AnimatorTickCounter.CreateSynthetic([
            (1, 100),
            (2, 0),
            (3, -10)
        ]);

        Assert.Equal(1, counter.TrackedEntityCount);
        Assert.Equal(100, counter.GetTickCount(1));
    }

    // -------------------------------------------------------------------------
    // PharosAssert.AnimationLodThrottled tests
    // -------------------------------------------------------------------------

    [Fact]
    public void AnimationLodThrottled_Passes_WhenThrottlingDetected()
    {
        AnimationLodTierResult result = new(100, 50, 24); // Far < 25% of Near

        // Should not throw
        PharosAssert.AnimationLodThrottled(result);
    }

    [Fact]
    public void AnimationLodThrottled_Throws_WhenEmpty()
    {
        AnimationLodTierResult result = AnimationLodTierResult.Empty;

        var ex = Assert.Throws<PharosAssertException>(() =>
            PharosAssert.AnimationLodThrottled(result));

        Assert.Contains("empty", ex.Message.ToLowerInvariant());
    }

    [Fact]
    public void AnimationLodThrottled_Throws_WhenNearIsZero()
    {
        AnimationLodTierResult result = new(0, 50, 25);

        var ex = Assert.Throws<PharosAssertException>(() =>
            PharosAssert.AnimationLodThrottled(result));

        Assert.Contains("Near-tier", ex.Message);
    }

    [Fact]
    public void AnimationLodThrottled_Throws_WhenNotThrottled()
    {
        AnimationLodTierResult result = new(100, 80, 60); // Far >= 50% of Near

        var ex = Assert.Throws<PharosAssertException>(() =>
            PharosAssert.AnimationLodThrottled(result));

        Assert.Contains("throttling not detected", ex.Message.ToLowerInvariant());
    }

    [Fact]
    public void AnimationLodThrottled_Throws_WhenRatioExceedsMax()
    {
        AnimationLodTierResult result = new(100, 50, 30); // Far/Near = 0.30, > 0.25

        var ex = Assert.Throws<PharosAssertException>(() =>
            PharosAssert.AnimationLodThrottled(result, maxFarRatio: 0.25f));

        Assert.Contains("insufficient", ex.Message.ToLowerInvariant());
    }

    [Fact]
    public void AnimationLodThrottled_CustomMaxRatio_Works()
    {
        AnimationLodTierResult result = new(100, 50, 40); // Far/Near = 0.40

        // Should pass with higher max ratio
        PharosAssert.AnimationLodThrottled(result, maxFarRatio: 0.45f);
    }

    // -------------------------------------------------------------------------
    // PharosAssert.AnimationLodNotThrottled tests
    // -------------------------------------------------------------------------

    [Fact]
    public void AnimationLodNotThrottled_Passes_WhenUniform()
    {
        AnimationLodTierResult result = AnimationLodTierResult.Uniform(100);

        // Should not throw
        PharosAssert.AnimationLodNotThrottled(result);
    }

    [Fact]
    public void AnimationLodNotThrottled_Passes_WhenEmpty()
    {
        AnimationLodTierResult result = AnimationLodTierResult.Empty;

        // Should not throw
        PharosAssert.AnimationLodNotThrottled(result);
    }

    [Fact]
    public void AnimationLodNotThrottled_Throws_WhenThrottled()
    {
        AnimationLodTierResult result = new(100, 50, 25); // Far < 50% of Near

        var ex = Assert.Throws<PharosAssertException>(() =>
            PharosAssert.AnimationLodNotThrottled(result));

        Assert.Contains("unexpected", ex.Message.ToLowerInvariant());
    }

    [Fact]
    public void AnimationLodNotThrottled_Throws_WhenBelowMinRatio()
    {
        AnimationLodTierResult result = new(100, 80, 80); // Ratio = 0.80, below 0.90

        var ex = Assert.Throws<PharosAssertException>(() =>
            PharosAssert.AnimationLodNotThrottled(result, minFarRatio: 0.9f));

        Assert.Contains("not uniform", ex.Message.ToLowerInvariant());
    }

    // -------------------------------------------------------------------------
    // PharosAssert.AnimationTicksAtLeast tests
    // -------------------------------------------------------------------------

    [Fact]
    public void AnimationTicksAtLeast_Passes_WhenAboveMinimums()
    {
        AnimationLodTierResult result = new(100, 50, 25);

        // Should not throw
        PharosAssert.AnimationTicksAtLeast(result, minNearTicks: 100, minMidTicks: 50, minFarTicks: 25);
    }

    [Fact]
    public void AnimationTicksAtLeast_Throws_WhenBelowMinimum()
    {
        AnimationLodTierResult result = new(90, 40, 20);

        var ex = Assert.Throws<PharosAssertException>(() =>
            PharosAssert.AnimationTicksAtLeast(result, minNearTicks: 100, minMidTicks: 50, minFarTicks: 25));

        Assert.Contains("Near: 90 < 100", ex.Message);
        Assert.Contains("Mid: 40 < 50", ex.Message);
        Assert.Contains("Far: 20 < 25", ex.Message);
    }

    // -------------------------------------------------------------------------
    // Thread safety tests
    // -------------------------------------------------------------------------

    [Fact]
    public void AnimatorTickCounter_ThreadSafe_ConcurrentTrackTick()
    {
        AnimatorTickCounter counter = new();
        const int iterations = 1000;
        const int threads = 4;

        Parallel.For(0, threads, _ =>
        {
            for (int i = 0; i < iterations; i++)
            {
                counter.TrackTick(1);
            }
        });

        Assert.Equal(iterations * threads, counter.GetTickCount(1));
    }

    [Fact]
    public void AnimatorTickCounter_ThreadSafe_ConcurrentMultipleEntities()
    {
        AnimatorTickCounter counter = new();
        const int iterations = 100;
        const int threads = 4;

        Parallel.For(0, threads, threadId =>
        {
            for (int i = 0; i < iterations; i++)
            {
                counter.TrackTick(threadId);
            }
        });

        Assert.Equal(threads, counter.TrackedEntityCount);
        for (int i = 0; i < threads; i++)
        {
            Assert.Equal(iterations, counter.GetTickCount(i));
        }
    }

    // -------------------------------------------------------------------------
    // Edge case tests
    // -------------------------------------------------------------------------

    [Fact]
    public void AnimationLodTierResult_NegativeValues_HandledGracefully()
    {
        // Should not crash with negative values (shouldn't happen in practice)
        AnimationLodTierResult result = new(-10, -5, -2);

        Assert.Equal(-17, result.TotalTickCount);
        Assert.False(result.IsEmpty);
    }

    [Fact]
    public void AnimatorTickCounter_LargeEntityIds_Handled()
    {
        AnimatorTickCounter counter = new();
        long largeId = long.MaxValue;

        counter.TrackTick(largeId);

        Assert.Equal(1, counter.GetTickCount(largeId));
    }

    [Fact]
    public void BuildTierResult_EmptyLists_ReturnsEmptyResult()
    {
        AnimatorTickCounter counter = new();
        counter.TrackTicks(1, 100);

        AnimationLodTierResult result = counter.BuildTierResult(
            nearEntityIds: [],
            midEntityIds: [],
            farEntityIds: []);

        Assert.True(result.IsEmpty);
    }
}
