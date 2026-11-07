using Amazon.Runtime;
using Amazon.S3;

namespace Texnokaktus.ProgOlymp.RunnerAgent.Storage;

internal sealed class S3Options
{
    public string Endpoint { get; set; } = "http://minio:9000";
    public string SourceBucket { get; set; } = "runner-sources";
    public string DataBucket { get; set; } = "runner-data";
    public string SigningRegion { get; set; } = "us-east-1";

    public void Validate()
    {
        if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out var endpoint)
         || endpoint.Scheme is not ("http" or "https")
         || endpoint.UserInfo.Length != 0
         || endpoint.Query.Length != 0
         || endpoint.Fragment.Length != 0
         || string.IsNullOrWhiteSpace(SourceBucket)
         || string.IsNullOrWhiteSpace(DataBucket)
         || SourceBucket == DataBucket
         || string.IsNullOrWhiteSpace(SigningRegion))
            throw new InvalidOperationException(
                "S3 must specify a valid endpoint, distinct buckets and signing region."
            );
    }

    public AmazonS3Config CreateClientConfig() => new()
    {
        ServiceURL = Endpoint,
        AuthenticationRegion = SigningRegion,
        ForcePathStyle = true,
        RetryMode = RequestRetryMode.Standard,
        MaxErrorRetry = 0,
        MaxStaleConnectionRetries = 0,
        DisableClockSkewCorrection = true,
        DisableLogging = true
    };

    public static AWSCredentials ReadCredentials()
    {
        var access = Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID");
        var secret = Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY");
        var token = Environment.GetEnvironmentVariable("AWS_SESSION_TOKEN");

        if (string.IsNullOrWhiteSpace(access)
         || string.IsNullOrWhiteSpace(secret)
         || token is not null && token.Length != 0 && string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException(
                "AWS_ACCESS_KEY_ID and AWS_SECRET_ACCESS_KEY must be set; AWS_SESSION_TOKEN is optional."
            );

        return string.IsNullOrEmpty(token)
                   ? new BasicAWSCredentials(access, secret)
                   : new SessionAWSCredentials(access, secret, token);
    }
}
