*English · [한국어](mcp-setup_kr.md)*

# TraceLens MCP setup

TraceLens.Mcp is a local stdio MCP server that reads the analysis target solution's `.tracelens/graph.json`. It doesn't open a network port, and it re-reads the graph file on every tool call. Since it's a standard MCP stdio server, any MCP-capable CLI — Claude Code, Codex CLI, and others — can use it the same way; only the registration steps differ. Both are covered below.

## Before you start

Build the whole solution first, from the repository root.

```powershell
dotnet build TraceLens.sln
```

The analysis target solution must have been analyzed at least once via the TraceLens UI or `POST /api/analyze`, so that `.tracelens/graph.json` exists.

## Register with Claude Code

Run this with the paths swapped for your actual repository and analysis target.

```powershell
claude mcp add --scope local tracelens -- dotnet "C:\repos\tracelens\src\TraceLens.Mcp\bin\Debug\net8.0\TraceLens.Mcp.dll" --graph "C:\repos\MyApp\.tracelens\graph.json"
```

After registering, run `/mcp` in Claude Code, or check status with:

```powershell
claude mcp get tracelens
```

### `.mcp.json` example

To manage project-scoped settings as a file, place this at the root of the analysis target solution.

```json
{
  "mcpServers": {
    "tracelens": {
      "type": "stdio",
      "command": "dotnet",
      "args": [
        "C:\\repos\\tracelens\\src\\TraceLens.Mcp\\bin\\Debug\\net8.0\\TraceLens.Mcp.dll",
        "--graph",
        "C:\\repos\\MyApp\\.tracelens\\graph.json"
      ]
    }
  }
}
```

The TraceLens UI's `MCP Direct` mode works without any registration at all. `POST /api/ask` builds a `.tracelens/mcp.json` that matches the current analysis result and passes it to the **Claude Code CLI** via `--mcp-config`. This mode is Claude Code-only, and is unrelated to the Codex CLI registration below.

## Register with Codex CLI

Codex CLI is registered the same way. Swap in the paths for your actual repository and analysis target.

```powershell
codex mcp add tracelens -- dotnet "C:\repos\tracelens\src\TraceLens.Mcp\bin\Debug\net8.0\TraceLens.Mcp.dll" --graph "C:\repos\MyApp\.tracelens\graph.json"
```

Check registration status with `codex mcp list`, or by running `/mcp` inside a `codex` session.

### `config.toml` example

Codex CLI keeps server entries as TOML in `~/.codex/config.toml` (or a trusted project's `.codex/config.toml`). `codex mcp add` writes the same thing into this file automatically.

```toml
[mcp_servers.tracelens]
command = "dotnet"
args = [
  "C:\\repos\\tracelens\\src\\TraceLens.Mcp\\bin\\Debug\\net8.0\\TraceLens.Mcp.dll",
  "--graph",
  "C:\\repos\\MyApp\\.tracelens\\graph.json",
]
```

The TraceLens UI's `MCP Direct` mode never calls Codex CLI — to use TraceLens's tools from Codex CLI, register it as above and then ask directly inside a `codex` session.

## Tools provided

- `get_node(nodeId)`: returns node metadata and its source.
- `get_source(file, startLine, endLine)`: returns the given line range from source inside the solution.
- `get_callers(nodeId)`, `get_callees(nodeId)`: return directly connected callers and callees.
- `get_entrypoints()`: returns the list of HTTP entry points.
- `search_symbol(query)`: searches nodes by display name, full ID, or namespace.

`file` only accepts paths relative to the analysis target solution's root. Paths outside the solution are rejected.
