namespace TraceLens.Mcp;

internal static class GraphPathArguments
{
    public static string Resolve(string[] args)
    {
        var optionIndex = Array.IndexOf(args, "--graph");
        var value = optionIndex >= 0 && optionIndex + 1 < args.Length
            ? args[optionIndex + 1]
            : Environment.GetEnvironmentVariable("TRACELENS_GRAPH_PATH");

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("--graph <path> 또는 TRACELENS_GRAPH_PATH가 필요하다.");
        }

        return Path.GetFullPath(value);
    }
}
