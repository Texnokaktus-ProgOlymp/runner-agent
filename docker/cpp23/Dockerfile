ARG BASE_IMAGE=runner-agent-base:local
FROM ${BASE_IMAGE}
RUN apt-get update && apt-get install -y --no-install-recommends g++ \
    && rm -rf /var/lib/apt/lists/* \
    && test "$(g++ -dumpfullversion)" = '13.3.0'
COPY docker/cpp23/appsettings.Production.json /app/appsettings.Production.json
