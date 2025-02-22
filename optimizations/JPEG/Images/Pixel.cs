using System;
using System.Linq;

namespace JPEG.Images;

public struct Pixel
{
	public Pixel(double firstComponent, double secondComponent, double thirdComponent, PixelFormat pixelFormat)
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
	public double value1;
	public double value2;
	public double value3;

	public double R => format == PixelFormat.RGB ? value1 : (298.082 * value1 + 408.583 * Cr) / 256.0 - 222.921;

	public double G =>
		format == PixelFormat.RGB ? value2 : (298.082 * Y - 100.291 * Cb - 208.120 * Cr) / 256.0 + 135.576;

	public double B => format == PixelFormat.RGB ? value3 : (298.082 * Y + 516.412 * Cb) / 256.0 - 276.836;

	public double Y => format == PixelFormat.YCbCr ? value1 : 16.0 + (65.738 * R + 129.057 * G + 24.064 * B) / 256.0;
	public double Cb => format == PixelFormat.YCbCr ? value2 : 128.0 + (-37.945 * R - 74.494 * G + 112.439 * B) / 256.0;
	public double Cr => format == PixelFormat.YCbCr ? value3 : 128.0 + (112.439 * R - 94.154 * G - 18.285 * B) / 256.0;
}