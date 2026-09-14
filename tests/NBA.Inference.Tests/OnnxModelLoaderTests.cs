using NBA.Inference;

namespace NBA.Inference.Tests;

public class OnnxModelLoaderTests
{
    private static string FixturePath => Path.Combine(AppContext.BaseDirectory, "Assets", "add-one.onnx");

    [Fact]
    public void Load_ValidModel_Succeeds()
    {
        using var session = OnnxModelLoader.Load(FixturePath);

        Assert.NotNull(session);
        Assert.Contains(session.InputMetadata.Keys, k => k == "input");
    }

    [Fact]
    public void Load_MissingFile_ThrowsModelLoadExceptionWithPath()
    {
        var missingPath = Path.Combine(AppContext.BaseDirectory, "Assets", "does-not-exist.onnx");

        var ex = Assert.Throws<ModelLoadException>(() => OnnxModelLoader.Load(missingPath));

        Assert.Equal(missingPath, ex.ModelPath);
        Assert.Contains(missingPath, ex.Message);
    }

    [Fact]
    public void Load_InvalidModelBytes_ThrowsModelLoadException()
    {
        var invalidPath = Path.Combine(Path.GetTempPath(), $"nba-invalid-{Guid.NewGuid():N}.onnx");
        File.WriteAllBytes(invalidPath, [0x00, 0x01, 0x02, 0x03]);

        try
        {
            var ex = Assert.Throws<ModelLoadException>(() => OnnxModelLoader.Load(invalidPath));
            Assert.Equal(invalidPath, ex.ModelPath);
        }
        finally
        {
            File.Delete(invalidPath);
        }
    }
}
