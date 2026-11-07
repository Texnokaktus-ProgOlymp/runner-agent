namespace Texnokaktus.ProgOlymp.RunnerAgent.Storage;

public static class ObjectKeys
{
    public static string Artifact(string prefix, string suffix)
    {
        ArgumentException.ThrowIfNullOrEmpty(prefix);

        return prefix.EndsWith('/') ? prefix + suffix : prefix + "/" + suffix;
    }
}
