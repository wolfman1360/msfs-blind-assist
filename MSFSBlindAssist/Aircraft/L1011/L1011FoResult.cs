namespace MSFSBlindAssist.Aircraft.L1011;

/// <summary>How a First Officer write ended.</summary>
public enum L1011FoOutcome
{
    /// <summary>The write was sent.</summary>
    Sent,
    /// <summary>The control already sat where the step wanted it; nothing was sent.</summary>
    AlreadyThere,
    /// <summary>Not sent, on purpose: a guard stopped it (an unknown toggle position, a closed gate).</summary>
    Refused,
    /// <summary>Not sent: no way to write was available (not connected, no calculator path).</summary>
    Unavailable,
    /// <summary>Not sent: the control key is not one the aircraft's map knows.</summary>
    UnknownKey,
}

/// <summary>
/// The result of one First Officer write: what happened, and for a refusal why.
/// <see cref="Done"/> is true when the control is where the step wanted it.
/// </summary>
public readonly record struct L1011FoResult(L1011FoOutcome Outcome, string? Reason = null)
{
    public bool Done => Outcome is L1011FoOutcome.Sent or L1011FoOutcome.AlreadyThere;

    public static L1011FoResult Sent => new(L1011FoOutcome.Sent);
    public static L1011FoResult AlreadyThere => new(L1011FoOutcome.AlreadyThere);
    public static L1011FoResult Unavailable => new(L1011FoOutcome.Unavailable);
    public static L1011FoResult UnknownKey => new(L1011FoOutcome.UnknownKey);
    public static L1011FoResult Refused(string why) => new(L1011FoOutcome.Refused, why);
}
