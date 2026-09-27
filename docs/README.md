# Documentation

Design and engineering documentation for FallenAngel. These documents were written in Chinese, the project's working language, and each now carries an English summary at the top. The project overview is also fully translated. If you would like a full translation of any other document, ask and it can be done.

| Document | What it covers |
| --- | --- |
| [project-overview.md](project-overview.md) | What the game is, how it plays, what is implemented, known issues and the roadmap. **Start here.** |
| [architecture.md](architecture.md) | Layering and dependency rules, the singleton pattern, event conventions and coding standards for new code. |
| [chart-format.md](chart-format.md) | The notation-to-chart conversion protocol: a two-stage pipeline (front-end parser + declarative rule engine) and the v2 chart schema. |
| [chart-editor.md](chart-editor.md) | How to use the in-engine 5-lane chart editor: shortcuts, note types, snapping and validation. |
| [note-types.md](note-types.md) | The mapping table from musical notation (drums, guitar, bass) to FallenAngel note types. |
| [roguelike-chapter1-design.md](roguelike-chapter1-design.md) | The chapter 1 design baseline (millennium dreamcore): world, run structure, gear, companions, pacing and numbers. |
| [common-gear-spec.md](common-gear-spec.md) | Implementation spec for the 16 common gear items: per-item rule, code seam, acceptance steps and open decisions. |
| [chapter-rooms-spec.md](chapter-rooms-spec.md) | Design spec for chapter 1's rooms: run flow, per-room rules, route-fee options, per-layer splits and the content tables still to build. |
| [chapter-route-spec.md](chapter-route-spec.md) | Route design: why the run branches, the composition rules, a hand-authored first map with node/edge tables, and the choice model. Supersedes the linear route. |

Elsewhere in the repository: [`../README.md`](../README.md) is the project overview in English and Chinese; [`../portfolio/`](../portfolio) holds portfolio figures; [`../CLAUDE.md`](../CLAUDE.md) is the AI collaboration brief; [`../chart_tools/`](../chart_tools) holds the analysis and conversion tooling.
