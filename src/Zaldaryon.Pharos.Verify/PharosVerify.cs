using Argon;
using System;
using System.Text;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Zaldaryon.Pharos.Core;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Zaldaryon.Pharos.UI;

namespace Zaldaryon.Pharos.Verify;

public static class PharosVerify
{
    public static void Initialize()
    {
        var settings = new JsonSerializerSettings();
        settings.Converters.Add(new TreeAttributeConverter());
        settings.Converters.Add(new ItemStackConverter());
        settings.Converters.Add(new BlockPosConverter());
        settings.Converters.Add(new Vec3dConverter());
        settings.Converters.Add(new EntityConverter());
        settings.Converters.Add(new GuiSnapshotConverter());
        settings.Converters.Add(new DialogLayoutConverter());
        settings.Converters.Add(new ItemTooltipConverter());
        VerifierSettings.AddExtraSettings(s => { foreach (var c in settings.Converters) s.Converters.Add(c); });
        VerifierSettings.AddScrubber((StringBuilder sb) =>
        {
            // Round floats to 4 decimals is handled in converters; here scrub obvious ids.
        });
    }

    private sealed class TreeAttributeConverter : JsonConverter<ITreeAttribute>
    {
        public override void WriteJson(JsonWriter w, ITreeAttribute v, JsonSerializer s)
        {
            w.WriteStartObject();
            foreach (var kv in v.OrderBy(x => x.Key))
            {
                w.WritePropertyName(kv.Key);
                w.WriteValue(kv.Value?.ToString());
            }
            w.WriteEndObject();
        }
        public override ITreeAttribute ReadJson(JsonReader r, Type t, ITreeAttribute e, bool h, JsonSerializer s) => throw new NotSupportedException();
    }

    private sealed class ItemStackConverter : JsonConverter<ItemStack>
    {
        public override void WriteJson(JsonWriter w, ItemStack v, JsonSerializer s)
        {
            w.WriteStartObject();
            w.WritePropertyName("Code"); w.WriteValue(v.Collectible?.Code?.ToString());
            w.WritePropertyName("Quantity"); w.WriteValue(v.StackSize);
            w.WriteEndObject();
        }
        public override ItemStack ReadJson(JsonReader r, Type t, ItemStack e, bool h, JsonSerializer s) => throw new NotSupportedException();
    }

    private sealed class BlockPosConverter : JsonConverter<BlockPos>
    {
        public override void WriteJson(JsonWriter w, BlockPos v, JsonSerializer s)
        {
            w.WriteStartObject();
            w.WritePropertyName("X"); w.WriteValue(v.X);
            w.WritePropertyName("Y"); w.WriteValue(v.Y);
            w.WritePropertyName("Z"); w.WriteValue(v.Z);
            w.WriteEndObject();
        }
        public override BlockPos ReadJson(JsonReader r, Type t, BlockPos e, bool h, JsonSerializer s) => throw new NotSupportedException();
    }

    private sealed class Vec3dConverter : JsonConverter<Vec3d>
    {
        public override void WriteJson(JsonWriter w, Vec3d v, JsonSerializer s)
        {
            w.WriteStartObject();
            w.WritePropertyName("X"); w.WriteValue(Math.Round(v.X, 4));
            w.WritePropertyName("Y"); w.WriteValue(Math.Round(v.Y, 4));
            w.WritePropertyName("Z"); w.WriteValue(Math.Round(v.Z, 4));
            w.WriteEndObject();
        }
        public override Vec3d ReadJson(JsonReader r, Type t, Vec3d e, bool h, JsonSerializer s) => throw new NotSupportedException();
    }

    private sealed class EntityConverter : JsonConverter<Entity>
    {
        public override void WriteJson(JsonWriter w, Entity v, JsonSerializer s)
        {
            w.WriteStartObject();
            w.WritePropertyName("Code"); w.WriteValue(v.Code?.ToString());
            w.WritePropertyName("Position"); w.WriteValue(v.ServerPos?.ToString());
            w.WriteEndObject();
        }
        public override Entity ReadJson(JsonReader r, Type t, Entity e, bool h, JsonSerializer s) => throw new NotSupportedException();
    }

    private sealed class GuiSnapshotConverter : JsonConverter<GuiSnapshot>
    {
        public override void WriteJson(JsonWriter w, GuiSnapshot v, JsonSerializer s)
        {
            w.WriteStartObject();
            w.WritePropertyName("Elements"); w.WriteValue(v.ToString());
            w.WriteEndObject();
        }
        public override GuiSnapshot ReadJson(JsonReader r, Type t, GuiSnapshot e, bool h, JsonSerializer s) => throw new NotSupportedException();
    }

    private sealed class DialogLayoutConverter : JsonConverter<DialogLayout>
    {
        public override void WriteJson(JsonWriter w, DialogLayout v, JsonSerializer s)
        {
            w.WriteStartObject();
            w.WritePropertyName("Dialog"); w.WriteValue(v.Dialog);
            w.WritePropertyName("ScreenWidth"); w.WriteValue(v.ScreenWidth);
            w.WritePropertyName("ScreenHeight"); w.WriteValue(v.ScreenHeight);
            w.WriteEndObject();
        }
        public override DialogLayout ReadJson(JsonReader r, Type t, DialogLayout e, bool h, JsonSerializer s) => throw new NotSupportedException();
    }

    private sealed class ItemTooltipConverter : JsonConverter<ItemTooltip>
    {
        public override void WriteJson(JsonWriter w, ItemTooltip v, JsonSerializer s)
        {
            w.WriteStartObject();
            w.WritePropertyName("Title"); w.WriteValue(v.Title);
            w.WritePropertyName("Text"); w.WriteValue(v.Text);
            w.WriteEndObject();
        }
        public override ItemTooltip ReadJson(JsonReader r, Type t, ItemTooltip e, bool h, JsonSerializer s) => throw new NotSupportedException();
    }

    public static class GameVersion
    {
        public static string Suffix() => "1.22.7";
    }
}
