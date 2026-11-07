using Texnokaktus.ProgOlymp.RunnerAgent.Storage;

namespace Texnokaktus.ProgOlymp.RunnerAgent.Tests;

[NonParallelizable]
public sealed class StorageConfigurationTests
{
    [TestCase("run", "run/tests/8/stdout.txt")]
    [TestCase("run/", "run/tests/8/stdout.txt")]
    [TestCase("a/../b", "a/../b/tests/8/stdout.txt")]
    [TestCase(" ", " /tests/8/stdout.txt")]
    public void PrefixIsAnOpaqueObjectKey(string prefix, string expected) =>
        Assert.That(ObjectKeys.Artifact(prefix, "tests/8/stdout.txt"), Is.EqualTo(expected));

    [Test]
    public void RejectsEmptyPrefix() => Assert.That(
        () => ObjectKeys.Artifact("", "stdout.txt"),
        Throws.ArgumentException
    );

    [TestCase("http://user:private-password@localhost:9000")]
    [TestCase("file:///tmp/storage")]
    [TestCase("http://localhost:9000?token=private-password")]
    public void InvalidEndpointDoesNotLeakItsValue(string endpoint)
    {
        var exception =
            Assert.Throws<InvalidOperationException>(() => new S3Options { Endpoint = endpoint }.Validate());

        Assert.That(exception!.ToString(), Does.Not.Contain("private-password"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CredentialsAreExplicitEnvironmentSnapshots(bool session)
    {
        var access = Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID");
        var secret = Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY");
        var token = Environment.GetEnvironmentVariable("AWS_SESSION_TOKEN");

        try
        {
            Environment.SetEnvironmentVariable("AWS_ACCESS_KEY_ID", "test-access");
            Environment.SetEnvironmentVariable("AWS_SECRET_ACCESS_KEY", "test-secret");
            Environment.SetEnvironmentVariable("AWS_SESSION_TOKEN", session ? "test-session" : null);
            var credentials = S3Options.ReadCredentials();
            Environment.SetEnvironmentVariable("AWS_SECRET_ACCESS_KEY", "changed");
            var snapshot = credentials.GetCredentials();
            Assert.That(snapshot.AccessKey, Is.EqualTo("test-access"));
            Assert.That(snapshot.SecretKey, Is.EqualTo("test-secret"));
            Assert.That(snapshot.UseToken, Is.EqualTo(session));
        }
        finally
        {
            Environment.SetEnvironmentVariable("AWS_ACCESS_KEY_ID", access);
            Environment.SetEnvironmentVariable("AWS_SECRET_ACCESS_KEY", secret);
            Environment.SetEnvironmentVariable("AWS_SESSION_TOKEN", token);
        }
    }

    [Test]
    public void MissingEnvironmentDoesNotFallBackToProfiles()
    {
        var access = Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID");
        var profile = Environment.GetEnvironmentVariable("AWS_PROFILE");

        try
        {
            Environment.SetEnvironmentVariable("AWS_ACCESS_KEY_ID", null);
            Environment.SetEnvironmentVariable("AWS_PROFILE", "default");
            Assert.That(S3Options.ReadCredentials, Throws.InvalidOperationException);
        }
        finally
        {
            Environment.SetEnvironmentVariable("AWS_ACCESS_KEY_ID", access);
            Environment.SetEnvironmentVariable("AWS_PROFILE", profile);
        }
    }
}
