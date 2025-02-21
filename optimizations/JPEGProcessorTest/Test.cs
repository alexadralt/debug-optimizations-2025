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
    public Task VerifyImage()
    {
        var imagePath = new App().Run(@"sample.bmp");
        return VerifyFile(imagePath);
    }
}