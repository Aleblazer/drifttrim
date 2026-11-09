namespace ZmkVolumeFader;

/// <summary>Pairs fresh MSB/LSB CCs without exposing intermediate coarse values.</summary>
internal sealed class MidiCcDecoder
{
    public const int PairTimeoutMs = 100;
    readonly int[] _coarse = new int[512];
    readonly long[] _at = new long[512];
    readonly bool[] _pending = new bool[512];

    public bool TryAccept14(int channel, int controller, int value, long now,
        out int axis, out int raw)
    {
        axis = raw = 0;
        if (channel is < 0 or > 15 || controller is < 0 or > 63 || value is < 0 or > 127) return false;
        axis = channel * 32 + controller % 32;
        if (controller < 32)
        {
            _coarse[axis] = value;
            _at[axis] = now;
            _pending[axis] = true;
            return false;
        }
        bool fresh = _pending[axis] && now - _at[axis] is >= 0 and <= PairTimeoutMs;
        _pending[axis] = false;
        if (!fresh) return false;
        raw = (_coarse[axis] << 7) | value;
        return true;
    }
}
