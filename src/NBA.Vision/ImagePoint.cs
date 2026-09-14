namespace NBA.Vision;

/// <summary>A point in a captured frame's pixel coordinate space.</summary>
public readonly record struct ImagePoint(double X, double Y);

/// <summary>A point in a sport's court-space coordinate system (meters from the geometry's origin).</summary>
public readonly record struct CourtPoint(double X, double Y);

/// <summary>One image-space point paired with the name of the court landmark it corresponds to.</summary>
public readonly record struct LandmarkCorrespondence(ImagePoint Image, string LandmarkName);
