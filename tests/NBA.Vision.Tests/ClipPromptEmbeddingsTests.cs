using System.Text.Json;

namespace NBA.Vision.Tests;

public class ClipPromptEmbeddingsTests
{
    [Fact]
    public void LoadFromJson_NormalizesEmbeddingsAndParsesSport()
    {
        var path = Path.Combine(Path.GetTempPath(), $"clip-prompts-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(new[]
            {
                new { sport = "basketball", prompt = "a basketball game", embedding = new[] { 3f, 4f, 0f } }, // norm 5
                new { sport = "soccer", prompt = "a soccer match", embedding = new[] { 0f, 0f, 5f } }, // norm 5
            }));

            var prompts = ClipPromptEmbeddings.LoadFromJson(path);

            Assert.Equal(2, prompts.Count);

            var basketball = prompts.Single(p => p.Sport == SportType.Basketball);
            Assert.Equal(0.6f, basketball.Embedding[0], precision: 5);
            Assert.Equal(0.8f, basketball.Embedding[1], precision: 5);
            Assert.Equal(0f, basketball.Embedding[2], precision: 5);

            var soccer = prompts.Single(p => p.Sport == new SportType("soccer"));
            Assert.Equal(0f, soccer.Embedding[0], precision: 5);
            Assert.Equal(0f, soccer.Embedding[1], precision: 5);
            Assert.Equal(1f, soccer.Embedding[2], precision: 5);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadFromJson_EmptyList_Throws()
    {
        var path = Path.Combine(Path.GetTempPath(), $"clip-prompts-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "[]");

            Assert.Throws<InvalidDataException>(() => ClipPromptEmbeddings.LoadFromJson(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadFromJson_AllZeroEmbedding_Throws()
    {
        var path = Path.Combine(Path.GetTempPath(), $"clip-prompts-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(new[]
            {
                new { sport = "basketball", prompt = "a basketball game", embedding = new[] { 0f, 0f, 0f } },
            }));

            Assert.Throws<InvalidDataException>(() => ClipPromptEmbeddings.LoadFromJson(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
