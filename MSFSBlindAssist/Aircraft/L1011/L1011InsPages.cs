namespace MSFSBlindAssist.Aircraft.L1011;

/// <summary>
/// An INS's pages are its display selector's positions, Track and ground speed to Test. In the INS window
/// Page Up and Page Down (and Alt+Left, Alt+Right) turn the selector one position through its panel row, the
/// keys that change a PMS page and every CDU window's pages; the knob stops at both ends, as the cockpit's
/// does, and says where it is. The new page is said once the aircraft reports it ([L1011-11]): the screen
/// reader cannot say it, since the cursor is not on the selector. Pure.
/// </summary>
public static class L1011InsPages
{
    /// <summary>Where to turn the selector, or nothing to turn and what to say instead.</summary>
    public readonly record struct Step(double? Target, string? Say);

    public const string Unknown = "Display selector position unknown";

    /// <summary>One position on (<paramref name="direction"/> +1) or back (-1) from the reported one.</summary>
    public static Step Next(IReadOnlyDictionary<double, string> positions, double? reported, int direction)
    {
        var ordered = positions.OrderBy(p => p.Key).ToList();
        int at = reported is double v ? ordered.FindIndex(p => Math.Abs(p.Key - v) < 0.01) : -1;
        if (at < 0)
            return new Step(null, Unknown);
        int to = at + Math.Sign(direction);
        if (to < 0)
            return new Step(null, "First position: " + ordered[at].Value);
        if (to >= ordered.Count)
            return new Step(null, "Last position: " + ordered[at].Value);
        return new Step(ordered[to].Key, null);
    }

    /// <summary>The page a page key asked for, said once the aircraft reports it, within <see cref="WaitMs"/>.</summary>
    public sealed class ReadBack
    {
        /// <summary>The selector row reports a move within about a second; past this the move is not said.</summary>
        public const int WaitMs = 3_000;

        private double? _target;
        private string _words = string.Empty;
        private long _armedAtMs;

        public void Arm(double target, string words, long nowMs)
        {
            _target = target;
            _words = words;
            _armedAtMs = nowMs;
        }

        public string? OnPoll(double? reported, long nowMs)
        {
            if (_target is not double target)
                return null;
            if (nowMs - _armedAtMs > WaitMs)
            {
                _target = null;
                return null;
            }
            if (reported is not double r || Math.Abs(r - target) >= 0.01)
                return null;
            _target = null;
            return _words;
        }
    }
}
