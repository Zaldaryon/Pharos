# Pharos Roadmap

Pharos is the unified headless testing framework for Vintage Story client and server systems.

## Release Progression

```
v0.1.0 Foundation (M1-M5) ──► v0.2.0 Optimum & Automation (M6-M10) ──► v0.3.0 Atlas Independence (M11-M14)
```

---

## v0.1.0 Foundation (Complete)

All 22 issues for v0.1.0 are complete.

### M1: Foundation
Bootstrap a headless Vintage Story client in a test process without an OS window or audio hardware.

| Order | Issue | Title | Type | Status |
|---|---|---|---|---|
| 1 | [#1](https://github.com/Zaldaryon/Pharos/issues/1) | Bootstrap headless ClientMain with offscreen GLFW context | core | Complete |
| 2 | [#2](https://github.com/Zaldaryon/Pharos/issues/2) | Null audio device to bypass OpenAL hardware requirement | core | Complete |
| 3 | [#3](https://github.com/Zaldaryon/Pharos/issues/3) | Deterministic frame stepper: Client.Frame(dt) and Client.Frames(n) | core | Complete |
| 4 | [#5](https://github.com/Zaldaryon/Pharos/issues/5) | Hidden GLFW window with FBO on Windows for local headless runs | platform | Complete |
| 5 | [#4](https://github.com/Zaldaryon/Pharos/issues/4) | Mesa llvmpipe and Xvfb support for Linux headless rendering | platform | Complete |

### M2: Connection
Connect the headless client to a server and control test player camera, position, and inventory.

| Order | Issue | Title | Type | Status |
|---|---|---|---|---|
| 6 | [#6](https://github.com/Zaldaryon/Pharos/issues/6) | Loopback singleplayer connection to embedded Atlas server | core | Complete |
| 7 | [#7](https://github.com/Zaldaryon/Pharos/issues/7) | Client-side test player with camera, position, and inventory | core | Complete |
| 8 | [#8](https://github.com/Zaldaryon/Pharos/issues/8) | World load synchronization: wait for chunk meshing before assertions | core | Complete |
| 9 | [#9](https://github.com/Zaldaryon/Pharos/issues/9) | Fixture/mock mode: inject pre-generated chunks without a live server | enhancement | Complete |

### M3: Inspection API
Expose client rendering internals for draw calls, culling, memory, and pixel assertions.

| Order | Issue | Title | Type | Status |
|---|---|---|---|---|
| 10 | [#10](https://github.com/Zaldaryon/Pharos/issues/10) | OpenGL command proxy: record and inspect draw calls and buffer operations | core | Complete |
| 11 | [#11](https://github.com/Zaldaryon/Pharos/issues/11) | Frustum and culling state API: query which chunks are culled per frame | core | Complete |
| 12 | [#12](https://github.com/Zaldaryon/Pharos/issues/12) | Mesh pool and allocation metrics: track MeshData, ItemRenderInfo, buffer reuse | core | Complete |
| 13 | [#13](https://github.com/Zaldaryon/Pharos/issues/13) | Framebuffer pixel readback for visual regression comparisons | enhancement | Complete |
| 14 | [#14](https://github.com/Zaldaryon/Pharos/issues/14) | Shader uniform and UBO state inspection | enhancement | Complete |

### M4: XUnit Integration
Package the engine into xUnit attributes and test lifecycle management.

| Order | Issue | Title | Type | Status |
|---|---|---|---|---|
| 15 | [#15](https://github.com/Zaldaryon/Pharos/issues/15) | [ClientScenario] and [ClientTheory] xUnit attributes with lifecycle management | core | Complete |
| 16 | [#16](https://github.com/Zaldaryon/Pharos/issues/16) | Client isolation modes: fresh client, rollback camera/state, restart | core | Complete |
| 17 | [#17](https://github.com/Zaldaryon/Pharos/issues/17) | [PharosMods] attribute to stage client-side mods before boot | core | Complete |
| 18 | [#18](https://github.com/Zaldaryon/Pharos/issues/18) | Bridge mod: in-client inspection hooks exposed to scenario code | core | Complete |

### M5: CI and Packaging
Ship the harness as NuGet packages with cross-platform CI and documentation.

| Order | Issue | Title | Type | Status |
|---|---|---|---|---|
| 19 | [#19](https://github.com/Zaldaryon/Pharos/issues/19) | CI workflow: build, test with Mesa headless on Linux, test on Windows | infra | Complete |
| 20 | [#20](https://github.com/Zaldaryon/Pharos/issues/20) | NuGet packaging: Pharos, Pharos.Bridge, Pharos.XUnit | infra | Complete |
| 21 | [#21](https://github.com/Zaldaryon/Pharos/issues/21) | README, wiki, quickstart guide, and logo | docs | Complete |
| 22 | [#22](https://github.com/Zaldaryon/Pharos/issues/22) | Compatibility matrix: VS 1.21.x, 1.22.x, .NET 10 | docs | Complete |

---

## v0.2.0 Optimum & Automation (Complete)

All 18 issues across milestones M6 through M10 are complete.

### M6: Optimum & Engine Verification
Validate Optimum chunk rendering, GPU indirect draw, SIMD frustum culling, greedy meshing, FSR upscaling, and ecosystem mod compatibility.

| Order | Issue | Title | Type | Status |
|---|---|---|---|---|
| 1 | [#44](https://github.com/Zaldaryon/Pharos/issues/44) | GPU Indirect Draw verification and multi-draw command buffer inspection | core | Complete |
| 2 | [#45](https://github.com/Zaldaryon/Pharos/issues/45) | SIMD frustum culling validation and BFS visibility traversal verification | core | Complete |
| 3 | [#46](https://github.com/Zaldaryon/Pharos/issues/46) | Greedy mesher face validation and chiseled block LOD distance falloff testing | core | Complete |
| 4 | [#47](https://github.com/Zaldaryon/Pharos/issues/47) | FSR 1.0 EASU/RCAS upscaling and render scale pipeline validation | enhancement | Complete |
| 5 | [#48](https://github.com/Zaldaryon/Pharos/issues/48) | Ecosystem mod compatibility simulation for Komet, OptiTime, Tungsten, and Synergy | core | Complete |

### M7: Input Simulation & UI Automation
Automate virtual mouse, keyboard input, GUI dialog inspection, inventory manipulation, and block interactions.

| Order | Issue | Title | Type | Status |
|---|---|---|---|---|
| 6 | [#49](https://github.com/Zaldaryon/Pharos/issues/49) | Deterministic virtual mouse and keyboard input controller | core | Complete |
| 7 | [#50](https://github.com/Zaldaryon/Pharos/issues/50) | Headless GUI inspection and widget interaction API | core | Complete |
| 8 | [#51](https://github.com/Zaldaryon/Pharos/issues/51) | Inventory slot interaction, drag-and-drop, and crafting grid automation | core | Complete |
| 9 | [#52](https://github.com/Zaldaryon/Pharos/issues/52) | Deterministic block interaction: placement, breaking, and tool usage simulation | core | Complete |

### M8: Network Simulation & Replay Harness
Enable deterministic packet recording, offline packet replay, simulated network degradation, and disconnect handling.

| Order | Issue | Title | Type | Status |
|---|---|---|---|---|
| 10 | [#53](https://github.com/Zaldaryon/Pharos/issues/53) | Deterministic server packet recorder and offline replay harness | core | Complete |
| 11 | [#54](https://github.com/Zaldaryon/Pharos/issues/54) | Network degradation simulation: latency, packet drop, and jitter on DummyNetwork | enhancement | Complete |
| 12 | [#55](https://github.com/Zaldaryon/Pharos/issues/55) | Client session reconnect, server disconnect, and crash containment testing | core | Complete |

### M9: Visual Regression & Memory Leak Profiling
Golden snapshot comparisons, perceptual diff heatmaps, OpenGL resource tracking, and automated test reporting.

| Order | Issue | Title | Type | Status |
|---|---|---|---|---|
| 13 | [#56](https://github.com/Zaldaryon/Pharos/issues/56) | Golden image test assertions with perceptual diff heatmaps and CI artifact upload | core | Complete |
| 14 | [#57](https://github.com/Zaldaryon/Pharos/issues/57) | Continuous OpenGL resource and unmanaged memory leak detector | core | Complete |
| 15 | [#58](https://github.com/Zaldaryon/Pharos/issues/58) | Automated HTML/Markdown diagnostic test report generator with frame metrics | enhancement | Complete |

### M10: Pharos CLI & Developer Tooling
Command-line test runner, chunk fixture generator, 2D slice visualization, and performance regression benchmark suites.

| Order | Issue | Title | Type | Status |
|---|---|---|---|---|
| 16 | [#59](https://github.com/Zaldaryon/Pharos/issues/59) | Standalone pharos CLI tool for headless test execution and scenario runners | infra | Complete |
| 17 | [#60](https://github.com/Zaldaryon/Pharos/issues/60) | Chunk fixture generator and ASCII/PNG slice preview CLI | enhancement | Complete |
| 18 | [#61](https://github.com/Zaldaryon/Pharos/issues/61) | Optimum automated performance regression scenario benchmark suite | core | Complete |

---

## v0.3.0 100% Atlas Independence (Complete)

All 14 issues across milestones M11 through M14 are complete. Pixnop.Atlas has been removed as a dependency. Pharos now ships a native embedded server engine, deterministic tick control, in-memory world rollback, server scenario xUnit attributes, lockstep client-server testing, and a compatibility shim for existing Atlas test suites.

### M11: Embedded Server Engine
Implement native embedded Vintage Story server engine, deterministic tick controllers, data isolation sandboxes, in-memory world rollback, and native loopback bindings.

| Order | Issue | Title | Type | Status |
|---|---|---|---|---|
| 1 | [#80](https://github.com/Zaldaryon/Pharos/issues/80) | Native EmbeddedServerHost and ServerWorldOptions replacing AtlasServerHost | core | Complete |
| 2 | [#81](https://github.com/Zaldaryon/Pharos/issues/81) | Deterministic server tick controller and condition-based tick waiter | core | Complete |
| 3 | [#82](https://github.com/Zaldaryon/Pharos/issues/82) | Server data path isolation, savegame management, and in-memory world rollback | core | Complete |
| 4 | [#83](https://github.com/Zaldaryon/Pharos/issues/83) | Loopback network binding and HeadlessClient native server connection | core | Complete |

### M12: Server Scenarios & Testing Framework
Provide native xUnit server scenario attributes, base test class, player fixtures, command dispatch, and server world manipulation helpers.

| Order | Issue | Title | Type | Status |
|---|---|---|---|---|
| 5 | [#84](https://github.com/Zaldaryon/Pharos/issues/84) | [ServerScenario] and [ServerTheory] xUnit attributes with server lifecycle and rollback | core | Complete |
| 6 | [#85](https://github.com/Zaldaryon/Pharos/issues/85) | ServerScenarioBase with console and player command execution APIs | core | Complete |
| 7 | [#86](https://github.com/Zaldaryon/Pharos/issues/86) | IServerTestPlayer fixture with genuine ConnectedClient and cleanup handling | core | Complete |
| 8 | [#87](https://github.com/Zaldaryon/Pharos/issues/87) | Server world state helpers: block placement, entity spawning, and event waiting | enhancement | Complete |

### M13: Unified Client-Server Testing
Unify client testing and server testing into synchronized in-process lockstep scenarios with packet and state assertions.

| Order | Issue | Title | Type | Status |
|---|---|---|---|---|
| 9 | [#88](https://github.com/Zaldaryon/Pharos/issues/88) | [ClientServerScenario] attribute and ClientServerScenarioBase scaffold | core | Complete |
| 10 | [#89](https://github.com/Zaldaryon/Pharos/issues/89) | Lockstep client-server synchronization: ClientServerSession frame and tick coordination | core | Complete |
| 11 | [#90](https://github.com/Zaldaryon/Pharos/issues/90) | End-to-end client-server action verification helpers and packet assertion APIs | enhancement | Complete |

### M14: Atlas Package Removal & Migration Shim
Remove Pixnop.Atlas package reference, migrate internal tests, build drop-in compatibility shim, and provide migration CLI tooling.

| Order | Issue | Title | Type | Status |
|---|---|---|---|---|
| 12 | [#91](https://github.com/Zaldaryon/Pharos/issues/91) | Drop Pixnop.Atlas dependency and migrate Pharos internal test suite | core | Complete |
| 13 | [#92](https://github.com/Zaldaryon/Pharos/issues/92) | Zaldaryon.Pharos.AtlasCompat drop-in compatibility shim for Atlas test suites | core | Complete |
| 14 | [#93](https://github.com/Zaldaryon/Pharos/issues/93) | Atlas to Pharos migration documentation and CLI migration tool | docs | Complete |
