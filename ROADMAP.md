# Pharos v0.1.0 Roadmap

All 22 issues target v0.1.0. The phases are sequential: each phase builds on
the previous one. Within a phase, `type: core` issues must be completed before
`type: enhancement` issues.

## Phase order

```
M1 Foundation ──► M2 Connection ──► M3 Inspection ──► M4 XUnit ──► M5 CI
   (#1-#5)          (#6-#9)           (#10-#14)        (#15-#18)    (#19-#22)
```

## M1: Foundation

Bootstrap a headless Vintage Story client in a test process. This phase
produces the core engine that boots and drives the client with no window and no
audio. Everything else depends on it.

| Order | Issue | Title | Type |
|---|---|---|---|
| 1 | [#1](https://github.com/Zaldaryon/Pharos/issues/1) | Bootstrap headless ClientMain with offscreen GLFW context | core |
| 2 | [#2](https://github.com/Zaldaryon/Pharos/issues/2) | Null audio device to bypass OpenAL hardware requirement | core |
| 3 | [#3](https://github.com/Zaldaryon/Pharos/issues/3) | Deterministic frame stepper: Client.Frame(dt) and Client.Frames(n) | core |
| 4 | [#5](https://github.com/Zaldaryon/Pharos/issues/5) | Hidden GLFW window with FBO on Windows for local headless runs | platform |
| 5 | [#4](https://github.com/Zaldaryon/Pharos/issues/4) | Mesa llvmpipe and Xvfb support for Linux headless rendering | platform |

### Gate

The client boots headless, advances frames deterministically, and runs on
both Windows and Linux. No server, no player, no rendering assertions yet.

---

## M2: Connection

Connect the headless client to a real server and control a test player's
camera, position, and inventory.

| Order | Issue | Title | Type |
|---|---|---|---|
| 6 | [#6](https://github.com/Zaldaryon/Pharos/issues/6) | Loopback singleplayer connection to embedded Atlas server | core |
| 7 | [#7](https://github.com/Zaldaryon/Pharos/issues/7) | Client-side test player with camera, position, and inventory | core |
| 8 | [#8](https://github.com/Zaldaryon/Pharos/issues/8) | World load synchronization: wait for chunk meshing before assertions | core |
| 9 | [#9](https://github.com/Zaldaryon/Pharos/issues/9) | Fixture/mock mode: inject pre-generated chunks without a live server | enhancement |

### Gate

A test player loads into a real world with meshed chunks. The camera is
controllable and chunk loading responds to camera movement. Fixture mode
enables fast isolated tests.

---

## M3: Inspection API

Expose the client's rendering internals so scenario code can assert on draw
calls, culling, memory, and pixels.

| Order | Issue | Title | Type |
|---|---|---|---|
| 10 | [#10](https://github.com/Zaldaryon/Pharos/issues/10) | OpenGL command proxy: record and inspect draw calls and buffer operations | core |
| 11 | [#11](https://github.com/Zaldaryon/Pharos/issues/11) | Frustum and culling state API: query which chunks are culled per frame | core |
| 12 | [#12](https://github.com/Zaldaryon/Pharos/issues/12) | Mesh pool and allocation metrics: track MeshData, ItemRenderInfo, buffer reuse | core |
| 13 | [#13](https://github.com/Zaldaryon/Pharos/issues/13) | Framebuffer pixel readback for visual regression comparisons | enhancement |
| 14 | [#14](https://github.com/Zaldaryon/Pharos/issues/14) | Shader uniform and UBO state inspection | enhancement |

### Gate

Scenario code can query draw calls, frustum culling state, allocation
metrics, framebuffer pixels, and shader uniforms after each frame.

---

## M4: XUnit Integration

Package the engine into xUnit attributes and lifecycle management so writing a
scenario is as simple as writing a test method.

| Order | Issue | Title | Type |
|---|---|---|---|
| 15 | [#15](https://github.com/Zaldaryon/Pharos/issues/15) | [ClientScenario] and [ClientTheory] xUnit attributes with lifecycle management | core |
| 16 | [#16](https://github.com/Zaldaryon/Pharos/issues/16) | Client isolation modes: fresh client, rollback camera/state, restart | core |
| 17 | [#17](https://github.com/Zaldaryon/Pharos/issues/17) | [PharosMods] attribute to stage client-side mods before boot | core |
| 18 | [#18](https://github.com/Zaldaryon/Pharos/issues/18) | Bridge mod: in-client inspection hooks exposed to scenario code | core |

### Gate

A user writes `[ClientScenario]`, inherits `ClientScenarioBase`, and gets a
fully managed client lifecycle with `Client`, `World`, isolation modes, and
mod staging. The bridge mod exposes rendering metrics to scenario code.

---

## M5: CI and Packaging

Ship the harness as NuGet packages with cross-platform CI and documentation.

| Order | Issue | Title | Type |
|---|---|---|---|
| 19 | [#19](https://github.com/Zaldaryon/Pharos/issues/19) | CI workflow: build, test with Mesa headless on Linux, test on Windows | infra |
| 20 | [#20](https://github.com/Zaldaryon/Pharos/issues/20) | NuGet packaging: Pharos, Pharos.Bridge, Pharos.XUnit | infra |
| 21 | [#21](https://github.com/Zaldaryon/Pharos/issues/21) | README, wiki, quickstart guide, and logo | docs |
| 22 | [#22](https://github.com/Zaldaryon/Pharos/issues/22) | Compatibility matrix: VS 1.21.x, 1.22.x, .NET 10 | docs |

### Gate

`dotnet add package Zaldaryon.Pharos.XUnit` works. CI is green on Linux and
Windows. A new user can follow the README to write and run their first client
scenario. v0.1.0 is published to NuGet.

---

## Summary

22 issues across 5 sequential phases. Each phase has a gate that must pass
before the next phase begins. All 22 issues are required for v0.1.0.
