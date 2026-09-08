*English · [한국어](README_kr.md)*

# TraceLens

**A local development tool that shows the call path of an ASP.NET Core Web API together with the code behind it, and hands an AI coding agent the context it needs.**

Everything here comes from reading your source **statically**. TraceLens does not collect runtime
traces or timings — the "Trace" in the name means following call paths through code, not distributed
tracing.

## The loop, in about a minute

```
1) tracelens analyze <your .sln or .csproj>       # analyzes and writes .tracelens/graph.json
2) tracelens serve   --target <same path>         # opens the UI at http://127.0.0.1:25011
3) pick an entry point → check the source → copy AI context   # evidence and limits come along
4) after the AI edits code → "Re-analyze and compare"          # see what actually changed
```

Screen recordings are re-shot for each release. The commands and screens in this document match the
current repository (main); where a capture does not exist yet, none is shown — no mockups are
presented as real screens.

## Getting started

### What you need

| Requirement | When it is needed |
|---|---|
| .NET 8 SDK or later | Always. It is what opens the project you analyze. |
| Node.js 20 or later | **Only when building from source.** Not needed with the released ZIP or .NET tool. |
| Claude Code CLI | Only for the ask panel's "Run with Claude Code". Copying context works without it. |
| Codex CLI / VS Code | Only if you want to register the TraceLens MCP server directly ([docs/mcp-setup.md](docs/mcp-setup.md)). |

### Start from the build output (no source tree, no Node.js)

```powershell
# Unpack the ZIP produced from this repository, then:
.\tracelens.exe doctor --target C:\repos\MyApp\MyApp.sln   # check the environment first
.\tracelens.exe demo --dir C:\temp\tracelens-demo          # copy the example project
.\tracelens.exe analyze C:\temp\tracelens-demo\OrderApi\OrderApi.csproj
.\tracelens.exe serve   --target C:\temp\tracelens-demo\OrderApi\OrderApi.csproj
```

To produce that build output yourself, run `.\pack.ps1` in the repository. It writes a Windows x64
portable ZIP and a .NET tool package into `artifacts/`, along with a `manifest.txt` holding
checksums, versions and the manual verification steps. The script only builds — it never publishes.

Only combinations that have actually been verified are listed here.

| Platform | Status |
|---|---|
| Windows 11 x64, .NET 8 runtime + .NET SDK | Verified with the portable ZIP: doctor, analyze, serve and source viewing |
| macOS / Linux | Not verified yet. The code targets net8.0 and will likely run, but it is not listed as supported |

Supported analysis targets are `.sln` and `.csproj`. `.slnx` is not supported yet because the
MSBuildWorkspace in use cannot open it; `doctor` says so explicitly rather than failing obscurely.

### Run from source

```powershell
git clone https://github.com/jacking75/DotNetApiServerTraceLens.git tracelens
cd tracelens
.\run.ps1
```

`run.ps1` installs client dependencies, builds the server, builds the client for production and
starts the server — all in one go. Open <http://127.0.0.1:25011> and that is the whole app.

| Option | Description |
|---|---|
| `.\run.ps1` | Builds the client and serves everything from one server. The everyday path. |
| `.\run.ps1 -Dev` | Runs the API server (25011) and the Vite dev server (25012) together, for client work. |
| `.\run.ps1 -SkipBuild` | Skips the build and runs immediately (Node.js is not needed in this mode). |
| `.\run.ps1 -NoBrowser` | Does not open a browser. |

### Commands

```text
tracelens doctor  [--target <path>] [--json]   Checks the environment. Never installs or changes settings.
tracelens analyze <path> [--json]              Runs one analysis and prints a summary.
tracelens serve   [--target <path>] [--port n] Starts the UI server (loopback only).
tracelens mcp     --graph <graph.json path>    Starts the local stdio MCP server.
tracelens demo    [--dir <destination>]        Copies the example project and explains what to look at.
```

## What is supported today

The table separates what static analysis can confirm from what it cannot.

