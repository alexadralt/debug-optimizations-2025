using System;
using System.Linq;

namespace JPEG.Images;

public struct Pixel
{
	public Pixel(float firstComponent, float secondComponent, float thirdComponent, PixelFormat pixelFormat)
	{
		if (!new[] { PixelFormat.RGB, PixelFormat.YCbCr }.Contains(pixelFormat))
			throw new FormatException("Unknown pixel format: " + pixelFormat);
		format = pixelFormat;
		if (pixelFormat == PixelFormat.RGB)
		{
			value1 = firstComponent;
			value2 = secondComponent;
			value3 = thirdComponent;
		}

		if (pixelFormat == PixelFormat.YCbCr)
		{
			value1 = firstComponent;
			value2 = secondComponent;
			value3 = thirdComponent;
		}
	}

	public PixelFormat format;
	public float value1;
	public float value2;
	public float value3;

	public float R => format == PixelFormat.RGB ? value1 : (298.082f * value1 + 408.583f * Cr) / 256.0f - 222.921f;

	public float G =>
		format == PixelFormat.RGB ? value2 : (298.082f * Y - 100.291f * Cb - 208.120f * Cr) / 256.0f + 135.576f;

	public float B => format == PixelFormat.RGB ? value3 : (298.082f * Y + 516.412f * Cb) / 256.0f - 276.836f;

	public float Y => format == PixelFormat.YCbCr ? value1 : 16.0f + (65.738f * R + 129.057f * G + 24.064f * B) / 256.0f;
	public float Cb => format == PixelFormat.YCbCr ? value2 : 128.0f + (-37.945f * R - 74.494f * G + 112.439f * B) / 256.0f;
	public float Cr => format == PixelFormat.YCbCr ? value3 : 128.0f + (112.439f * R - 94.154f * G - 18.285f * B) / 256.0f;
}