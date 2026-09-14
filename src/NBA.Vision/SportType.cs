namespace NBA.Vision;

/// <summary>
/// Identifies a sport by a stable string id rather than a closed enum, so a new sport can be added purely
/// by registering it in <see cref="CourtGeometryRegistry"/> - no existing type needs a new member. See the
/// sport-classification spec's "Sport registry is extensible without changing the classifier's interface".
/// </summary>
public readonly record struct SportType(string Id)
{
    public static readonly SportType Basketball = new("basketball");

    /// <summary>Confidence was below the acceptance threshold - no sport is committed to.</summary>
    public static readonly SportType Unknown = new("unknown");

    public override string ToString() => Id;
}
