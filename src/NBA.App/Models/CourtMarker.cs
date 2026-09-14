namespace NBA.App.Models;

/// <summary>A projected point plotted on the minimap, in court-space meters.</summary>
public sealed record CourtMarker(double X, double Y, string? Label = null, string? StyleKey = null);
