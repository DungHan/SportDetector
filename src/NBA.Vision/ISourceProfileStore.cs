namespace NBA.Vision;

public interface ISourceProfileStore
{
    SourceProfile? Load(string sourceKey);

    void Save(SourceProfile profile);
}
