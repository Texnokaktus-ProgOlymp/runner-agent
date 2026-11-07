namespace Texnokaktus.ProgOlymp.RunnerAgent.Storage;

public enum ObjectRole
{
    Source,
    Data
}

public sealed record ArtifactReference(string Key, byte[] Sha256);

public interface IObjectStorage
{
    Task DownloadAsync(
        ObjectRole role,
        string key,
        ReadOnlyMemory<byte> sha256,
        string path,
        CancellationToken cancellationToken
    );

    Task<ArtifactReference> UploadAsync(string path, string key, CancellationToken cancellationToken);
}
