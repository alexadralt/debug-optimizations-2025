using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using JPEG.Processor;

namespace JPEG;

public class DCT
{
	private static float[] _cosines;

	static DCT()
	{
		var squaredSize = JpegProcessor.DCTSize * JpegProcessor.DCTSize;
		_cosines = GC.AllocateUninitializedArray<float>(squaredSize * 2);

		var doubleSize = JpegProcessor.DCTSize * 2;
		for (var u = 0; u < JpegProcessor.DCTSize; u++)
		{
			for (var x = 0; x < JpegProcessor.DCTSize; x++)
			{
				_cosines[u * JpegProcessor.DCTSize + x] = (float)Math.Cos(((2d * x + 1d) * u * Math.PI) / doubleSize);
			}
		}

		for (var v = 0; v < JpegProcessor.DCTSize; v++)
		{
			for (var y = 0; y < JpegProcessor.DCTSize; y++)
			{
				_cosines[squaredSize + v * JpegProcessor.DCTSize + y] = (float)Math.Cos(((2d * y + 1d) * v * Math.PI) / doubleSize);
			}
		}
	}
	
	public static float[] DCT2D(float[] input)
	{
		var beta = Beta(JpegProcessor.DCTSize, JpegProcessor.DCTSize);
		var squaredSize = JpegProcessor.DCTSize * JpegProcessor.DCTSize;
		var coeffs = GC.AllocateUninitializedArray<float>(squaredSize);

		for (var u = 0; u < JpegProcessor.DCTSize; u++)
		{
			for (var v = 0; v < JpegProcessor.DCTSize; v++)
			{
				var sum = 0f;
				var count = Vector<float>.Count;
				var len = JpegProcessor.DCTSize / count;
				var inputOffset = 0;
				
				for (var x = 0; x < JpegProcessor.DCTSize; x++)
				{
					var b = _cosines[u * JpegProcessor.DCTSize + x];
					var xSum = Vector<float>.Zero;
					
					var cCosineOffset = squaredSize + v * JpegProcessor.DCTSize;
					
					for (var y = 0;
					     y < len;
					     y++, inputOffset += count, cCosineOffset += count)
					{
						var c = new Vector<float>(_cosines, cCosineOffset);
						var inputElem = new Vector<float>(input, inputOffset);
						xSum += inputElem * c;
					}
				
					sum += Vector.Sum(xSum) * b;
				}
			
				coeffs[u * JpegProcessor.DCTSize + v] = sum * beta * Alpha(u) * Alpha(v);
			}
		}

		return coeffs;
	}

	public static void IDCT2D(float[,] coeffs, float[] output)
	{
		var width = coeffs.GetLength(1);
		var height = coeffs.GetLength(0);
		var beta = Beta(height, width);
		
		for (var x = 0; x < width; x++)
		{
			for (var y = 0; y < height; y++)
			{
				var sum = 0f;
				for (var u = 0; u < width; u++)
				{
					var uSum = 0f;
					for (var v = 0; v < height; v++)
					{
						uSum += BasisFunction(coeffs[u, v], u, v, x, y, height, width)
						        * Alpha(u) * Alpha(v);
					}

					sum += uSum;
				}

				output[x * height + y] = sum * beta;
			}
		}
	}

	public static float BasisFunction(float a, float u, float v, float x, float y, int height, int width)
	{
		var b = (float)Math.Cos(((2d * x + 1d) * u * Math.PI) / (2 * width));
		var c = (float)Math.Cos(((2d * y + 1d) * v * Math.PI) / (2 * height));

		return a * b * c;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static float Alpha(int u)
	{
		if (u == 0)
			return 0.70710678118654752440084436210485f; // 1 / sqrt(2)
		return 1;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static float Beta(int height, int width)
	{
		return 1f / width + 1f / height;
	}
}