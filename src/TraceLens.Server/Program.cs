using TraceLens.Server;

// 호스트 구성은 ServerHost에 있다(CLI의 `tracelens serve`가 같은 구성을 쓴다).
// `run.ps1 -Port`가 넘기는 `--port`를 여기서 읽는다. 루프백 바인딩은 그대로다(설계 제약 4).
var port = ReadPort(args) ?? ServerHost.DefaultPort;
await ServerHost.Build(args, new ServerOptions { Port = port }).RunAsync();

static int? ReadPort(string[] args)
{
    for (var index = 0; index < args.Length - 1; index++)
    {
        if (string.Equals(args[index], "--port", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(args[index + 1], out var value) &&
            value is > 0 and <= 65535)
        {
            return value;
        }
    }

    return null;
}

public partial class Program;
