using Windows.Devices.Enumeration;
using Windows.Devices.Midi;

namespace ZmkVolumeFader;

/// <summary>Event-driven Windows MIDI discovery, endpoint lifetime and bounded latest-value input.</summary>
internal sealed class MidiInputService : IDisposable
{
    internal sealed record Source(string Key, string Name);
    internal readonly record struct Sample(string Key, int Axis, int Raw, long At, long Order, long Session);
    sealed class Input
    {
        public required string Id;
        public required MidiInPort Port;
        public long Generation;
        public readonly MidiCcDecoder Decoder = new();
        public readonly Dictionary<MidiSourceMode, string> Keys = new();
    }

    readonly object _gate = new();
    readonly Dictionary<string, DeviceInformation> _devices = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, Input> _inputs = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<(string, int), Sample> _pending = new();
    HashSet<string> _wanted = new(StringComparer.OrdinalIgnoreCase);
    readonly DeviceWatcher _watcher;
    readonly SemaphoreSlim _wake = new(0, 1);
    readonly CancellationTokenSource _stop = new();
    readonly Task _worker;
    readonly TaskCompletionSource _enumerated = new(TaskCreationOptions.RunContinuationsAsynchronously);
    bool _disposed;
    long _messages;
    long _generation;
    long _sampleOrder;
    public event Action? Changed;
    public long Messages => Interlocked.Read(ref _messages);
    public string? LastError { get; private set; }
    public Task InitialEnumeration => _enumerated.Task;

    public MidiInputService()
    {
        _watcher = DeviceInformation.CreateWatcher(MidiInPort.GetDeviceSelector());
        _watcher.Added += Added;
        _watcher.Removed += Removed;
        _watcher.Updated += Updated;
        _watcher.EnumerationCompleted += Enumerated;
        _worker = Task.Run(RunAsync);
        try { _watcher.Start(); }
        catch { _stop.Cancel(); throw; }
    }

    void Added(DeviceWatcher _, DeviceInformation device)
    {
        lock (_gate) { if (_disposed) return; _devices[device.Id] = device; }
        Wake();
        Changed?.Invoke();
    }
    void Removed(DeviceWatcher _, DeviceInformationUpdate update)
    {
        lock (_gate) _devices.Remove(update.Id);
        Wake();
    }
    void Updated(DeviceWatcher _, DeviceInformationUpdate update)
    {
        lock (_gate) if (_devices.TryGetValue(update.Id, out var device)) device.Update(update);
        Wake();
    }
    void Enumerated(DeviceWatcher _, object args) => _enumerated.TrySetResult();

    public Source[] Sources()
    {
        lock (_gate) return _devices.Values.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .SelectMany(d => Enum.GetValues<MidiSourceMode>().Select(mode =>
            {
                string key = FaderSourceLogic.MidiKey(d.Id, mode);
                return new Source(key, $"{d.Name} [{key[^6..]}]");
            })).ToArray();
    }

    public void Monitor(IEnumerable<string> keys)
    {
        var wanted = keys.Where(FaderSourceLogic.IsMidi).ToHashSet(StringComparer.OrdinalIgnoreCase);
        lock (_gate)
        {
            if (_disposed || _wanted.SetEquals(wanted)) return;
            _wanted = wanted;
            foreach (var key in _pending.Keys.Where(p => !_wanted.Contains(p.Item1)).ToArray()) _pending.Remove(key);
        }
        Wake();
    }

    public bool IsConnected(string key)
    {
        lock (_gate) return _wanted.Contains(key) && _inputs.Values.Any(input => input.Keys.ContainsValue(key));
    }

    public Sample[] Drain()
    {
        lock (_gate)
        {
            var result = _pending.Values.OrderBy(s => s.Order).ToArray();
            _pending.Clear();
            return result;
        }
    }
    public long Session(string key)
    {
        lock (_gate) return _wanted.Contains(key)
            ? _inputs.Values.FirstOrDefault(input => input.Keys.ContainsValue(key))?.Generation ?? 0 : 0;
    }
    public bool HasPending { get { lock (_gate) return _pending.Count > 0; } }

    void Wake()
    {
        try { _wake.Release(); } catch (SemaphoreFullException) { } catch (ObjectDisposedException) { }
    }

