# A Vintage Story dedicated server for Pharos tests: offline auth, no whitelist,
# a creative superflat world. Not meant to face the internet.
FROM mcr.microsoft.com/dotnet/runtime:10.0

ARG VS_VERSION=1.22.7

RUN apt-get update \
 && apt-get install -y --no-install-recommends wget ca-certificates \
 && rm -rf /var/lib/apt/lists/*

# The server tarball's layout has changed between releases; take the folder
# that holds VintagestoryServer.dll, wherever it is.
RUN mkdir -p /tmp/vs /opt/vintagestory \
 && wget -q "https://cdn.vintagestory.at/gamefiles/stable/vs_server_linux-x64_${VS_VERSION}.tar.gz" -O /tmp/vs.tar.gz \
 && tar xzf /tmp/vs.tar.gz -C /tmp/vs \
 && cp -a "$(dirname "$(find /tmp/vs -name VintagestoryServer.dll | head -n 1)")/." /opt/vintagestory/ \
 && rm -rf /tmp/vs /tmp/vs.tar.gz

COPY start-server.sh /usr/local/bin/start-server.sh
RUN chmod +x /usr/local/bin/start-server.sh

ENV VS_INSTALL=/opt/vintagestory \
    VS_DATA=/data \
    VS_PORT=42420

VOLUME /data
EXPOSE 42420/tcp 42420/udp

ENTRYPOINT ["/usr/local/bin/start-server.sh"]
