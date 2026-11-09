using System.Security.Cryptography;
using System.Text;

namespace ZmkVolumeFader;

internal enum MidiSourceMode { Qmk, Cc14, Cc7 }
internal sealed record FaderDeviceChoice(string Key, string Name, bool Connected);

/// <summary>Transport-independent source identity, control addressing and units.</summary>
internal static class FaderSourceLogic
{
    public static string MidiKey(string endpointId, MidiSourceMode mode) =>
        $"midi:{mode.ToString().ToLowerInvariant()}:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(endpointId.ToUpperInvariant())))}";

    public static bool TryMidiMode(string? key, out MidiSourceMode mode)
    {
        mode = default;
        if (key == null) return false;
        var parts = key.Split(':');
        return parts.Length == 3 && parts[0].Equals("midi", StringComparison.OrdinalIgnoreCase)
            && parts[2].Length == 64 && parts[2].All(Uri.IsHexDigit)
            && Enum.TryParse(parts[1], true, out mode) && Enum.IsDefined(mode);
    }

    public static bool IsMidi(string? key) => TryMidiMode(key, out _);
    public static string CanonicalKey(string key) => TryMidiMode(key, out var mode)
        ? $"midi:{mode.ToString().ToLowerInvariant()}:{key.Split(':')[2].ToUpperInvariant()}" : key;
    public static string WithMidiMode(string key, MidiSourceMode mode) => IsMidi(key)
        ? $"midi:{mode.ToString().ToLowerInvariant()}:{key.Split(':')[2].ToUpperInvariant()}" : key;
    public static string PickerDeviceKey(string key) => WithMidiMode(key, MidiSourceMode.Cc14);
    public static string MidiProfileLabel(MidiSourceMode mode) => mode switch
    {
        MidiSourceMode.Qmk => "14-bit CC (QMK/VIK heartbeat)",
        MidiSourceMode.Cc7 => "7-bit CC",
        _ => "14-bit CC",
    };

    public static string DeviceName(string key, string name)
    {
        if (!IsMidi(key)) return name;
        // Older saved bindings included the profile in the device's display name.
        foreach (string suffix in new[] { " · MIDI · 14-bit CC", " · MIDI · 7-bit CC", " · QMK / VIK · 14-bit CC · heartbeat" })
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return name[..^suffix.Length];
        return name;
    }

    public static FaderDeviceChoice[] DeviceChoices(IEnumerable<FaderDeviceChoice> sources) => sources
        .GroupBy(s => PickerDeviceKey(s.Key), StringComparer.OrdinalIgnoreCase)
        .Select(group =>
        {
            var source = group.OrderByDescending(s => s.Connected).First();
            return new FaderDeviceChoice(group.Key, DeviceName(source.Key, source.Name), group.Any(s => s.Connected));
        }).OrderByDescending(s => s.Connected).ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    public static bool IsQmk(string key) => TryMidiMode(key, out var mode) && mode == MidiSourceMode.Qmk;
    public static int Axes(string? key) => TryMidiMode(key, out var mode) ? (mode == MidiSourceMode.Cc7 ? 16 * 128 : 16 * 32) : 8;
    public static int RawMaximum(string? key) => TryMidiMode(key, out var mode) ? (mode == MidiSourceMode.Cc7 ? 127 : 16383) : 3250;
    public static int TravelThreshold(string key) => IsMidi(key) ? Math.Max(16, RawMaximum(key) / 12) : 1500;
    public static string Units(string key) => IsMidi(key) ? "counts" : "mV";
    public static string ControlName(string key, int axis)
    {
        if (!TryMidiMode(key, out var mode)) return $"axis {axis + 1}";
        int width = mode == MidiSourceMode.Cc7 ? 128 : 32;
        int cc = axis % width;
        return $"Ch {axis / width + 1} · CC {cc}" + (width == 32 ? $"/{cc + 32}" : "");
    }

    // A coarse and fine pair cannot be assigned again via another MIDI profile.
    public static string BindingKey(string key, int axis)
    {
        if (!TryMidiMode(key, out var mode)) return $"{key.Trim()}\n{axis}";
        int width = mode == MidiSourceMode.Cc7 ? 128 : 32;
        int cc = axis % width;
        if (cc is >= 32 and < 64) cc -= 32;
        return $"midi:{key.Split(':')[2]}\n{axis / width}:{cc}";
    }
}
