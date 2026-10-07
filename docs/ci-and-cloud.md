# CI and Cloud Environments

Pharos runs the real Vintage Story client headlessly, so a test machine needs no GPU, no display and no game account. This page covers running the suite in CI and in cloud development environments, against the embedded server or against a dedicated server.

## What a machine needs

| Need | Linux | Windows |
|------|-------|---------|
| .NET | 10.0 SDK | 10.0 SDK |
| Vintage Story | The client tarball, with `VINTAGE_STORY` pointing at it | The installer, extracted, with `VINTAGE_STORY` pointing at it |
| OpenGL | Mesa llvmpipe (`libgl1-mesa-dri`) | Mesa for Windows next to the test binaries |
| Display | Xvfb (`xvfb-run`) | None |
| Audio | None: the null device, `ALSOFT_DRIVERS=null` | None |

`.github/workflows/ci.yml` sets all of this up on GitHub's Linux and Windows runners, and caches the game download between runs.

## Auth

Tests join offline by default: the client answers the server's login token itself and nothing contacts the auth server. The embedded server accepts that, and so does the dedicated server image below, because both run with `VerifyPlayerAuth` off. See [Engine Mode](engine-mode.md#real-network-connections-and-auth).

To also run the online auth tests, give the job an account session as secrets: `PHAROS_VS_PLAYERNAME`, `PHAROS_VS_PLAYERUID`, `PHAROS_VS_SESSIONKEY` and `PHAROS_VS_SESSIONSIGNATURE`. The CI workflow passes them through when they exist; without them those tests are skipped. The session key is a credential: store it only as a secret.

## A dedicated server

`docker/dedicated-server` builds a dedicated server for tests: offline auth, no whitelist, a creative superflat world.

```bash
docker compose -f docker/dedicated-server/compose.yaml up -d --build
docker compose -f docker/dedicated-server/compose.yaml logs -f   # wait for "Dedicated Server now running"
PHAROS_REMOTE_SERVER=127.0.0.1:42420 dotnet test --filter FullyQualifiedName~RemoteServerTests
```

`PHAROS_REMOTE_SERVER` (`host:port`) points the `RemoteServerTests` at the server; without it they are skipped. The `Remote Dedicated Server (Linux)` CI job builds the image, waits for the server, and runs them.

Where there is no Docker daemon, as in many cloud development environments, run the same configuration from a Vintage Story install directly; every client install ships the server:

```bash
VS_INSTALL=$VINTAGE_STORY VS_DATA=/tmp/vs-server VS_PORT=42420 docker/dedicated-server/start-server.sh &
```

`start-server.sh` reads `VS_INSTALL`, `VS_DATA`, `VS_PORT`, `VS_VERIFY_PLAYER_AUTH`, `VS_WORLD_TYPE` and `VS_PLAYSTYLE`. These servers are for tests: they admit anyone, so keep them off the internet.

In a test, `HeadlessClient.ConnectRemote(host, port, auth)` joins such a server and returns a `RemoteServerSession`, which steps only the client since the server keeps its own clock:

```csharp
using RemoteServerSession session = client.ConnectRemote("127.0.0.1", 42420, ClientAuth.Offline("Tester"));
Assert.True(await session.WaitForPlayerJoinedAsync(TimeSpan.FromSeconds(180)));
```

## Cloud development environments

A cloud environment such as a Claude Code cloud session needs the same pieces, installed by its setup script:

```bash
# .NET 10 and the native libraries
apt-get update && apt-get install -y libgl1-mesa-dri xvfb libopenal1 libglfw3 libcairo2
# Vintage Story, once
mkdir -p /opt/vs && wget -q https://cdn.vintagestory.at/gamefiles/stable/vs_client_linux-x64_1.22.7.tar.gz -O /tmp/vs.tgz \
  && tar xzf /tmp/vs.tgz -C /opt/vs --strip-components=1
```

Then set `VINTAGE_STORY=/opt/vs` and run the suite with `scripts/run-headless-linux.sh`. The environment's network policy must allow `cdn.vintagestory.at` for the download, and `auth3.vintagestory.at` only if you run the online auth tests.

## Slow runners

Shared CI runners render in software and are often busy. Pharos tests wait in frames or server ticks for a condition, bounded by a generous limit, rather than for a fixed time or a fixed number of steps, so a slow runner only makes them slower. Write new scenarios the same way: step until the outcome is visible, with a limit, and assert after.