    async Task RunAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                DeviceInformation[] devices;
                HashSet<string> wanted;
                lock (_gate) { devices = _devices.Values.ToArray(); wanted = new(_wanted, StringComparer.OrdinalIgnoreCase); }
                bool Desired(DeviceInformation d) => Enum.GetValues<MidiSourceMode>().Any(mode => wanted.Contains(FaderSourceLogic.MidiKey(d.Id, mode)));
                var desired = devices.Where(Desired).ToDictionary(d => d.Id, StringComparer.OrdinalIgnoreCase);
                Input[] close;
                lock (_gate) close = _inputs.Values.Where(input => !desired.ContainsKey(input.Id)).ToArray();
                foreach (var input in close) Close(input);
                foreach (var device in desired.Values)
                {
                    lock (_gate) { if (_disposed) return; if (_inputs.ContainsKey(device.Id)) continue; }
                    try
                    {
                        var port = await MidiInPort.FromIdAsync(device.Id);
                        if (port == null) throw new IOException($"MIDI input could not be opened: {device.Name}");
                        var input = new Input { Id = device.Id, Port = port, Generation = Interlocked.Increment(ref _generation) };
                        foreach (var mode in Enum.GetValues<MidiSourceMode>()) input.Keys[mode] = FaderSourceLogic.MidiKey(device.Id, mode);
                        lock (_gate)
                        {
                            // An open can finish after unplug, deselection or shutdown.
                            if (_disposed || !_devices.ContainsKey(device.Id) || !input.Keys.Values.Any(_wanted.Contains))
                            { port.Dispose(); continue; }
                            try { port.MessageReceived += Received; }
                            catch { port.Dispose(); throw; }
                            _inputs.Add(device.Id, input);
                        }
                        LastError = null;
                        Changed?.Invoke();
                    }
                    catch (Exception ex)
                    {
                        LastError = ex.Message;
                        Program.LogRateLimited($"midi-open:{device.Id}", ex, "Opening MIDI input");
                    }
                }
                bool retry;
                lock (_gate) retry = desired.Keys.Any(id => !_inputs.ContainsKey(id));
                await _wake.WaitAsync(retry ? TimeSpan.FromSeconds(5) : Timeout.InfiniteTimeSpan, _stop.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { LastError = ex.Message; Program.Log(ex, "MIDI discovery"); }
        finally
        {
            Input[] close;
            lock (_gate) close = _inputs.Values.ToArray();
            foreach (var input in close) Close(input);
        }
    }

    void Received(MidiInPort port, MidiMessageReceivedEventArgs args)
    {
        try
        {
            if (args.Message is not MidiControlChangeMessage cc || cc.Controller >= 120) return;
            Interlocked.Increment(ref _messages);
            if (Program.DiagSink) return;
            long now = Environment.TickCount64;
            bool queued = false;
            lock (_gate)
            {
                if (_disposed || !_inputs.TryGetValue(port.DeviceId, out var input) || input.Port != port) return;
                void Publish(MidiSourceMode mode, int axis, int value)
                {
                    string key = input.Keys[mode];
                    if (!_wanted.Contains(key)) return;
                    _pending[(key, axis)] = new Sample(key, axis, value, now, ++_sampleOrder, input.Generation);
                    queued = true;
                }
                Publish(MidiSourceMode.Cc7, cc.Channel * 128 + cc.Controller, cc.ControlValue);
                if (input.Decoder.TryAccept14(cc.Channel, cc.Controller, cc.ControlValue, now, out int axis, out int raw))
                {
                    Publish(MidiSourceMode.Cc14, axis, raw);
                    Publish(MidiSourceMode.Qmk, axis, raw);
                }
            }
            if (queued) Changed?.Invoke();
        }
        catch (Exception ex) { Program.LogRateLimited("midi-receive", ex, "Receiving MIDI CC"); }
    }

    void Close(Input input)
    {
        lock (_gate)
        {
            if (!_inputs.Remove(input.Id)) return;
            foreach (var key in _pending.Keys.Where(p => input.Keys.ContainsValue(p.Item1)).ToArray()) _pending.Remove(key);
        }
        try { input.Port.MessageReceived -= Received; input.Port.Dispose(); }
        catch (Exception ex) { Program.LogRateLimited("midi-close", ex, "Closing MIDI input"); }
        Changed?.Invoke();
    }

    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; _pending.Clear(); }
        _watcher.Added -= Added;
        _watcher.Removed -= Removed;
        _watcher.Updated -= Updated;
        _watcher.EnumerationCompleted -= Enumerated;
        try { _watcher.Stop(); } catch (Exception ex) { Program.LogRateLimited("midi-stop", ex); }
        _stop.Cancel();
        // Do not wait on WinRT opens from the UI thread. The worker owns cleanup.
        _ = _worker.ContinueWith(_ => { _wake.Dispose(); _stop.Dispose(); }, TaskScheduler.Default);
    }
}
