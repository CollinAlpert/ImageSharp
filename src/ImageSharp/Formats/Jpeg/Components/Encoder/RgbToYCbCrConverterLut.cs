// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Runtime.CompilerServices;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Formats.Jpeg.Components.Encoder
{
    /// <summary>
    /// Provides 8-bit lookup tables for converting from Rgb to YCbCr colorspace.
    /// Methods to build the tables are based on libjpeg implementation.
    /// </summary>
    internal unsafe struct RgbToYCbCrConverterLut
    {
        /// <summary>
        /// The red luminance table
        /// </summary>
        public fixed int YRTable[256];

        /// <summary>
        /// The green luminance table
        /// </summary>
        public fixed int YGTable[256];

        /// <summary>
        /// The blue luminance table
        /// </summary>
        public fixed int YBTable[256];

        /// <summary>
        /// The red blue-chrominance table
        /// </summary>
        public fixed int CbRTable[256];

        /// <summary>
        /// The green blue-chrominance table
        /// </summary>
        public fixed int CbGTable[256];

        /// <summary>
        /// The blue blue-chrominance table
        /// B=>Cb and R=>Cr are the same
        /// </summary>
        public fixed int CbBTable[256];

        /// <summary>
        /// The green red-chrominance table
        /// </summary>
        public fixed int CrGTable[256];

        /// <summary>
        /// The blue red-chrominance table
        /// </summary>
        public fixed int CrBTable[256];

        // Speediest right-shift on some machines and gives us enough accuracy at 4 decimal places.
        private const int ScaleBits = 16;

        private const int CBCrOffset = 128 << ScaleBits;

        private const int Half = 1 << (ScaleBits - 1);

        /// <summary>
        /// Initializes the YCbCr tables
        /// </summary>
        /// <returns>The initialized <see cref="RgbToYCbCrConverterLut"/></returns>
        public static RgbToYCbCrConverterLut Create()
        {
            RgbToYCbCrConverterLut tables = default;

            for (int i = 0; i <= 255; i++)
            {
                // The values for the calculations are left scaled up since we must add them together before rounding.
                tables.YRTable[i] = Fix(0.299F) * i;
                tables.YGTable[i] = Fix(0.587F) * i;
                tables.YBTable[i] = (Fix(0.114F) * i) + Half;
                tables.CbRTable[i] = (-Fix(0.168735892F)) * i;
                tables.CbGTable[i] = (-Fix(0.331264108F)) * i;

                // We use a rounding fudge - factor of 0.5 - epsilon for Cb and Cr.
                // This ensures that the maximum output will round to 255
                // not 256, and thus that we don't have to range-limit.
                //
                // B=>Cb and R=>Cr tables are the same
                tables.CbBTable[i] = (Fix(0.5F) * i) + CBCrOffset + Half - 1;

                tables.CrGTable[i] = (-Fix(0.418687589F)) * i;
                tables.CrBTable[i] = (-Fix(0.081312411F)) * i;
            }

            return tables;
        }

        /// <summary>
        /// Optimized method to allocates the correct y, cb, and cr values to the DCT blocks from the given r, g, b values.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ConvertPixelInto(
            int r,
            int g,
            int b,
            ref Block8x8F yResult,
            ref Block8x8F cbResult,
            ref Block8x8F crResult,
            int i)
        {
            // float y = (0.299F * r) + (0.587F * g) + (0.114F * b);
            yResult[i] = (this.YRTable[r] + this.YGTable[g] + this.YBTable[b]) >> ScaleBits;

            // float cb = 128F + ((-0.168736F * r) - (0.331264F * g) + (0.5F * b));
            cbResult[i] = (this.CbRTable[r] + this.CbGTable[g] + this.CbBTable[b]) >> ScaleBits;

            // float cr = 128F + ((0.5F * r) - (0.418688F * g) - (0.081312F * b));
            crResult[i] = (this.CbBTable[r] + this.CrGTable[g] + this.CrBTable[b]) >> ScaleBits;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ConvertPixelInto(
            int r,
            int g,
            int b,
            ref Block8x8F yResult,
            int i)
        {
            // float y = (0.299F * r) + (0.587F * g) + (0.114F * b);
            yResult[i] = (this.YRTable[r] + this.YGTable[g] + this.YBTable[b]) >> ScaleBits;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ConvertPixelInto(
            int r,
            int g,
            int b,
            ref Block8x8F cbResult,
            ref Block8x8F crResult,
            int i)
        {
            // float cb = 128F + ((-0.168736F * r) - (0.331264F * g) + (0.5F * b));
            cbResult[i] = (this.CbRTable[r] + this.CbGTable[g] + this.CbBTable[b]) >> ScaleBits;

            // float cr = 128F + ((0.5F * r) - (0.418688F * g) - (0.081312F * b));
            crResult[i] = (this.CbBTable[r] + this.CrGTable[g] + this.CrBTable[b]) >> ScaleBits;
        }

        public void Convert(Span<Rgb24> rgbSpan, ref Block8x8F yBlock, ref Block8x8F cbBlock, ref Block8x8F crBlock)
        {
            ref Rgb24 rgbStart = ref rgbSpan[0];

            for (int i = 0; i < Block8x8F.Size; i++)
            {
                ref Rgb24 c = ref Unsafe.Add(ref rgbStart, i);

                this.ConvertPixelInto(
                    c.R,
                    c.G,
                    c.B,
                    ref yBlock,
                    ref cbBlock,
                    ref crBlock,
                    i);
            }
        }

        public void Convert(Span<Rgb24> rgbSpan, ref Block8x8F yBlockLeft, ref Block8x8F yBlockRight, ref Block8x8F cbBlock, ref Block8x8F crBlock, int row)
        {
            ref Rgb24 rgbStart = ref rgbSpan[0];
            for (int i = 0; i < 8; i += 2)
            {
                Span<int> rgbTriplets = stackalloc int[24]; // 8 pixels by 3 integers

                for (int j = 0; j < 2; j++)
                {
                    // left
                    ref Rgb24 stride = ref Unsafe.Add(ref rgbStart, (i + j) * 16);
                    for (int k = 0; k < 8; k += 2)
                    {
                        Rgb24 px0 = Unsafe.Add(ref stride, k);
                        this.ConvertPixelInto(px0.R, px0.G, px0.B, ref yBlockLeft, (i + j) * 8 + k);

                        Rgb24 px1 = Unsafe.Add(ref stride, k + 1);
                        this.ConvertPixelInto(px1.R, px1.G, px1.B, ref yBlockLeft, (i + j) * 8 + k + 1);

                        int idx = 3 * (k / 2);
                        rgbTriplets[idx] += px0.R + px1.R;
                        rgbTriplets[idx + 1] += px0.G + px1.G;
                        rgbTriplets[idx + 2] += px0.B + px1.B;
                    }

                    // right
                    stride = ref Unsafe.Add(ref stride, 8);
                    for (int k = 0; k < 8; k += 2)
                    {
                        Rgb24 px0 = Unsafe.Add(ref stride, k);
                        this.ConvertPixelInto(px0.R, px0.G, px0.B, ref yBlockRight, (i + j) * 8 + k);

                        Rgb24 px1 = Unsafe.Add(ref stride, k + 1);
                        this.ConvertPixelInto(px1.R, px1.G, px1.B, ref yBlockRight, (i + j) * 8 + k + 1);

                        int idx = 3 * (4 + (k / 2));
                        rgbTriplets[idx] += px0.R + px1.R;
                        rgbTriplets[idx + 1] += px0.G + px1.G;
                        rgbTriplets[idx + 2] += px0.B + px1.B;

                    }
                }

                int writeIdx =
                    row * Block8x8F.Size / 2 // upper or lower part
                    + (i / 2) * 8;           // which row
                for (int j = 0; j < 8; j++)
                {
                    int idx = j * 3;
                    this.ConvertPixelInto(rgbTriplets[idx] / 4, rgbTriplets[idx + 1] / 4, rgbTriplets[idx + 2] / 4, ref cbBlock, ref crBlock, writeIdx + j);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Fix(float x)
            => (int)((x * (1L << ScaleBits)) + 0.5F);
    }
}
