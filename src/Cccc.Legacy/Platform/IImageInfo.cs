namespace CCCCScheduler.Platform;

/// <summary>Reads an image's dimensions without decoding the whole thing.</summary>
public interface IImageInfo
{
    /// <summary>
    /// The image's native pixel size. Throws when the file is not an image this platform can read —
    /// callers rely on that to refuse a bad floor plan before copying it into a project.
    /// </summary>
    (int Width, int Height) PixelSize(string imagePath);

    /// <summary>
    /// The same answer for an image that is not a file: a floor plan read straight out of a
    /// <c>.cccc</c> in memory, or an upload that has never touched a disk. Throws for the same reason
    /// and on the same terms as <see cref="PixelSize(string)"/> — it is the same decoder, given the
    /// stream directly instead of one opened from a path.
    ///
    /// <para>The stream is read from its current position and is <b>not</b> disposed; the caller owns
    /// it. Whether the position is left at the end is up to the decoder and not something to rely
    /// on — seek before reusing it.</para>
    ///
    /// <para>Required rather than defaulted, deliberately: a default implementation that threw would
    /// let a platform quietly not have this, and the host that needs it is the one with no file paths
    /// to fall back to.</para>
    /// </summary>
    (int Width, int Height) PixelSize(Stream image);
}
