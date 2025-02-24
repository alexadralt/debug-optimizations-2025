using System;
using System.Linq;

namespace JPEG.Images;

public struct Pixel
{
	public Pixel(float firstComponent, float secondComponent, float thirdComponent)
	{
		value1 = firstComponent;
		value2 = secondComponent;
		value3 = thirdComponent;
	}

	public float value1;
	public float value2;
	public float value3;
}