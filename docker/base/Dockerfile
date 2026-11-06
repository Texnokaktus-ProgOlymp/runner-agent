FROM mcr.microsoft.com/dotnet/aspnet:10.0.14 AS isolate-build
RUN apt-get update && apt-get install -y --no-install-recommends \
    ca-certificates curl gcc make pkg-config libcap-dev libseccomp-dev libc6-dev \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /src/isolate
RUN curl -fsSL https://github.com/ioi/isolate/archive/8f185bb37f3f23e29b33b0c7727c91c13429abe3.tar.gz \
    | tar xz --strip-components=1 \
    && make isolate PREFIX=/usr CONFIGDIR=/etc BOXDIR=/var/lib/isolate SYSTEMD_CFLAGS= SYSTEMD_LIBS=

FROM mcr.microsoft.com/dotnet/aspnet:10.0.14
RUN apt-get update && apt-get install -y --no-install-recommends libcap2 libseccomp2 \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /var/lib/isolate /run/isolate/locks /var/lib/runner-agent /usr/local/lib/runner-agent
COPY --from=isolate-build /src/isolate/isolate /usr/bin/isolate
COPY artifacts/host/ /app/
COPY scripts/sandbox-environment.sh /usr/local/lib/runner-agent/environment.sh
COPY scripts/entrypoint.sh /usr/local/bin/runner-agent
RUN sed -i 's/\r$//' /usr/local/lib/runner-agent/environment.sh /usr/local/bin/runner-agent \
    && chmod 755 /usr/local/bin/runner-agent
WORKDIR /app
ENTRYPOINT ["/usr/local/bin/runner-agent"]
