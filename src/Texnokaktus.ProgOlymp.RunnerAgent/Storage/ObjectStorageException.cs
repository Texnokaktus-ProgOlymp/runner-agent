namespace Texnokaktus.ProgOlymp.RunnerAgent.Storage;

public sealed class ObjectStorageException(string code) : Exception("Object storage operation failed.")
{
    public string Code { get; } = code;
}
