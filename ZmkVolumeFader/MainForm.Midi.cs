namespace ZmkVolumeFader;

public partial class MainForm
{
    MidiInputService? _midi;
    readonly System.Windows.Forms.Timer _midiWatch = new() { Interval = 1000 };
    readonly Dictionary<string, long> _midiSessions = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<(string Key, int Axis), long> _midiLastSeen = new();
    readonly HashSet<(string Key, int Axis)> _midiExpired = new();
    const long QmkTimeoutMs = 3500;
    volatile string[] _midiConnectedNames = Array.Empty<string>();

    void StartMidi()
    {
        try
        {
            _midi = new MidiInputService();
            _midi.Changed += RequestFaderPump;
            _midiWatch.Tick += (_, _) => { RefreshMidiConnections(); ExpireMidiInputs(); };
            RefreshMidiSubscriptions();
        }
        catch (Exception ex) { Program.Log(ex, "Starting MIDI input"); }
    }

    void RefreshMidiSubscriptions()
    {
        var keys = _configuredDeviceSnap.Concat(_captureDeviceSnap).Where(FaderSourceLogic.IsMidi).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _midi?.Monitor(keys);
        if (_midi != null && keys.Count > 0) _midiWatch.Start();
        else _midiWatch.Stop();
        foreach (var key in _midiSessions.Keys.Where(k => !keys.Contains(k)).ToArray())
        {
            ClearMidiSource(key);
            _midiSessions.Remove(key);
        }
    }

    void ClearMidiSource(string key)
    {
        _rawByDevice.Remove(key);
        foreach (var binding in _midiLastSeen.Keys.Where(p => p.Key == key).ToArray()) _midiLastSeen.Remove(binding);
        _midiExpired.RemoveWhere(p => p.Key == key);
        foreach (var a in _sliders.Where(a => a.SourceKey == key)) { a.Sm = -1; a.LastRaw = int.MinValue; }
    }

    void RefreshMidiConnections()
    {
        if (_midi == null) return;
        foreach (var key in _configuredDeviceSnap.Concat(_captureDeviceSnap).Where(FaderSourceLogic.IsMidi).Distinct())
        {
            long session = _midi.Session(key);
            _midiSessions.TryGetValue(key, out long prior);
            if (prior == session) continue;
            ClearMidiSource(key);
            _midiSessions[key] = session;
            MarkSourceConnection(key, false);
            if (session != 0)
            {
                string name = _midi.Sources().FirstOrDefault(s => s.Key == key)?.Name ?? key;
                AddOrConnectSource(key, name);
            }
        }
        _midiConnectedNames = _midi.Sources().Where(s => _midi.IsConnected(s.Key)).Select(s => s.Name).ToArray();
        UpdateAggregateStatus();
    }

    void PumpMidi()
    {
        if (_midi == null) return;
        RefreshMidiConnections();
        foreach (var sample in _midi.Drain())
        {
            long session = _midi.Session(sample.Key);
            if (Program.DiagSynth || session == 0 || session != sample.Session) continue;
            if (_midiSessions.GetValueOrDefault(sample.Key) != session) RefreshMidiConnections();
            if (FaderSourceLogic.IsQmk(sample.Key) && Environment.TickCount64 - sample.At > QmkTimeoutMs) continue;
            if (!_rawByDevice.TryGetValue(sample.Key, out var raw))
                _rawByDevice[sample.Key] = raw = Enumerable.Repeat(int.MinValue, FaderSourceLogic.Axes(sample.Key)).ToArray();
            if (sample.Axis < 0 || sample.Axis >= raw.Length) continue;
            var binding = (sample.Key, sample.Axis);
            bool resumed = _midiExpired.Remove(binding) || !_midiLastSeen.ContainsKey(binding);
            _midiLastSeen[binding] = sample.At;
            int previous = raw[sample.Axis];
            raw[sample.Axis] = sample.Raw;
            foreach (var a in _sliders.Where(a => a.SourceKey == sample.Key && a.AxisIndex == sample.Axis))
            {
                if (resumed) { a.Sm = -1; ArmPickup(a, publish: false); }
                // Heartbeats refresh liveness without stealing target ownership or
                // slowly easing a parked MIDI controller through the HID noise filter.
                if (resumed || previous != sample.Raw || a.Sm < 0) ApplyAxis(a, sample.Raw);
                UpdateSourceLabel(a);
                UpdateSliderState(a);
            }
        }
    }

    void ExpireMidiInputs()
    {
        bool changed = false;
        long now = Environment.TickCount64;
        foreach (var pair in _midiLastSeen)
        {
            if (!FaderSourceLogic.IsQmk(pair.Key.Key) || now - pair.Value <= QmkTimeoutMs || !_midiExpired.Add(pair.Key)) continue;
            if (_rawByDevice.TryGetValue(pair.Key.Key, out var raw)) raw[pair.Key.Axis] = int.MinValue;
            foreach (var a in _sliders.Where(a => a.SourceKey == pair.Key.Key && a.AxisIndex == pair.Key.Axis))
            {
                CancelPickup(a);
                a.Sm = -1;
                a.LastRaw = int.MinValue;
                UpdateSourceLabel(a);
                UpdateSliderState(a);
            }
            changed = true;
        }
        if (changed) PublishDesiredVolumes();
    }

    bool MidiInputLive(Axis a) => !FaderSourceLogic.IsMidi(a.SourceKey) ||
        _midiLastSeen.ContainsKey((a.SourceKey, a.AxisIndex)) && !_midiExpired.Contains((a.SourceKey, a.AxisIndex));

    void StopMidi()
    {
        _midiWatch.Stop();
        _midiWatch.Dispose();
        if (_midi != null) { _midi.Changed -= RequestFaderPump; _midi.Dispose(); _midi = null; }
    }
}
