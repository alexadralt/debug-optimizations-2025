using System.Drawing;
using System.Drawing.Imaging;

namespace JPEG.Images;

class Matrix
{
	public readonly Pixel[,] Pixels;
	public readonly int Height;
	public readonly int Width;

	public Matrix(int height, int width)
	{
		Height = height;
		Width = width;

		Pixels = new Pixel[height, width];
	}

	public static explicit operator Matrix(Bitmap bmp)
	{
		var bmpHeight = bmp.Height;
		var bmpWidth = bmp.Width;
		var height = bmpHeight - bmpHeight % 8;
		var width = bmpWidth - bmpWidth % 8;

		var data = bmp.LockBits(new Rectangle(0, 0, bmpWidth, bmpHeight), ImageLockMode.ReadOnly, bmp.PixelFormat);
		var stride = data.Stride;
		
		var matrix = new Matrix(height, width);

		unsafe
		{
			var dataPtr = (byte*)data.Scan0;
			fixed (Pixel* ptr = matrix.Pixels)
			{
				var pixel = ptr;
				for (var i = 0; i < height; i++, dataPtr += stride)
				{
					var rowPtr = dataPtr;
					for (var j = 0; j < width; j++, pixel++)
					{
						pixel->value3 = *rowPtr++; // blue
						pixel->value2 = *rowPtr++; // green
						pixel->value1 = *rowPtr++; // red
					}
				}
			}
		}
		
		bmp.UnlockBits(data);

		return matrix;
	}

	public static explicit operator Bitmap(Matrix matrix)
	{
		var (width, height) = (matrix.Width, matrix.Height);
		var pixelFormat = System.Drawing.Imaging.PixelFormat.Format24bppRgb;
		var bmp = new Bitmap(width, height, pixelFormat);
		var data = bmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, pixelFormat);
		var stride = data.Stride;

		unsafe
		{
			var dataPtr = (byte*)data.Scan0;
			fixed (Pixel* ptr = matrix.Pixels)
			{
				var pixel = ptr;
				for (var i = 0; i < height; i++, dataPtr += stride)
				{
					var rowPtr = dataPtr;
					for (var j = 0; j < width; j++, pixel++)
					{
						// blue channel
						*rowPtr = ToByte((298.082f * pixel->value1 + 516.412f * pixel->value2) / 256.0f - 276.836f);
						rowPtr++;
						
						// green channel
						*rowPtr = ToByte(
							(298.082f * pixel->value1 - 100.291f * pixel->value2 - 208.120f * pixel->value3) / 256.0f +
							135.576f);
						rowPtr++;
						
						// red channel
						*rowPtr = ToByte((298.082f * pixel->value1 + 408.583f * pixel->value3) / 256.0f - 222.921f);
						rowPtr++;
					}
				}
			}
		}
		
		bmp.UnlockBits(data);

		return bmp;
	}

	private static byte ToByte(float d)
	{
		var val = (int)d;
		if (val > byte.MaxValue)
			return byte.MaxValue;
		if (val < byte.MinValue)
			return byte.MinValue;
		return (byte)val;
	}
}