| Area | Support | Notes |
|---|---|---|
| Controller entry points | Supported | `[HttpGet]`-family attributes plus `[Route]`. Two routes on one method become two endpoints. |
| Minimal API entry points | Supported | `MapGet/MapPost/MapPut/MapDelete/MapPatch/MapMethods/Map`, `MapGroup` prefixes (including nesting), and method-group, lambda and local-function handlers. |
| Registrations inside extension methods | Supported (estimated) | Follows `app.MapFeatureEndpoints()` style registration up to a depth of 4, and says that the prefix came from the call site. |
| Dynamic routes | Not supported | If the route is not a constant, the literal source text is shown and the endpoint is marked **estimated**. |
| Interface calls | Supported | Resolved to the implementation through DI registrations (`AddScoped<I, Impl>` and friends). Without a registration, it connects only when the solution has exactly one implementation, and marks that **estimated**. |
| Keyed / conditional DI registrations | Not asserted | Which implementation runs cannot be decided statically, so no edge is created and the reason is recorded. |
| Database and cache access | Supported (configurable) | Read from SQL literals and cache calls. Point `db.queryApis` in `tracelens.config.json` at your own wrapper types. |
| Reflection, event subscriptions, message handlers | Not supported | Never turned into call edges. "No callers found" does not mean "safe to delete". |

Every analysis carries **what it could not see**. The summary bar and the AI context both show
`resolution rate = resolved supported call sites / inspected supported call sites`, how many projects
had compile errors, and how many call sites stayed unresolved. That rate is not "accuracy".

## Six views

The toolbar switches between **Call flow · Class diagram · Flow map · Startup flow · Data & cache ·
Middleware**. The UI ships in Korean and English; pick the language in the header (the browser
language is used as the default).

- **Call flow** — pick one entry point, draw to the first branch, then expand a level at a time with `⊕`.
- **Class diagram** — the whole solution as UML boxes on one canvas, with density controls: collapse members, toggle each of the four relationship kinds, namespace checkboxes, and zoom. Past 150 classes it starts collapsed.
- **Flow map** — an entry-point map; click a route row to enter that entry point's full, method-level path.
- **Startup flow** — Program.cs read in source order: DI registrations with lifetimes, and the middleware pipeline order.
- **Data & cache** — the storage access map, plus which steps of a transaction-free write path are not rolled back.
- **Middleware** — `app.UseXxx()` order, the request/response halves of each middleware, and short-circuit points.

Findings always come with **evidence and a confidence level (certain / estimated)**, and rules that
were not checked stay in the catalog marked "off", so you always know what was not looked at.

## Working with an AI agent

1. Select something on screen: an entry point, method, class, finding or write path.
2. In the ask panel pick an **intent** (understand / plan a change / review a change / investigate data) and a **length** (short, balanced, detailed).
3. Check the preview: which files are included, whether source text is in there, how long it is, what was omitted.
4. **Copy context** into whichever AI tool you use, or **Run with Claude Code** to ask right there.

The context is assembled in this order:

1. Your intent and question, plus the identifiers of what you selected
2. Analysis time, snapshotId, freshness, analysis scope, and the significant unresolved reasons
3. The selected method's signature and a **verified** source range (marked if it no longer matches the analysis)
4. The representative call path from the entry point to the target, with the evidence for each call site (direct call / DI registration / single-implementation guess)
5. Direct callers and callees, plus related database, cache and middleware findings
6. Test candidates found in the graph — not the result of running any test
7. What was omitted for length, and how to fetch more

> The analysis is local, but **asking an external AI sends that context to that service.**
> That is why there is a preview before you send, an option to leave source text out, and per-file
> exclusion. Automatic secret detection alone is not treated as a safety guarantee.

### Connecting it to your AI tool (MCP)

TraceLens ships a local stdio MCP server. "Show MCP setup" in the ask panel prints a snippet filled
in with your actual target and installed host — it never overwrites your existing configuration.
[docs/mcp-setup.md](docs/mcp-setup.md) has the full walkthrough.

