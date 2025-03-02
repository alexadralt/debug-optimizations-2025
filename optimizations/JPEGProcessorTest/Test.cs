using System.Runtime.CompilerServices;
using JPEG;

namespace JPEGProcessorTest;

[TestFixture]
public class Test
{
    [ModuleInitializer]
    public static void Init() =>
        VerifyImageSharp.Initialize();
    
    [Test]
    public Task VerifySample()
    {
        var imagePath = new App().Run(@"sample.bmp");
        return VerifyFile(imagePath);
    }
    
    [Test]
    public Task VerifyMarbles()
    {
        var imagePath = new App().Run(@"MARBLES.bmp");
        return VerifyFile(imagePath);
    }
}