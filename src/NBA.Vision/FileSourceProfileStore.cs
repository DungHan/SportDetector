using System.Text.Json;

namespace NBA.Vision;

/// <summary>One JSON file per source, under a configurable directory - simple, human-inspectable, and easy to back up/delete.</summary>
public sealed class FileSourceProfileStore : ISourceProfileStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly string _directory;

    public FileSourceProfileStore(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(_directory);
    }

    public SourceProfile? Load(string sourceKey)
    {
        var path = PathFor(sourceKey);
        if (!File.Exists(path))
        {
            return null;
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<SourceProfile>(json, SerializerOptions);
    }

    public void Save(SourceProfile profile)
    {
        var path = PathFor(profile.SourceKey);
        var json = JsonSerializer.Serialize(profile, SerializerOptions);
        File.WriteAllText(path, json);
    }

    private string PathFor(string sourceKey)
    {
        var safeFileName = string.Concat(sourceKey.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return Path.Combine(_directory, $"{safeFileName}.json");
    }
}
