namespace NBA.JerseyOcr;

/// <summary>A crop rectangle after <see cref="CropClamping.Clamp"/>, in source-space pixel coordinates.</summary>
public readonly record struct ClampedCrop(int Left, int Top, int Right, int Bottom, bool IsDegenerate);
