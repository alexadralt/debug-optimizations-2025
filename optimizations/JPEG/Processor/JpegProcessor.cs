using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using JPEG.Images;

namespace JPEG.Processor;

public class JpegProcessor : IJpegProcessor
{
	public static readonly JpegProcessor Init = new();
	public const int CompressionQuality = 70;
	public const int DCTSize = 8;
	
	private static readonly int[,] QuantizationMatrix = new[,]
	{
		{ 16, 11, 10, 16, 24, 40, 51, 61 },
		{ 12, 12, 14, 19, 26, 58, 60, 55 },
		{ 14, 13, 16, 24, 40, 57, 69, 56 },
		{ 14, 17, 22, 29, 51, 87, 80, 62 },
		{ 18, 22, 37, 56, 68, 109, 103, 77 },
		{ 24, 35, 55, 64, 81, 104, 113, 92 },
		{ 49, 64, 78, 87, 103, 121, 120, 101 },
		{ 72, 92, 95, 98, 112, 100, 103, 99 }
	};

	static JpegProcessor()
	{
		var multiplier = CompressionQuality < 50 ? 5000 / CompressionQuality : 200 - 2 * CompressionQuality;

		for (int y = 0; y < DCTSize; y++)
		{
			for (int x = 0; x < DCTSize; x++)
			{
				QuantizationMatrix[y, x] = (multiplier * QuantizationMatrix[y, x] + 50) / 100;
			}
		}
	}

	public void Compress(string imagePath, string compressedImagePath)
	{
		using var fileStream = File.OpenRead(imagePath);
		using var bmp = (Bitmap)Image.FromStream(fileStream, false, false);
		var imageMatrix = (Matrix)bmp;
		//Console.WriteLine($"{bmp.Width}x{bmp.Height} - {fileStream.Length / (1024.0 * 1024):F2} MB");
		var compressionResult = Compress(imageMatrix, CompressionQuality);
		compressionResult.Save(compressedImagePath);
	}

	public void Uncompress(string compressedImagePath, string uncompressedImagePath)
	{
		var compressedImage = CompressedImage.Load(compressedImagePath);
		var uncompressedImage = Uncompress(compressedImage);
		var resultBmp = (Bitmap)uncompressedImage;
		resultBmp.Save(uncompressedImagePath, ImageFormat.Bmp);
	}

