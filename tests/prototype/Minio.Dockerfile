FROM ubuntu:24.04
RUN apt-get update && apt-get install -y --no-install-recommends ca-certificates curl \
    && rm -rf /var/lib/apt/lists/*
RUN curl -fSL --retry 3 --retry-all-errors https://github.com/minio/minio/releases/download/RELEASE.2025-04-22T22-12-26Z/minio.linux-amd64.RELEASE.2025-04-22T22-12-26Z -o /usr/local/bin/minio \
    && echo '53e2a2cb16c5366ea6fbbc479c19ddb4c6a0948273e752f740fb1fbf27bb817c  /usr/local/bin/minio' | sha256sum -c - \
    && chmod 755 /usr/local/bin/minio
ENTRYPOINT ["/usr/local/bin/minio"]
