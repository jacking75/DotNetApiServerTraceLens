*English · [한국어](mcp-setup_kr.md)*

# TraceLens MCP setup

The TraceLens MCP server is a **local stdio** server that reads the analysis target's
`.tracelens/graph.json`. It opens no network port, and it **only reads an already-saved analysis —
it never re-analyzes.** Because it is a standard MCP stdio server, any MCP-capable tool (Claude Code,
Codex CLI, VS Code) can use it; only the registration format differs.

## The easy way: let the app generate the snippet

After analyzing in the TraceLens UI, press **"Show MCP setup"** in the ask panel. It prints a
configuration snippet with the real paths for your target and installed host filled in.
**Nothing is overwritten** — add what it prints to the matching file.

The same thing is available over HTTP:

```powershell
curl http://127.0.0.1:25011/api/mcp-setup
```

## Prerequisites

1. With the released build, `tracelens.exe` itself is the MCP host. From source, build first:

   ```powershell
   dotnet build TraceLens.sln
   ```

2. Analyze the target at least once so `.tracelens/graph.json` exists:

   ```powershell
   tracelens analyze C:\repos\MyApp\MyApp.sln
   # or analyze in the UI, or POST /api/analyze
   ```

## The launch command

From the released build (recommended):

```text
command: C:\tools\tracelens\tracelens.exe
args:    ["mcp", "--graph", "C:\\repos\\MyApp\\.tracelens\\graph.json"]
```

From source:

```text
command: dotnet
args:    ["C:\\repos\\tracelens\\src\\TraceLens.Mcp\\bin\\Debug\\net8.0\\TraceLens.Mcp.dll",
          "--graph", "C:\\repos\\MyApp\\.tracelens\\graph.json"]
```

`TRACELENS_GRAPH_PATH` works instead of `--graph` if you prefer an environment variable.

## Claude Code

```powershell
claude mcp add --scope local tracelens -- C:\tools\tracelens\tracelens.exe mcp --graph "C:\repos\MyApp\.tracelens\graph.json"
claude mcp get tracelens
```

To keep it in a file, put `.mcp.json` at the analysis target's root. If the file already exists, add
only the `tracelens` entry under `mcpServers`.

```json
{
  "mcpServers": {
    "tracelens": {
      "type": "stdio",
      "command": "C:\\tools\\tracelens\\tracelens.exe",
      "args": ["mcp", "--graph", "C:\\repos\\MyApp\\.tracelens\\graph.json"]
    }
  }
}
```

The UI's **"Run with Claude Code"** works without any of this: `POST /api/ask` writes a
`.tracelens/mcp.json` for the current analysis and passes it to the Claude Code CLI via
`--mcp-config`. That mode is Claude Code-specific and independent of the registrations below.

## Codex CLI

```powershell
codex mcp add tracelens -- C:\tools\tracelens\tracelens.exe mcp --graph "C:\repos\MyApp\.tracelens\graph.json"
codex mcp list
```

Or write it into `~/.codex/config.toml` (or a trusted project's `.codex/config.toml`) directly:

```toml
[mcp_servers.tracelens]
command = "C:\\tools\\tracelens\\tracelens.exe"
args = ["mcp", "--graph", "C:\\repos\\MyApp\\.tracelens\\graph.json"]
```

## VS Code

VS Code supports local MCP servers and the tools they expose. Put this in `.vscode/mcp.json` in your
workspace; if the file exists, add only the `tracelens` entry under `servers`.

```json
{
  "servers": {
    "tracelens": {
      "type": "stdio",
      "command": "C:\\tools\\tracelens\\tracelens.exe",
      "args": ["mcp", "--graph", "C:\\repos\\MyApp\\.tracelens\\graph.json"]
    }
  }
}
```

## The tools

### Call this first

| Tool | Input | Returns |
|---|---|---|
| `get_analysis_status` | `expectedSnapshotId?` | Target, timestamp, freshness, diagnostics and capabilities — how much to trust everything else. |

### Task context

| Tool | Input | Returns |
|---|---|---|
| `get_context` | `selectionKind`, `selectionId`, `intent?`, `budget?`, `question?`, `expectedSnapshotId?` | The same task context the UI previews: signature, verified source, path from the entry point, evidence and limitations. |
| `get_entrypoint_flow` | `endpointId`, `depth?`, `maxNodes?` | One entry point's bounded call path plus the calls that could not be resolved. |
| `get_findings` | `nodeId?`, `ruleId?`, `limit?`, `cursor?` | Database and middleware findings with confidence, plus **the list of disabled rules**. |
| `get_impact` | `nodeIds`, `maxDepth?`, `maxNodes?` | Entry points that may be affected by changing a method, with the evidence path. |
| `compare_snapshots` | `baselineId`, `currentId?` | Structural, body and finding differences against a saved baseline, and whether the comparison conditions match. |

`selectionKind` is one of `node`, `endpoint`, `class`, `namespace`, `finding`, `writePath`,
`middlewareStep`, `startupStep`. `intent` is `explain`, `planChange`, `reviewChange`,
`investigateData` or `share`; `budget` is `short`, `balanced` or `detailed`.

### Individual lookups

| Tool | Input | Returns |
|---|---|---|
| `get_node` | `nodeId`, `expectedSnapshotId?` | Node metadata and that method's source. |
| `get_source` | `file`, `startLine`, `endLine` | Source inside the analysis root, plus `sourceVersion` (snapshot/current). |
| `get_callers` · `get_callees` | `nodeId`, `limit?`, `cursor?` | Adjacent nodes with **call evidence**, plus the unresolved boundary list. |
| `get_entrypoints` | `query?`, `limit?`, `cursor?` | HTTP entry points. Two routes on one method appear as two entries. |
| `search_symbol` | `query`, `limit?`, `cursor?` | Node search by display name, full id or namespace. |

## Things worth knowing

- **It never re-analyzes.** If the code changed, the user has to run `tracelens analyze` or press "Re-analyze" in the UI. When `get_analysis_status` reports `stale`, say so in your answer.
- **It tells you when the snapshot changed.** Pass a response's `snapshotId` back as `expectedSnapshotId` on follow-up calls, and you get `SNAPSHOT_MISMATCH` if the analysis changed in between.
- **Source access is restricted.** `file` must be relative to the analysis root, and only files this snapshot actually analyzed can be read. Symlink and junction targets are resolved and checked, and responses are size-capped.
- **An empty list is not "nothing".** `get_callers`/`get_callees` return a `boundaries` list of calls that could not be resolved. Read it before concluding there are no callers.
- **Large answers page.** List tools accept `limit`/`cursor`; follow `nextCursor` to continue.

## Verifying it works

Do not stop at "the tools appear". Ask a real question:

```text
Use tracelens get_analysis_status to check the current analysis, then call get_context for the
POST /api/orders entry point and explain what it writes, citing the evidence.
```

It only counts as working when the answer quotes real files and line numbers.