	private static CompressedImage Compress(Matrix matrix, int quality = 50)
	{
		var width = matrix.Width / DCTSize;
		var height = matrix.Height / DCTSize;
		
		var chunksCount = width * height;
		var bytesCollection = new List<((int, int), byte[])>(chunksCount);
		
		var partitioner = Partitioner.Create(0, chunksCount, chunksCount / Environment.ProcessorCount);
		Parallel.ForEach(partitioner, range =>
		{
			var subMatrix = GC.AllocateUninitializedArray<float>(DCTSize * DCTSize);
			var coeffs = GC.AllocateUninitializedArray<float>(DCTSize * DCTSize);
			var quantizedFreqsArr = GC.AllocateUninitializedArray<byte>(DCTSize * DCTSize);
			var quantizedBytesArr = GC.AllocateUninitializedArray<byte>(DCTSize * DCTSize);
			var bytes = new List<byte>(DCTSize * DCTSize);
			
			for (var k = range.Item1; k < range.Item2; k++)
			{
				var x = (k % width) * DCTSize;
				var y = (k / width) * DCTSize;

				// Y
				{
					for (var j = 0; j < DCTSize; j++)
					{
						for (var i = 0; i < DCTSize; i++)
						{
							var pixel = matrix.Pixels[y + j, x + i];
							subMatrix[j * DCTSize + i] = 16.0f
							                             + (65.738f * pixel.value1
							                                + 129.057f * pixel.value2
							                                + 24.064f * pixel.value3) / 256.0f;
						}
					}

					ShiftMatrixValues(subMatrix, -128);
					var channelFreqs = DCT.DCT2D(subMatrix, coeffs);
					var quantizedFreqs = Quantize(channelFreqs, quantizedFreqsArr);
					var quantizedBytes = ZigZagScan(quantizedFreqs, quantizedBytesArr);
					bytes.AddRange(quantizedBytes);
				}

				// Cb
				{
					for (var j = 0; j < DCTSize; j++)
					{
						for (var i = 0; i < DCTSize; i++)
						{
							var pixel = matrix.Pixels[y + j, x + i];
							subMatrix[j * DCTSize + i] = 128.0f
							                             + (-37.945f * pixel.value1
							                                - 74.494f * pixel.value2
							                                + 112.439f * pixel.value3) / 256.0f;
						}
					}

					ShiftMatrixValues(subMatrix, -128);
					var channelFreqs = DCT.DCT2D(subMatrix, coeffs);
					var quantizedFreqs = Quantize(channelFreqs, quantizedFreqsArr);
					var quantizedBytes = ZigZagScan(quantizedFreqs, quantizedBytesArr);
					bytes.AddRange(quantizedBytes);
				}

				// Cr
				{
					for (var j = 0; j < DCTSize; j++)
					{
						for (var i = 0; i < DCTSize; i++)
						{
							var pixel = matrix.Pixels[y + j, x + i];
							subMatrix[j * DCTSize + i] = 128.0f
							                             + (112.439f * pixel.value1
							                                - 94.154f * pixel.value2
							                                - 18.285f * pixel.value3) / 256.0f;
						}
					}

					ShiftMatrixValues(subMatrix, -128);
					var channelFreqs = DCT.DCT2D(subMatrix, coeffs);
					var quantizedFreqs = Quantize(channelFreqs, quantizedFreqsArr);
					var quantizedBytes = ZigZagScan(quantizedFreqs, quantizedBytesArr);
					bytes.AddRange(quantizedBytes);
				}

				lock (bytesCollection)
				{
					bytesCollection.Add(((x, y), bytes.ToArray()));
				}
				
				bytes.Clear();
			}
		});
		
		var allQuantizedBytes = new List<byte>(chunksCount * 3 * DCTSize * DCTSize);
		bytesCollection.Sort((x, y) =>
		{
			var tupleX = x.Item1;
			var tupleY = y.Item1;
			if (tupleX.Item2 < tupleY.Item2)
			{
				return -1;
			}
			if (tupleX.Item2 > tupleY.Item2)
			{
				return 1;
			}

			if (tupleX.Item1 < tupleY.Item1)
			{
				return -1;
			}
			if (tupleX.Item1 > tupleY.Item1)
			{
				return 1;
			}

			return 0;
		});

		for (var i = 0; i < bytesCollection.Count; i++)
		{
			allQuantizedBytes.AddRange(bytesCollection[i].Item2);
		}

		var compressedBytes = HuffmanCodec.Encode(allQuantizedBytes, out var decodeTable, out var bitsCount);

		return new CompressedImage
		{
			Quality = quality, CompressedBytes = compressedBytes, BitsCount = bitsCount, DecodeTable = decodeTable,
			Height = matrix.Height, Width = matrix.Width
		};
	}

	private static Matrix Uncompress(CompressedImage image)
	{
		var sizeSquared = DCTSize * DCTSize;
		var result = new Matrix(image.Height, image.Width);
		var allQuantizedBytes = HuffmanCodec.Decode(image.CompressedBytes, image.DecodeTable, image.BitsCount);
		
		var height = image.Height / DCTSize;
		var width = image.Width / DCTSize;

		var chunksCount = width * height;
		var partitioner = Partitioner.Create(0, chunksCount, chunksCount / Environment.ProcessorCount);
		Parallel.ForEach(partitioner, range =>
		{
			var outputY = GC.AllocateUninitializedArray<float>(DCTSize * DCTSize);
			var outputCb = GC.AllocateUninitializedArray<float>(DCTSize * DCTSize);
			var outputCr = GC.AllocateUninitializedArray<float>(DCTSize * DCTSize);
			var channelFrequenciesArray = GC.AllocateUninitializedArray<float>(DCTSize * DCTSize);
			var quantizedFrequenciesArray = GC.AllocateUninitializedArray<byte>(DCTSize * DCTSize);
			
			for (int i = range.Item1; i < range.Item2; i++)
			{
				var x = (i % width) * DCTSize;
				var y = (i / width) * DCTSize;
				var byteIndex = i * 3 * sizeSquared;
				
				// Y
				{
					var byteSpan = new Span<byte>(allQuantizedBytes, byteIndex, sizeSquared);
					var quantizedFreqs = ZigZagUnScan(byteSpan, quantizedFrequenciesArray);
					var channelFreqs = DeQuantize(quantizedFreqs, channelFrequenciesArray);
					DCT.IDCT2D(channelFreqs, outputY);
					ShiftMatrixValues(outputY, 128);
				}
					
				// Cb
				{
					var byteSpan = new Span<byte>(allQuantizedBytes, byteIndex + sizeSquared, sizeSquared);
					var quantizedFreqs = ZigZagUnScan(byteSpan, quantizedFrequenciesArray);
					var channelFreqs = DeQuantize(quantizedFreqs, channelFrequenciesArray);
					DCT.IDCT2D(channelFreqs, outputCb);
					ShiftMatrixValues(outputCb, 128);
				}
					
				// Cr
				{
					var byteSpan = new Span<byte>(allQuantizedBytes, byteIndex + sizeSquared * 2, sizeSquared);
					var quantizedFreqs = ZigZagUnScan(byteSpan, quantizedFrequenciesArray);
					var channelFreqs = DeQuantize(quantizedFreqs, channelFrequenciesArray);
					DCT.IDCT2D(channelFreqs, outputCr);
					ShiftMatrixValues(outputCr, 128);
				}
		
				SetPixels(result, outputY, outputCb, outputCr, y, x);
			}
		});
		
		return result;
	}

