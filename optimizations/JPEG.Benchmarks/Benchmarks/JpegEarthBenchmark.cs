using BenchmarkDotNet.Attributes;
using JPEG.Processor;

namespace JPEG.Benchmarks.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(warmupCount: 2, iterationCount: 3)]
public class JpegEarthBenchmark
{
    private IJpegProcessor jpegProcessor;
    private static readonly string imagePath = @"C:\kontur-homework\debug-optimizations-2025\optimizations\JPEG.Benchmarks\bin\Release\net7.0\earth.bmp";
    private static readonly string compressedImagePath = imagePath + ".compressed." + JpegProcessor.CompressionQuality;
    private static readonly string uncompressedImagePath =
        imagePath + ".uncompressed." + JpegProcessor.CompressionQuality + ".bmp";
    
    [GlobalSetup]
    public void SetUp()
    {
        jpegProcessor = JpegProcessor.Init;
    }

    [Benchmark]
    public void Compress()
    {
        jpegProcessor.Compress(imagePath, compressedImagePath);
    }

    [Benchmark]
    public void Uncompress()
    {
        jpegProcessor.Uncompress(compressedImagePath, uncompressedImagePath);
    }
}