namespace NBA.App.Models;

/// <summary>
/// A projected point plotted on the minimap, in court-space meters. <see cref="Color"/> is the marked
/// player's sampled jersey/upper-body color (<see cref="NBA.Vision.UpperBodyColorSampling"/>, threaded through
/// <see cref="NBA.Tracking.TrackedPlayer"/>) - null when no color was sampled for this track, in which case the
/// view falls back to a neutral fill. <see cref="Opacity"/> is less than 1 for a track currently coasting on
/// motion prediction rather than a real detection this frame (<see cref="NBA.Tracking.TrackedPlayer.FramesSinceMatch"/>
/// &gt; 0) - the marker stays on the minimap through a brief detection miss instead of disappearing, just faded,
/// so a temporary tracking failure reads as "still there, briefly uncertain" rather than "player gone".
/// </summary>
public sealed record CourtMarker(double X, double Y, string? Label = null, string? StyleKey = null, (byte R, byte G, byte B)? Color = null, double Opacity = 1.0);
