# Security policy

TraceLens is a **local development tool**. The server binds to `127.0.0.1` only, MCP uses local stdio, and
there is no option to expose either to the network. It reads the source of the solution you point it at,
and it can launch a local `claude`/`codex` CLI only when you press the button for it.

## Reporting a vulnerability

Please do **not** open a public issue for a security problem. Use GitHub's private
[security advisory form](https://github.com/jacking75/DotNetApiServerTraceLens/security/advisories/new)
for this repository. Include the version, the steps to reproduce, and the impact you see.

Things that count as security problems here include: a way to read files outside the analyzed solution
through the source viewer, a way to make the server reachable from another machine, command injection
through the agent launcher, and path traversal in the browse or import endpoints.

## Supported versions

Only the latest release on `main` is supported.