	private static void ShiftMatrixValues(float[] subMatrix, int shiftValue)
	{
		for (var y = 0; y < DCTSize; y++)
		for (var x = 0; x < DCTSize; x++)
			subMatrix[y * DCTSize + x] += shiftValue;
	}

	private static void SetPixels(Matrix matrix, float[] a, float[] b, float[] c, int yOffset, int xOffset)
	{
		for (var y = 0; y < DCTSize; y++)
		for (var x = 0; x < DCTSize; x++)
			matrix.Pixels[yOffset + y, xOffset + x] = new Pixel(a[y * DCTSize + x], b[y * DCTSize + x], c[y * DCTSize + x]);
	}

	private static byte[] ZigZagScan(byte[] channelFreqs, byte[] result)
	{
		(
			result[0], result[1], result[2],
			result[3], result[4],
			result[5], result[6], result[7],
			result[8], result[9], result[10],
			result[11], result[12],
			result[13], result[14], result[15],
			result[16], result[17], result[18],
			result[19], result[20],
			result[21], result[22], result[23],
			result[24], result[25], result[26],
			result[27], result[28],
			result[29], result[30], result[31],
			result[32], result[33], result[34],
			result[35], result[36],
			result[37], result[38], result[39],
			result[40], result[41], result[42],
			result[43], result[44],
			result[45], result[46], result[47],
			result[48], result[49], result[50],
			result[51], result[52],
			result[53], result[54], result[55],
			result[56], result[57], result[58],
			result[59], result[60],
			result[61], result[62], result[63]
		)
		=
		(
			channelFreqs[0 * DCTSize + 0], channelFreqs[0 * DCTSize + 1], channelFreqs[1 * DCTSize + 0],
			channelFreqs[2 * DCTSize + 0], channelFreqs[1 * DCTSize + 1],
			channelFreqs[0 * DCTSize + 2], channelFreqs[0 * DCTSize + 3], channelFreqs[1 * DCTSize + 2],
			channelFreqs[2 * DCTSize + 1], channelFreqs[3 * DCTSize + 0], channelFreqs[4 * DCTSize + 0],
			channelFreqs[3 * DCTSize + 1], channelFreqs[2 * DCTSize + 2],
			channelFreqs[1 * DCTSize + 3], channelFreqs[0 * DCTSize + 4], channelFreqs[0 * DCTSize + 5],
			channelFreqs[1 * DCTSize + 4], channelFreqs[2 * DCTSize + 3], channelFreqs[3 * DCTSize + 2],
			channelFreqs[4 * DCTSize + 1], channelFreqs[5 * DCTSize + 0],
			channelFreqs[6 * DCTSize + 0], channelFreqs[5 * DCTSize + 1], channelFreqs[4 * DCTSize + 2],
			channelFreqs[3 * DCTSize + 3], channelFreqs[2 * DCTSize + 4], channelFreqs[1 * DCTSize + 5],
			channelFreqs[0 * DCTSize + 6], channelFreqs[0 * DCTSize + 7],
			channelFreqs[1 * DCTSize + 6], channelFreqs[2 * DCTSize + 5], channelFreqs[3 * DCTSize + 4],
			channelFreqs[4 * DCTSize + 3], channelFreqs[5 * DCTSize + 2], channelFreqs[6 * DCTSize + 1],
			channelFreqs[7 * DCTSize + 0], channelFreqs[7 * DCTSize + 1],
			channelFreqs[6 * DCTSize + 2], channelFreqs[5 * DCTSize + 3], channelFreqs[4 * DCTSize + 4],
			channelFreqs[3 * DCTSize + 5], channelFreqs[2 * DCTSize + 6], channelFreqs[1 * DCTSize + 7],
			channelFreqs[2 * DCTSize + 7], channelFreqs[3 * DCTSize + 6],
			channelFreqs[4 * DCTSize + 5], channelFreqs[5 * DCTSize + 4], channelFreqs[6 * DCTSize + 3],
			channelFreqs[7 * DCTSize + 2], channelFreqs[7 * DCTSize + 3], channelFreqs[6 * DCTSize + 4],
			channelFreqs[5 * DCTSize + 5], channelFreqs[4 * DCTSize + 6],
			channelFreqs[3 * DCTSize + 7], channelFreqs[4 * DCTSize + 7], channelFreqs[5 * DCTSize + 6],
			channelFreqs[6 * DCTSize + 5], channelFreqs[7 * DCTSize + 4], channelFreqs[7 * DCTSize + 5],
			channelFreqs[6 * DCTSize + 6], channelFreqs[5 * DCTSize + 7],
			channelFreqs[6 * DCTSize + 7], channelFreqs[7 * DCTSize + 6], channelFreqs[7 * DCTSize + 7]
		);

		return result;
	}

