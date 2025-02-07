// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Processing.Processors.Quantization;

/// <summary>
/// Allows the quantization of images pixels using color palettes.
/// </summary>
public class PaletteQuantizer : IQuantizer
{
    private readonly ReadOnlyMemory<Color> colorPalette;

    /// <summary>
    /// Initializes a new instance of the <see cref="PaletteQuantizer"/> class.
    /// </summary>
    /// <param name="palette">The color palette.</param>
    public PaletteQuantizer(ReadOnlyMemory<Color> palette)
        : this(palette, new QuantizerOptions())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PaletteQuantizer"/> class.
    /// </summary>
    /// <param name="palette">The color palette.</param>
    /// <param name="options">The quantizer options defining quantization rules.</param>
    public PaletteQuantizer(ReadOnlyMemory<Color> palette, QuantizerOptions options)
    {
        Guard.MustBeGreaterThan(palette.Length, 0, nameof(palette));
        Guard.NotNull(options, nameof(options));

        this.colorPalette = palette;
        this.Options = options;
    }

    /// <inheritdoc />
    public QuantizerOptions Options { get; }

    /// <inheritdoc />
    public IQuantizer<TPixel> CreatePixelSpecificQuantizer<TPixel>(Configuration configuration)
        where TPixel : unmanaged, IPixel<TPixel>
        => this.CreatePixelSpecificQuantizer<TPixel>(configuration, this.Options);

    /// <inheritdoc />
    public IQuantizer<TPixel> CreatePixelSpecificQuantizer<TPixel>(Configuration configuration, QuantizerOptions options)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Guard.NotNull(options, nameof(options));

        // The palette quantizer can reuse the same pixel map across multiple frames
        // since the palette is unchanging. This allows a reduction of memory usage across
        // multi frame gifs using a global palette.
        int length = Math.Min(this.colorPalette.Length, options.MaxColors);
        TPixel[] palette = new TPixel[length];

        Color.ToPixel(configuration, this.colorPalette.Span, palette.AsSpan());
        return new PaletteQuantizer<TPixel>(configuration, options, palette);
    }
}
