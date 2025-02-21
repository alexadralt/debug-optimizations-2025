using System;
using JPEG.Utilities;

namespace JPEG;

public class DCT
{
	public static double[,] DCT2D(double[,] input)
	{
		var height = input.GetLength(0);
		var width = input.GetLength(1);
		var coeffs = new double[width, height];

		for (var u = 0; u < width; u++)
		{
			for (var v = 0; v < height; v++)
			{
				var sum = 0d;
				for (var x = 0; x < width; x++)
				{
					var xSum = 0d;
					for (var y = 0; y < height; y++)
					{
						xSum += BasisFunction(input[x, y], u, v, x, y, height, width);
					}
					
					sum += xSum;
				}
				
				coeffs[u, v] = sum * Beta(height, width) * Alpha(u) * Alpha(v);
			}
		}

		return coeffs;
	}

	public static void IDCT2D(double[,] coeffs, double[,] output)
	{
		var width = coeffs.GetLength(1);
		var height = coeffs.GetLength(0);
		
		for (var x = 0; x < width; x++)
		{
			for (var y = 0; y < height; y++)
			{
				var sum = 0d;
				for (var u = 0; u < width; u++)
				{
					var uSum = 0d;
					for (var v = 0; v < height; v++)
					{
						uSum += BasisFunction(coeffs[u, v], u, v, x, y, coeffs.GetLength(0), coeffs.GetLength(1))
						        * Alpha(u) * Alpha(v);
					}

					sum += uSum;
				}

				output[x, y] = sum * Beta(coeffs.GetLength(0), coeffs.GetLength(1));
			}
		}
	}

	public static double BasisFunction(double a, double u, double v, double x, double y, int height, int width)
	{
		var b = Math.Cos(((2d * x + 1d) * u * Math.PI) / (2 * width));
		var c = Math.Cos(((2d * y + 1d) * v * Math.PI) / (2 * height));

		return a * b * c;
	}

	private static double Alpha(int u)
	{
		if (u == 0)
			return 1 / Math.Sqrt(2);
		return 1;
	}

	private static double Beta(int height, int width)
	{
		return 1d / width + 1d / height;
	}
}