# Pharos v0.1.0 Roadmap

All 22 issues for v0.1.0 are complete. The phases were sequential: each phase built on the previous one. Within a phase, `type: core` issues were completed before `type: enhancement` issues.

## Phase Order

```
M1 Foundation ──► M2 Connection ──► M3 Inspection ──► M4 XUnit ──► M5 CI
   (#1-#5)          (#6-#9)           (#10-#14)        (#15-#18)    (#19-#22)
```

## M1: Foundation ✓

Bootstrap a headless Vintage Story client in a test process. This phase produced the core engine that boots and drives the client with no window and no audio.

| Order | Issue | Title | Type | Status |
|-------|-------|-------|------|--------|
| 1 | [#1](https://github.com/Zaldaryon/Pharos/issues/1) | Bootstrap headless ClientMain with offscreen GLFW context | core | ✓ Done |
| 2 | [#2](https://github.com/Zaldaryon/Pharos/issues/2) | Null audio device to bypass OpenAL hardware requirement | core | ✓ Done |
| 3 | [#3](https://github.com/Zaldaryon/Pharos/issues/3) | Deterministic frame stepper: Client.Frame(dt) and Client.Frames(n) | core | ✓ Done |
| 4 | [#5](https://github.com/Zaldaryon/Pharos/issues/5) | Hidden GLFW window with FBO on Windows for local headless runs | platform | ✓ Done |
| 5 | [#4](https://github.com/Zaldaryon/Pharos/issues/4) | Mesa llvmpipe and Xvfb support for Linux headless rendering | platform | ✓ Done |

## M2: Connection ✓

Connect the headless client to a real server and control a test player's camera, position, and inventory.

| Order | Issue | Title | Type | Status |
|-------|-------|-------|------|--------|
| 6 | [#6](https://github.com/Zaldaryon/Pharos/issues/6) | Loopback singleplayer connection to embedded Atlas server | core | ✓ Done |
| 7 | [#7](https://github.com/Zaldaryon/Pharos/issues/7) | Client-side test player with camera, position, and inventory | core | ✓ Done |
| 8 | [#8](https://github.com/Zaldaryon/Pharos/issues/8) | World load synchronization: wait for chunk meshing before assertions | core | ✓ Done |
| 9 | [#9](https://github.com/Zaldaryon/Pharos/issues/9) | Fixture/mock mode: inject pre-generated chunks without a live server | enhancement | ✓ Done |

## M3: Inspection API ✓

Expose the client's rendering internals so scenario code can assert on draw calls, culling, memory, and pixels.

| Order | Issue | Title | Type | Status |
|-------|-------|-------|------|--------|
| 10 | [#10](https://github.com/Zaldaryon/Pharos/issues/10) | OpenGL command proxy: record and inspect draw calls and buffer operations | core | ✓ Done |
| 11 | [#11](https://github.com/Zaldaryon/Pharos/issues/11) | Frustum and culling state API: query which chunks are culled per frame | core | ✓ Done |
| 12 | [#12](https://github.com/Zaldaryon/Pharos/issues/12) | Mesh pool and allocation metrics: track MeshData, ItemRenderInfo, buffer reuse | core | ✓ Done |
| 13 | [#13](https://github.com/Zaldaryon/Pharos/issues/13) | Framebuffer pixel readback for visual regression comparisons | enhancement | ✓ Done |
| 14 | [#14](https://github.com/Zaldaryon/Pharos/issues/14) | Shader uniform and UBO state inspection | enhancement | ✓ Done |

## M4: XUnit Integration ✓

Package the engine into xUnit attributes and lifecycle management so writing a scenario is as simple as writing a test method.

| Order | Issue | Title | Type | Status |
|-------|-------|-------|------|--------|
| 15 | [#15](https://github.com/Zaldaryon/Pharos/issues/15) | [ClientScenario] and [ClientTheory] xUnit attributes with lifecycle management | core | ✓ Done |
| 16 | [#16](https://github.com/Zaldaryon/Pharos/issues/16) | Client isolation modes: fresh client, rollback camera/state, restart | core | ✓ Done |
| 17 | [#17](https://github.com/Zaldaryon/Pharos/issues/17) | [PharosMods] attribute to stage client-side mods before boot | core | ✓ Done |
| 18 | [#18](https://github.com/Zaldaryon/Pharos/issues/18) | Bridge mod: in-client inspection hooks exposed to scenario code | core | ✓ Done |

## M5: CI and Packaging ✓

Ship the harness as NuGet packages with cross-platform CI and documentation.

| Order | Issue | Title | Type | Status |
|-------|-------|-------|------|--------|
| 19 | [#19](https://github.com/Zaldaryon/Pharos/issues/19) | CI workflow: build, test with Mesa headless on Linux, test on Windows | infra | ✓ Done |
| 20 | [#20](https://github.com/Zaldaryon/Pharos/issues/20) | NuGet packaging: Pharos, Pharos.Bridge, Pharos.XUnit | infra | ✓ Done |
| 21 | [#21](https://github.com/Zaldaryon/Pharos/issues/21) | README, wiki, quickstart guide, and logo | docs | ✓ Done |
| 22 | [#22](https://github.com/Zaldaryon/Pharos/issues/22) | Compatibility matrix: VS 1.21.x, 1.22.x, .NET 10 | docs | ✓ Done |

## Summary

All 22 issues across 5 sequential phases are complete. Each phase gate passed before the next phase began. v0.1.0 is ready for release.

`dotnet add package Zaldaryon.Pharos.XUnit` works. CI is green on Linux and Windows. A new user can follow the README to write and run their first client scenario.
