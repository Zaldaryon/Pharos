using Argon;
using VerifyTests;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Network;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.UI;

namespace Zaldaryon.Pharos.Verify;

/// <summary>Registers deterministic game-state converters with Verify before the first verification.</summary>
public static class PharosVerify
{
    private static readonly object s_lock = new();
    private static bool s_initialized;

    /// <summary>Call once at test-assembly initialization. Repeated calls are harmless.</summary>
    public static void Initialize()
    {
        lock (s_lock)
        {
            if (s_initialized) return;
            VerifierSettings.AddExtraSettings(settings =>
            {
                settings.Converters.Insert(0, new GameConverter());
                settings.Converters.Insert(0, new RoundedNumberConverter());
            });
            VerifierSettings.ScrubMembers(name => AttributeSnapshot.IsIdentity(name));
            s_initialized = true;
        }
    }

    /// <summary>The installed game's short version. Kept for callers of the original helper.</summary>
    public static class GameVersion
    {
        /// <summary>Reads the game API version, never a hard-coded release.</summary>
        public static string Suffix() => (string)typeof(Vintagestory.API.Config.GameVersion)
            .GetField(nameof(Vintagestory.API.Config.GameVersion.ShortGameVersion))!.GetRawConstantValue()!;
    }

    private sealed class GameConverter : WriteOnlyJsonConverter
    {
        public override bool CanConvert(Type type) => typeof(ITreeAttribute).IsAssignableFrom(type)
            || typeof(ItemStack).IsAssignableFrom(type) || typeof(AssetLocation).IsAssignableFrom(type)
            || typeof(BlockPos).IsAssignableFrom(type) || typeof(Vec3d).IsAssignableFrom(type)
            || typeof(Entity).IsAssignableFrom(type) || type == typeof(GuiSnapshot) || type == typeof(DialogLayout)
            || type == typeof(ItemTooltip) || type == typeof(ModMessage) || type == typeof(ServerModMessage)
            || type == typeof(HudReadout) || typeof(IGameCalendar).IsAssignableFrom(type);

        public override void Write(VerifyJsonWriter writer, object value)
        {
            object? snapshot = value switch
            {
                ITreeAttribute tree => AttributeSnapshot.Capture(tree),
                ItemStack stack => AttributeSnapshot.Capture(stack),
                AssetLocation code => code.ToString(),
                BlockPos pos => new { pos.X, pos.Y, pos.Z, Dimension = pos.dimension },
                Vec3d vec => new { vec.X, vec.Y, vec.Z },
                Entity entity => new { Code = entity.Code?.ToString(), Position = entity.Pos.XYZ, Attributes = AttributeSnapshot.Capture(entity.WatchedAttributes) },
                GuiSnapshot gui => new { gui.OpenDialogs, gui.HudElements, gui.HasModal, gui.Layouts },
                DialogLayout layout => new
                {
                    layout.Dialog, layout.ScreenWidth, layout.ScreenHeight, layout.GuiScale,
                    Composers = layout.Composers.Select(c => new { c.Composer, c.X, c.Y, c.Width, c.Height }).ToArray(),
                    Elements = layout.Elements.Select(e => new { e.Composer, e.Key, e.Type, e.X, e.Y, e.Width, e.Height, e.Text, e.IsInteractive, e.IsClipped }).ToArray()
                },
                ItemTooltip tooltip => new { tooltip.Title, tooltip.Lines },
                ModMessage message => new { message.Direction, message.Channel, message.Udp, message.MessageId, message.TypeName, Data = Convert.ToHexString(message.Data), message.Injected },
                ServerModMessage message => new { message.Direction, message.Channel, message.Udp, message.MessageId, message.TypeName, message.Position, Data = Convert.ToHexString(message.Data) },
                HudReadout hud => new { hud.Dialogs },
                IGameCalendar calendar => new { Date = "{Calendar}", calendar.HoursPerDay, calendar.DaysPerMonth, calendar.DaysPerYear, calendar.CalendarSpeedMul, calendar.SpeedOfTime, calendar.SeasonOverride },
                _ => throw new NotSupportedException(value.GetType().FullName)
            };
            writer.Serialize(snapshot!);
        }
    }

    private sealed class RoundedNumberConverter : JsonConverter
    {
        public override bool CanRead => false;
        public override bool CanConvert(Type type) => type == typeof(float) || type == typeof(double) || type == typeof(decimal);
        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            if (value is decimal number) writer.WriteValue(Math.Round(number, 4));
            else writer.WriteValue(Math.Round(Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture), 4));
        }
        public override object ReadJson(JsonReader reader, Type type, object? value, JsonSerializer serializer) => throw new NotSupportedException();
    }
}