| Tool | What it returns |
|---|---|
| `get_analysis_status` | Target, timestamp, freshness, capabilities and a diagnostics summary |
| `get_context` | The same task context the UI previews, with evidence, paths and limitations |
| `get_entrypoint_flow` | One entry point's bounded call path and its boundaries |
| `get_findings` | Database and middleware findings with confidence, plus the list of disabled rules |
| `get_impact` | Entry points that may be affected by changing a method, with the evidence path |
| `compare_snapshots` | Structural, body and finding differences against a saved baseline |
| `get_node` · `get_source` · `get_callers` · `get_callees` · `get_entrypoints` · `search_symbol` | Nodes, source, adjacent calls, entry points and symbol search |

Every tool **reads an already-saved analysis and never re-analyzes**. Responses carry a `snapshotId`;
pass it back as `expectedSnapshotId` on follow-up calls to find out if the analysis changed underneath you.

## Change impact and before/after comparison

- **Change impact**: select a method and press "Find affected APIs" to walk callers backwards to the HTTP entry points that reach it. The result is a list of **candidates** — it does not prove anything about reflection, external calls, event subscriptions or runtime DI selection.
- **Before/after**: press "Save as before" to record a baseline, let the AI edit the code, then press "Re-analyze and compare". You get differences in endpoints, methods (signature and body), calls, data access and findings. If the analyzer, rules or scope differ, the panel says the comparison conditions are not the same. **"No differences" is not "safe", and is not "the tests pass".**

## Freshness and re-analysis

Nothing is watched in the background and nothing re-analyzes itself. Re-analysis is always something
you trigger. What you do get is a freshness indicator that always tells you whether the last analysis
still matches the code.

- `fresh` — every recorded input matches what is on disk.
- `stale` — something changed, was deleted, or is **newly added**, or a config input changed.
- `unknown` — the check could not cover it (for example an analysis saved in an older format). It is never reported as `fresh` in that case.

## How this relates to other tools

Commercial and open-source tools that trace callers or do impact analysis already exist. NDepend, for
example, offers impact exploration through callers and its own official MCP server
([impact analysis docs](https://www.ndepend.com/docs/refactoring-impact-analysis),
[MCP server repository](https://github.com/ndepend/NDepend.MCP.Server) — a separate license or trial
is required).

TraceLens aims narrower: tying **request-centric ASP.NET Core exploration, local static analysis,
AI context with evidence attached, and database/cache compensation checking** into one workflow, under
the MIT license. That is a positioning statement, not a measured claim of superiority.

## Documentation

- [docs/manual.html](docs/manual.html) — the manual: screen layout, all six views, AI integration, real usage walkthroughs
- [docs/mcp-setup.md](docs/mcp-setup.md) — registering the MCP server with Claude Code, Codex CLI and VS Code
- [docs/data-contract.md](docs/data-contract.md) — the `graph.json` schema, the API and MCP contracts, and migration rules
- [CONTRIBUTING.md](CONTRIBUTING.md) — how to report a missed call, and what contributions are in scope
- [CHANGELOG.md](CHANGELOG.md) — release notes (updated by hand)
- [CLAUDE.md](CLAUDE.md) — repository instructions and design constraints for AI coding agents

## Tech stack

| Area | Choice | Notes |
|---|---|---|
| Analysis | C# / Roslyn | Extracts method call relationships with exact file and line information |
| Shared queries | `TraceLens.Context` | Index, context, impact, comparison and source-access policy. Shared by the server and MCP |
| Server | ASP.NET Core Minimal API | Binds to loopback only. There is no option to expose it remotely |
| Frontend | React + TypeScript (Vite) | Call flow uses Cytoscape.js; other diagrams are DOM+SVG; the code view is Monaco |
| Storage | JSON under `.tracelens/` | Written to a temp file, verified, then swapped in |
| Agent integration | Local stdio MCP plus optional Claude Code CLI invocation | Nothing is exposed remotely |

## License

MIT. See [LICENSE](LICENSE).
