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
		var bytesCollection = new List<((int, int), List<byte>)>();

		var width = matrix.Width / DCTSize;
		var height = matrix.Height / DCTSize;
		var chunksCount = width * height;
		var partitioner = Partitioner.Create(0, chunksCount, chunksCount / Environment.ProcessorCount);
		Parallel.ForEach(partitioner, range =>
		{
			var subMatrix = new float[DCTSize * DCTSize];
			
			for (var k = range.Item1; k < range.Item2; k++)
			{
				var x = (k % width) * DCTSize;
				var y = (k / width) * DCTSize;
				var bytes = new List<byte>();

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
					var channelFreqs = DCT.DCT2D(subMatrix);
					var quantizedFreqs = Quantize(channelFreqs);
					var quantizedBytes = ZigZagScan(quantizedFreqs);
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
					var channelFreqs = DCT.DCT2D(subMatrix);
					var quantizedFreqs = Quantize(channelFreqs);
					var quantizedBytes = ZigZagScan(quantizedFreqs);
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
					var channelFreqs = DCT.DCT2D(subMatrix);
					var quantizedFreqs = Quantize(channelFreqs);
					var quantizedBytes = ZigZagScan(quantizedFreqs);
					bytes.AddRange(quantizedBytes);
				}

				lock (bytesCollection)
				{
					bytesCollection.Add(((x, y), bytes));
				}
			}
		});
		
		var allQuantizedBytes = new List<byte>();
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
			for (int i = range.Item1; i < range.Item2; i++)
			{
				var x = (i % width) * DCTSize;
				var y = (i / width) * DCTSize;
				var byteIndex = i * 3 * sizeSquared;
				
				float[] _y;
				{
					var byteSpan = new Span<byte>(allQuantizedBytes, byteIndex, sizeSquared);
					var quantizedFreqs = ZigZagUnScan(byteSpan);
					var channelFreqs = DeQuantize(quantizedFreqs);
					_y = DCT.IDCT2D(channelFreqs);
					ShiftMatrixValues(_y, 128);
				}
					
				float[] cb;
				{
					var byteSpan = new Span<byte>(allQuantizedBytes, byteIndex + sizeSquared, sizeSquared);
					var quantizedFreqs = ZigZagUnScan(byteSpan);
					var channelFreqs = DeQuantize(quantizedFreqs);
					cb = DCT.IDCT2D(channelFreqs);
					ShiftMatrixValues(cb, 128);
				}
					
				float[] cr;
				{
					var byteSpan = new Span<byte>(allQuantizedBytes, byteIndex + sizeSquared * 2, sizeSquared);
					var quantizedFreqs = ZigZagUnScan(byteSpan);
					var channelFreqs = DeQuantize(quantizedFreqs);
					cr = DCT.IDCT2D(channelFreqs);
					ShiftMatrixValues(cr, 128);
				}
		
				SetPixels(result, _y, cb, cr, y, x);
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

	private static byte[] ZigZagScan(byte[,] channelFreqs)
	{
		return new[]
		{
			channelFreqs[0, 0], channelFreqs[0, 1], channelFreqs[1, 0], channelFreqs[2, 0], channelFreqs[1, 1],
			channelFreqs[0, 2], channelFreqs[0, 3], channelFreqs[1, 2],
			channelFreqs[2, 1], channelFreqs[3, 0], channelFreqs[4, 0], channelFreqs[3, 1], channelFreqs[2, 2],
			channelFreqs[1, 3], channelFreqs[0, 4], channelFreqs[0, 5],
			channelFreqs[1, 4], channelFreqs[2, 3], channelFreqs[3, 2], channelFreqs[4, 1], channelFreqs[5, 0],
			channelFreqs[6, 0], channelFreqs[5, 1], channelFreqs[4, 2],
			channelFreqs[3, 3], channelFreqs[2, 4], channelFreqs[1, 5], channelFreqs[0, 6], channelFreqs[0, 7],
			channelFreqs[1, 6], channelFreqs[2, 5], channelFreqs[3, 4],
			channelFreqs[4, 3], channelFreqs[5, 2], channelFreqs[6, 1], channelFreqs[7, 0], channelFreqs[7, 1],
			channelFreqs[6, 2], channelFreqs[5, 3], channelFreqs[4, 4],
			channelFreqs[3, 5], channelFreqs[2, 6], channelFreqs[1, 7], channelFreqs[2, 7], channelFreqs[3, 6],
			channelFreqs[4, 5], channelFreqs[5, 4], channelFreqs[6, 3],
			channelFreqs[7, 2], channelFreqs[7, 3], channelFreqs[6, 4], channelFreqs[5, 5], channelFreqs[4, 6],
			channelFreqs[3, 7], channelFreqs[4, 7], channelFreqs[5, 6],
			channelFreqs[6, 5], channelFreqs[7, 4], channelFreqs[7, 5], channelFreqs[6, 6], channelFreqs[5, 7],
			channelFreqs[6, 7], channelFreqs[7, 6], channelFreqs[7, 7]
		};
	}

	private static byte[,] ZigZagUnScan(Span<byte> quantizedBytes)
	{
		return new[,]
		{
			{
				quantizedBytes[0], quantizedBytes[1], quantizedBytes[5], quantizedBytes[6], quantizedBytes[14],
				quantizedBytes[15], quantizedBytes[27], quantizedBytes[28]
			},
			{
				quantizedBytes[2], quantizedBytes[4], quantizedBytes[7], quantizedBytes[13], quantizedBytes[16],
				quantizedBytes[26], quantizedBytes[29], quantizedBytes[42]
			},
			{
				quantizedBytes[3], quantizedBytes[8], quantizedBytes[12], quantizedBytes[17], quantizedBytes[25],
				quantizedBytes[30], quantizedBytes[41], quantizedBytes[43]
			},
			{
				quantizedBytes[9], quantizedBytes[11], quantizedBytes[18], quantizedBytes[24], quantizedBytes[31],
				quantizedBytes[40], quantizedBytes[44], quantizedBytes[53]
			},
			{
				quantizedBytes[10], quantizedBytes[19], quantizedBytes[23], quantizedBytes[32], quantizedBytes[39],
				quantizedBytes[45], quantizedBytes[52], quantizedBytes[54]
			},
			{
				quantizedBytes[20], quantizedBytes[22], quantizedBytes[33], quantizedBytes[38], quantizedBytes[46],
				quantizedBytes[51], quantizedBytes[55], quantizedBytes[60]
			},
			{
				quantizedBytes[21], quantizedBytes[34], quantizedBytes[37], quantizedBytes[47], quantizedBytes[50],
				quantizedBytes[56], quantizedBytes[59], quantizedBytes[61]
			},
			{
				quantizedBytes[35], quantizedBytes[36], quantizedBytes[48], quantizedBytes[49], quantizedBytes[57],
				quantizedBytes[58], quantizedBytes[62], quantizedBytes[63]
			}
		};
	}

	private static byte[,] Quantize(float[] channelFreqs)
	{
		var width = DCTSize;
		var height = DCTSize;
		var result = new byte[width, height];

		for (int y = 0; y < width; y++)
		{
			for (int x = 0; x < height; x++)
			{
				result[y, x] = (byte)(channelFreqs[y * width + x] / QuantizationMatrix[y, x]);
			}
		}

		return result;
	}

	private static float[] DeQuantize(byte[,] quantizedBytes)
	{
		var result = GC.AllocateUninitializedArray<float>(DCTSize * DCTSize);

		for (int y = 0; y < DCTSize; y++)
		{
			for (int x = 0; x < DCTSize; x++)
			{
				result[y * DCTSize + x] =
					((sbyte)quantizedBytes[y, x]) *
					QuantizationMatrix[y, x]; //NOTE cast to sbyte not to lose negative numbers
			}
		}

		return result;
	}
}