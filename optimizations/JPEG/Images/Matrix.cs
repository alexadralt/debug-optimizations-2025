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
						pixel->value3 = *dataPtr++;
						pixel->value2 = *dataPtr++;
						pixel->value1 = *dataPtr++;
						pixel->format = PixelFormat.RGB;
					}
				}
			}
		}
		
		bmp.UnlockBits(data);

		return matrix;
	}

	public static explicit operator Bitmap(Matrix matrix)
	{
		var bmp = new Bitmap(matrix.Width, matrix.Height);

		for (var j = 0; j < bmp.Height; j++)
		{
			for (var i = 0; i < bmp.Width; i++)
			{
				var pixel = matrix.Pixels[j, i];
				bmp.SetPixel(i, j, Color.FromArgb(ToByte(pixel.R), ToByte(pixel.G), ToByte(pixel.B)));
			}
		}

		return bmp;
	}

	public static int ToByte(float d)
	{
		var val = (int)d;
		if (val > byte.MaxValue)
			return byte.MaxValue;
		if (val < byte.MinValue)
			return byte.MinValue;
		return val;
	}
}