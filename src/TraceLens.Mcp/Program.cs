using TraceLens.Mcp;

// 호스트 구성은 McpHost에 있다(CLI의 `tracelens mcp`가 같은 구성을 쓴다).
return await McpHost.RunAsync(args);
