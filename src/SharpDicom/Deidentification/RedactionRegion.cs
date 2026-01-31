using System;

namespace SharpDicom.Deidentification;

/// <summary>
/// Defines a rectangular region for pixel data redaction.
/// </summary>
/// <remarks>
/// Used to specify areas of an image that should be filled with a solid color
/// to remove burned-in annotations or other identifying information.
/// </remarks>
public readonly struct RedactionRegion : IEquatable<RedactionRegion>
{
    /// <summary>
    /// Gets the X coordinate of the top-left corner (0-based).
    /// </summary>
    public int X { get; init; }

    /// <summary>
    /// Gets the Y coordinate of the top-left corner (0-based).
    /// </summary>
    public int Y { get; init; }

    /// <summary>
    /// Gets the width of the region in pixels.
    /// </summary>
    public int Width { get; init; }

    /// <summary>
    /// Gets the height of the region in pixels.
    /// </summary>
    public int Height { get; init; }

    /// <summary>
    /// Gets the frame number to apply this region to (0-based).
    /// </summary>
    /// <remarks>
    /// If null, the region is applied to all frames in the image.
    /// If specified, the region is only applied to the specified frame.
    /// </remarks>
    public int? Frame { get; init; }

    /// <summary>
    /// Creates a new <see cref="RedactionRegion"/> with the specified bounds.
    /// </summary>
    /// <param name="x">X coordinate of the top-left corner.</param>
    /// <param name="y">Y coordinate of the top-left corner.</param>
    /// <param name="width">Width of the region in pixels.</param>
    /// <param name="height">Height of the region in pixels.</param>
    /// <param name="frame">Optional frame number (0-based), or null for all frames.</param>
    public RedactionRegion(int x, int y, int width, int height, int? frame = null)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
        Frame = frame;
    }

    /// <summary>
    /// Creates a region covering the top portion of the image.
    /// </summary>
    /// <param name="height">Height of the bar in pixels.</param>
    /// <param name="imageWidth">Width of the image.</param>
    /// <returns>A region spanning the full width at the top of the image.</returns>
    public static RedactionRegion TopBar(int height, int imageWidth)
        => new(0, 0, imageWidth, height);

    /// <summary>
    /// Creates a region covering the bottom portion of the image.
    /// </summary>
    /// <param name="height">Height of the bar in pixels.</param>
    /// <param name="imageWidth">Width of the image.</param>
    /// <param name="imageHeight">Height of the image.</param>
    /// <returns>A region spanning the full width at the bottom of the image.</returns>
    public static RedactionRegion BottomBar(int height, int imageWidth, int imageHeight)
        => new(0, imageHeight - height, imageWidth, height);

    /// <summary>
    /// Creates a region covering the left side of the image.
    /// </summary>
    /// <param name="width">Width of the bar in pixels.</param>
    /// <param name="imageHeight">Height of the image.</param>
    /// <returns>A region spanning the full height at the left of the image.</returns>
    public static RedactionRegion LeftBar(int width, int imageHeight)
        => new(0, 0, width, imageHeight);

    /// <summary>
    /// Creates a region covering the right side of the image.
    /// </summary>
    /// <param name="width">Width of the bar in pixels.</param>
    /// <param name="imageWidth">Width of the image.</param>
    /// <param name="imageHeight">Height of the image.</param>
    /// <returns>A region spanning the full height at the right of the image.</returns>
    public static RedactionRegion RightBar(int width, int imageWidth, int imageHeight)
        => new(imageWidth - width, 0, width, imageHeight);

    /// <summary>
    /// Creates a region from two corner coordinates.
    /// </summary>
    /// <param name="x1">X coordinate of first corner.</param>
    /// <param name="y1">Y coordinate of first corner.</param>
    /// <param name="x2">X coordinate of opposite corner.</param>
    /// <param name="y2">Y coordinate of opposite corner.</param>
    /// <param name="frame">Optional frame number (0-based), or null for all frames.</param>
    /// <returns>A region covering the rectangle between the two corners.</returns>
    /// <remarks>
    /// The corners can be specified in any order; the region will be
    /// normalized to have positive width and height.
    /// </remarks>
    public static RedactionRegion FromCorners(int x1, int y1, int x2, int y2, int? frame = null)
        => new(
            Math.Min(x1, x2),
            Math.Min(y1, y2),
            Math.Abs(x2 - x1),
            Math.Abs(y2 - y1),
            frame);

    /// <summary>
    /// Determines whether this region intersects with the image bounds.
    /// </summary>
    /// <param name="imageWidth">Width of the image.</param>
    /// <param name="imageHeight">Height of the image.</param>
    /// <returns>True if the region overlaps with the image area.</returns>
    public bool IntersectsImage(int imageWidth, int imageHeight)
        => X < imageWidth && Y < imageHeight && X + Width > 0 && Y + Height > 0;

    /// <summary>
    /// Gets the area of this region in pixels.
    /// </summary>
    public long Area => (long)Width * Height;

    /// <inheritdoc/>
    public bool Equals(RedactionRegion other)
        => X == other.X && Y == other.Y && Width == other.Width &&
           Height == other.Height && Frame == other.Frame;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is RedactionRegion r && Equals(r);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
#if NETSTANDARD2_0
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + X.GetHashCode();
            hash = hash * 31 + Y.GetHashCode();
            hash = hash * 31 + Width.GetHashCode();
            hash = hash * 31 + Height.GetHashCode();
            hash = hash * 31 + Frame.GetHashCode();
            return hash;
        }
#else
        return HashCode.Combine(X, Y, Width, Height, Frame);
#endif
    }

    /// <summary>
    /// Equality operator.
    /// </summary>
    public static bool operator ==(RedactionRegion left, RedactionRegion right) => left.Equals(right);

    /// <summary>
    /// Inequality operator.
    /// </summary>
    public static bool operator !=(RedactionRegion left, RedactionRegion right) => !left.Equals(right);

    /// <inheritdoc/>
    public override string ToString()
        => Frame.HasValue
            ? $"Region({X}, {Y}, {Width}x{Height}, Frame={Frame.Value})"
            : $"Region({X}, {Y}, {Width}x{Height})";
}
