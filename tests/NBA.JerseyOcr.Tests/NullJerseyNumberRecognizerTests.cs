namespace NBA.JerseyOcr.Tests;

public class NullJerseyNumberRecognizerTests
{
    [Fact]
    public void Recognize_AlwaysReturnsUnrecognized_RegardlessOfInput()
    {
        var recognizer = new NullJerseyNumberRecognizer();
        var pixels = new byte[16 * 4 * 16];

        var result = recognizer.Recognize(pixels, width: 16, height: 16, stride: 16 * 4, cropLeft: 0, cropTop: 0, cropRight: 16, cropBottom: 16);

        Assert.Null(result.Number);
    }
}