	private static byte[] ZigZagUnScan(Span<byte> quantizedBytes, byte[] result)
	{
		(
			result[0], result[1], result[2], result[3], result[4], result[5], result[6], result[7],
			result[8], result[9], result[10], result[11], result[12], result[13], result[14], result[15],
			result[16], result[17], result[18], result[19], result[20], result[21], result[22], result[23],
			result[24], result[25], result[26], result[27], result[28], result[29], result[30], result[31],
			result[32], result[33], result[34], result[35], result[36], result[37], result[38], result[39],
			result[40], result[41], result[42], result[43], result[44], result[45], result[46], result[47],
			result[48], result[49], result[50], result[51], result[52], result[53], result[54], result[55],
			result[56], result[57], result[58], result[59], result[60], result[61], result[62], result[63]
		)
		=
		(
			quantizedBytes[0], quantizedBytes[1], quantizedBytes[5], quantizedBytes[6], quantizedBytes[14],
				quantizedBytes[15], quantizedBytes[27], quantizedBytes[28],
			quantizedBytes[2], quantizedBytes[4], quantizedBytes[7], quantizedBytes[13], quantizedBytes[16],
				quantizedBytes[26], quantizedBytes[29], quantizedBytes[42],
			quantizedBytes[3], quantizedBytes[8], quantizedBytes[12], quantizedBytes[17], quantizedBytes[25],
				quantizedBytes[30], quantizedBytes[41], quantizedBytes[43],
			quantizedBytes[9], quantizedBytes[11], quantizedBytes[18], quantizedBytes[24], quantizedBytes[31],
				quantizedBytes[40], quantizedBytes[44], quantizedBytes[53],
			quantizedBytes[10], quantizedBytes[19], quantizedBytes[23], quantizedBytes[32], quantizedBytes[39],
				quantizedBytes[45], quantizedBytes[52], quantizedBytes[54],
			quantizedBytes[20], quantizedBytes[22], quantizedBytes[33], quantizedBytes[38], quantizedBytes[46],
				quantizedBytes[51], quantizedBytes[55], quantizedBytes[60],
			quantizedBytes[21], quantizedBytes[34], quantizedBytes[37], quantizedBytes[47], quantizedBytes[50],
				quantizedBytes[56], quantizedBytes[59], quantizedBytes[61],
			quantizedBytes[35], quantizedBytes[36], quantizedBytes[48], quantizedBytes[49], quantizedBytes[57],
				quantizedBytes[58], quantizedBytes[62], quantizedBytes[63]
		);

		return result;
	}

	private static byte[] Quantize(float[] channelFreqs, byte[] result)
	{
		for (int y = 0; y < DCTSize; y++)
		{
			for (int x = 0; x < DCTSize; x++)
			{
				result[y * DCTSize + x] = (byte)(channelFreqs[y * DCTSize + x] / QuantizationMatrix[y, x]);
			}
		}

		return result;
	}

	private static float[] DeQuantize(byte[] quantizedBytes, float[] result)
	{
		for (int y = 0; y < DCTSize; y++)
		{
			for (int x = 0; x < DCTSize; x++)
			{
				result[y * DCTSize + x] =
					((sbyte)quantizedBytes[y * DCTSize + x]) *
					QuantizationMatrix[y, x]; //NOTE cast to sbyte not to lose negative numbers
			}
		}

		return result;
	}
}