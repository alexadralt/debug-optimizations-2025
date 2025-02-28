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
		// Pixels = GC.AllocateUninitializedArray<Pixel>(height * width);
	}

	public static explicit operator Matrix(Bitmap bmp)
	{
		var bmpHeight = bmp.Height;
		var bmpWidth = bmp.Width;
		var height = bmpHeight - bmpHeight % 8;
		var width = bmpWidth - bmpWidth % 8;

		var data = bmp.LockBits(new Rectangle(0, 0, bmpWidth, bmpHeight), ImageLockMode.ReadOnly, bmp.PixelFormat);
		
		var matrix = new Matrix(height, width);

		unsafe
		{
			var dataPtr = (byte*)data.Scan0;
			fixed (Pixel* ptr = matrix.Pixels)
			{
				var pixel = ptr;
				for (var i = 0; i < height; i++)
				{
					for (var j = 0; j < width; j++, pixel++)
					{
						pixel->value3 = *dataPtr++; // blue
						pixel->value2 = *dataPtr++; // green
						pixel->value1 = *dataPtr++; // red
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

		unsafe
		{
			var dataPtr = (byte*)data.Scan0;
			fixed (Pixel* ptr = matrix.Pixels)
			{
				var pixel = ptr;
				for (var i = 0; i < height; i++)
				{
					for (var j = 0; j < width; j++, pixel++)
					{
						// blue channel
						*dataPtr = ToByte((298.082f * pixel->value1 + 516.412f * pixel->value2) / 256.0f - 276.836f);
						dataPtr++;
						
						// green channel
						*dataPtr = ToByte(
							(298.082f * pixel->value1 - 100.291f * pixel->value2 - 208.120f * pixel->value3) / 256.0f +
							135.576f);
						dataPtr++;
						
						// red channel
						*dataPtr = ToByte((298.082f * pixel->value1 + 408.583f * pixel->value3) / 256.0f - 222.921f);
						dataPtr++;
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