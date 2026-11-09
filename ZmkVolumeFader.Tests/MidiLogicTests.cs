using System.Text.Json;
using ZmkVolumeFader;

internal static class MidiLogicTests
{
    static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("MIDI: " + name);
    }

    public static void Run()
    {
        var decoder = new MidiCcDecoder();
        Check(!decoder.TryAccept14(0, 16, 103, 0, out _, out _), "coarse value is never exposed");
        Check(decoder.TryAccept14(0, 48, 16, 2, out int axis, out int raw) && axis == 16 && raw == 13200,
            "QMK nominal 3.3 V retains its original 14-bit count");
        Check(!decoder.TryAccept14(0, 48, 17, 3, out _, out _), "fine byte cannot reuse a consumed coarse byte");
        Check(!decoder.TryAccept14(0, 49, 1, 4, out _, out _), "out-of-order or orphan fine byte is ignored");
        decoder.TryAccept14(0, 16, 1, 10, out _, out _);
        Check(!decoder.TryAccept14(0, 48, 2, 111, out _, out _), "old coarse byte expires");
        decoder.TryAccept14(0, 16, 1, 120, out _, out _);
        decoder.TryAccept14(0, 16, 2, 121, out _, out _);
        Check(decoder.TryAccept14(0, 48, 3, 122, out _, out raw) && raw == 259, "latest coarse value wins after a lost fine byte");
        decoder.TryAccept14(0, 16, 1, 130, out _, out _);
        decoder.TryAccept14(1, 16, 2, 130, out _, out _);
        Check(decoder.TryAccept14(1, 48, 4, 131, out axis, out raw) && axis == 48 && raw == 260, "MIDI channels are isolated");
        Check(decoder.TryAccept14(0, 48, 5, 131, out axis, out raw) && axis == 16 && raw == 133, "interleaved channel pair is retained");
        foreach (int endpoint in new[] { 0, 16383 })
        {
            decoder.TryAccept14(15, 31, endpoint >> 7, 200, out _, out _);
            Check(decoder.TryAccept14(15, 63, endpoint & 127, 201, out axis, out raw) && axis == 511 && raw == endpoint,
                "full value and channel ranges are supported");
        }
        Check(!decoder.TryAccept14(16, 16, 0, 0, out _, out _) && !decoder.TryAccept14(0, 64, 0, 0, out _, out _)
            && !decoder.TryAccept14(0, 16, 128, 0, out _, out _), "invalid messages cannot index decoder storage");
        var reconnected = new MidiCcDecoder();
        Check(!reconnected.TryAccept14(0, 48, 1, 0, out _, out _), "new endpoint session has no stale pairing state");

        string key = FaderSourceLogic.MidiKey("endpoint-a", MidiSourceMode.Qmk);
        string generic = FaderSourceLogic.MidiKey("endpoint-a", MidiSourceMode.Cc14);
        string seven = FaderSourceLogic.MidiKey("endpoint-a", MidiSourceMode.Cc7);
        string other = FaderSourceLogic.MidiKey("endpoint-b", MidiSourceMode.Qmk);
        Check(key != other && key.Length < 512, "same-named endpoint IDs remain distinct and bounded");
        Check(key == FaderSourceLogic.MidiKey("ENDPOINT-A", MidiSourceMode.Qmk), "Windows endpoint ID casing does not change identity");
        Check(FaderSourceLogic.CanonicalKey(key.ToLowerInvariant()) == key, "imported source identity is canonicalized");
        var devices = FaderSourceLogic.DeviceChoices(new[]
        {
            new FaderDeviceChoice(key, "ZodiPact [ABC123] · QMK / VIK · 14-bit CC · heartbeat", true),
            new FaderDeviceChoice(generic, "ZodiPact [ABC123] · MIDI · 14-bit CC", true),
            new FaderDeviceChoice(seven, "ZodiPact [ABC123] · MIDI · 7-bit CC", false),
            new FaderDeviceChoice(other, "ZodiPact [DEF456]", true),
            new FaderDeviceChoice("keyboard", "LiberArk68", true),
        });
        Check(devices.Length == 3 && devices.Count(d => FaderSourceLogic.IsMidi(d.Key)) == 2,
            "profiles collapse per endpoint without merging separate same-named devices or HID");
        var pact = devices.Single(d => d.Key == generic);
        Check(pact.Name == "ZodiPact [ABC123]" && pact.Connected,
            "device picker removes legacy profile suffixes and uses 14-bit by default");
        Check(FaderSourceLogic.WithMidiMode(pact.Key, MidiSourceMode.Qmk) == key
            && FaderSourceLogic.WithMidiMode(pact.Key, MidiSourceMode.Cc7) == seven,
            "profile selector restores existing persisted binding keys");
        Check(FaderSourceLogic.WithMidiMode("keyboard", MidiSourceMode.Cc7) == "keyboard",
            "MIDI profile selection does not change HID identity");
        Check(FaderLayoutLogic.BindingKey(key, 16) == FaderLayoutLogic.BindingKey(generic, 16)
            && FaderLayoutLogic.BindingKey(key, 16) == FaderLayoutLogic.BindingKey(seven, 48),
            "one physical CC pair cannot be bound again using another profile");
        Check(FaderLayoutLogic.BindingKey(key, 16) != FaderLayoutLogic.BindingKey(other, 16), "different devices can use identical channel/CC pairs");
        Check(FaderSourceLogic.Axes(key) == 512 && FaderSourceLogic.Axes(seven) == 2048
            && FaderSourceLogic.Axes("keyboard") == 8, "source bounds are transport-specific");
        Check(FaderSourceLogic.ControlName(key, 119) == "Ch 4 · CC 23/55", "fourth address right-hand fader has readable identity");

        var layout = new Settings { SchemaVersion = 4 };
        var identities = new HashSet<int>();
        for (int channel = 0; channel < 4; channel++)
            for (int cc = 16; cc < 24; cc++)
            {
                int expected = channel * 32 + cc;
                decoder.TryAccept14(channel, cc, channel * 8 + cc, 300, out _, out _);
                Check(decoder.TryAccept14(channel, cc + 32, 7, 301, out axis, out raw) && axis == expected && identities.Add(axis),
                    "all 32 firmware controls remain distinct");
                layout.Layout.Add(new SliderConfig { SourceDeviceKey = key, AxisIndex = axis,
                    Cal = new Calibration { Min = 0, Max = 13200, Taper = TaperKind.Straight } });
            }
        layout.Layout.Add(new SliderConfig { SourceDeviceKey = "keyboard", AxisIndex = 7 });
        layout.Layout.Add(new SliderConfig { IsVirtual = true, AxisIndex = -1 });
        SettingsStore.Normalize(layout);
        var restored = SettingsStore.Normalize(JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(layout))!);
        Check(restored.SchemaVersion == 5 && restored.Layout.Count == 34 && restored.Layout[31].AxisIndex == 119
            && restored.Layout[31].Cal.Max == 13200 && restored.Layout[32].AxisIndex == 7 && restored.Layout[33].IsVirtual,
            "settings round-trip preserves 32 MIDI faders beside HID and virtual faders");
        Check(!SettingsRecoveryLogic.SupportsSchema(5, 4), "old builds refuse new source-address schema");
        var emptyV4 = new Settings { SchemaVersion = 4, Devices = new() {
            ["obsolete"] = new LegacyDeviceProfile { Sliders = new() { new SliderConfig() } } } };
        Check(SettingsStore.Normalize(emptyV4).Layout.Count == 0, "schema-four intentional empty layouts do not resurrect legacy profiles");
        var sevenCapture = CalibrationCaptureLogic.Analyze(Enumerable.Range(0, 43).Select(i => i * 3).ToArray(), 127, 127);
        Check(sevenCapture.Good, "7-bit controllers use their own calibration scale");
    }
}
