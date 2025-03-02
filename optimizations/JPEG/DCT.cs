using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using JPEG.Processor;

namespace JPEG;

public class DCT
{
	private static readonly float[] _cosines;
	private static readonly float[] _cosindesSecondHalfTransposed;
	private static readonly Vector<float> _alphas0;

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

		_cosindesSecondHalfTransposed = GC.AllocateUninitializedArray<float>(squaredSize);
		for (var v = 0; v < JpegProcessor.DCTSize; v++)
		{
			for (var y = 0; y < JpegProcessor.DCTSize; y++)
			{
				var value = (float)Math.Cos(((2d * y + 1d) * v * Math.PI) / doubleSize);
				_cosines[squaredSize + v * JpegProcessor.DCTSize + y] = value;
				_cosindesSecondHalfTransposed[y * JpegProcessor.DCTSize + v] = value;
			}
		}

		var alphas = GC.AllocateUninitializedArray<float>(Vector<float>.Count);
		alphas[0] = 0.70710678118654752440084436210485f;
		for (var i = 1; i < alphas.Length; i++)
		{
			alphas[i] = 1;
		}

		_alphas0 = new Vector<float>(alphas);
	}
	
	public static float[] DCT2D(float[] input, float[] coeffs)
	{
		var beta = 2f * (1f / JpegProcessor.DCTSize);
		var squaredSize = JpegProcessor.DCTSize * JpegProcessor.DCTSize;

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

	public static void IDCT2D(float[] coeffs, float[] output)
	{
		var beta = 2f * (1f / JpegProcessor.DCTSize);
		var squaredSize = JpegProcessor.DCTSize * JpegProcessor.DCTSize;
		
		for (var x = 0; x < JpegProcessor.DCTSize; x++)
		{
			for (var y = 0; y < JpegProcessor.DCTSize; y++)
			{
				var sum = 0f;
				var count = Vector<float>.Count;
				var len = JpegProcessor.DCTSize / count;
				var coeffsOffset = 0;
				
				for (var u = 0; u < JpegProcessor.DCTSize; u++)
				{
					var b = _cosines[u * JpegProcessor.DCTSize + x];
					var uSum = Vector<float>.Zero;
					
					var cCosineOffset = y * JpegProcessor.DCTSize;

					{
						var c = new Vector<float>(_cosindesSecondHalfTransposed, cCosineOffset);
						var coeffsElem = new Vector<float>(coeffs, coeffsOffset);
						uSum += coeffsElem * c * _alphas0 * Alpha(u);
						
						cCosineOffset += count;
						coeffsOffset += count;
					}
					
					for (var v = 1; v < len; v++, cCosineOffset += count, coeffsOffset += count)
					{
						var c = new Vector<float>(_cosindesSecondHalfTransposed, cCosineOffset);
						var coeffsElem = new Vector<float>(coeffs, coeffsOffset);
						uSum += coeffsElem * c * Alpha(u);
					}

					sum += Vector.Sum(uSum) * b;
				}

				output[x * JpegProcessor.DCTSize + y] = sum * beta;
			}
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static float Alpha(int u)
	{
		if (u == 0)
			return 0.70710678118654752440084436210485f; // 1 / sqrt(2)
		return 1;
	}
}