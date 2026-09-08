using TraceLens.Server;

// 호스트 구성은 ServerHost에 있다(CLI의 `tracelens serve`가 같은 구성을 쓴다).
await ServerHost.Build(args).RunAsync();

public partial class Program;
