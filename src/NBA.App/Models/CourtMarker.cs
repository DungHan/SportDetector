namespace NBA.App.Models;

/// <summary>
/// A projected point plotted on the minimap, in court-space meters. <see cref="Color"/> is the marked
/// player's sampled jersey/upper-body color (<see cref="NBA.Vision.UpperBodyColorSampling"/>, threaded through
/// <see cref="NBA.Tracking.TrackedPlayer"/>) - null when no color was sampled for this track, in which case the
/// view falls back to a neutral fill.
/// </summary>
public sealed record CourtMarker(double X, double Y, string? Label = null, string? StyleKey = null, (byte R, byte G, byte B)? Color = null);
