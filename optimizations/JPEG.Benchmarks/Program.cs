using BenchmarkDotNet.Running;
using JPEG.Benchmarks.Benchmarks;

namespace JPEG.Benchmarks;

internal class Program
{
	public static void Main(string[] args)
	{
		BenchmarkRunner.Run<JpegProcessorBenchmark>();
	}
}