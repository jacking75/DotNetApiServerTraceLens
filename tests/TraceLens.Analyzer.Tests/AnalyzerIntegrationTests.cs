using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using TraceLens.Analyzer.Models;
using Xunit;

namespace TraceLens.Analyzer.Tests;

public sealed class AnalyzerIntegrationTests
{
    [Fact]
    public async Task AnalyzeAsync_BuildsExpectedControllerServiceRepositoryGraph()
    {
        var logs = new ConcurrentQueue<string>();
        await using var provider = CreateProvider(logs);

        var result = await provider.GetRequiredService<ITraceLensAnalyzer>().AnalyzeAsync(GetFixtureSolutionPath());

        Assert.Equal(5, result.Graph.EntryPoints.Count);
        Assert.True(
            result.Graph.Nodes.Count == 37,
            $"예상 노드 수는 37개지만 {result.Graph.Nodes.Count}개다.{Environment.NewLine}{string.Join(Environment.NewLine, logs)}");
        Assert.Equal(21, result.Graph.Edges.Count);
        Assert.Equal(5, result.Graph.Nodes.Count(node => node.Kind == NodeKind.Controller));
        Assert.Contains(result.Graph.Nodes, node => node.Kind == NodeKind.Service);
        Assert.Contains(result.Graph.Nodes, node => node.Kind == NodeKind.Repository);
        Assert.All(result.Graph.FileHashes.Values, hash => Assert.StartsWith("sha256:", hash));
        Assert.True(File.Exists(result.OutputPath));
    }

    [Fact]
    public async Task AnalyzeAsync_ResolvesInterfaceCallsByRegistrationAndSingleImplementation()
    {
        var logs = new ConcurrentQueue<string>();
        await using var provider = CreateProvider(logs);

        var result = await provider.GetRequiredService<ITraceLensAnalyzer>().AnalyzeAsync(GetFixtureSolutionPath());

        var callee = (string nodeId) => result.Graph.Edges.Any(edge =>
            edge.From == "FixtureApp.Controllers.OrdersController.Create(int)" && edge.To == nodeId);

        // Program.cs가 아닌 파일에서 AddSingleton으로 등록한 구현체로 이어져야 한다.
        Assert.True(
            callee("FixtureApp.Services.NotificationService.NotifyAsync(int)"),
            string.Join(Environment.NewLine, logs));
        Assert.False(callee("FixtureApp.Services.NullNotificationService.NotifyAsync(int)"));

        // DI 등록이 없어도 구현체가 하나뿐이면 그 구현체로 이어져야 한다.
        Assert.True(
            callee("FixtureApp.Services.AuditService.RecordAsync(int)"),
            string.Join(Environment.NewLine, logs));
    }

    [Fact]
    public async Task AnalyzeAsync_LinksObjectCreationAndScansConstructorAndPropertyBodies()
    {
        var logs = new ConcurrentQueue<string>();
        await using var provider = CreateProvider(logs);

        var result = await provider.GetRequiredService<ITraceLensAnalyzer>().AnalyzeAsync(GetFixtureSolutionPath());
        var hasEdge = (string from, string to) => result.Graph.Edges.Any(edge => edge.From == from && edge.To == to);
        var draftConstructorId = "FixtureApp.Models.OrderDraft..ctor()";

        // new로 만든 DTO는 기본 생성자만 있어도 노드가 생기고, 그 DTO 파일을 가리켜야 한다.
        var draftConstructor = Assert.Single(result.Graph.Nodes, node => node.Id == draftConstructorId);
        Assert.Equal("new OrderDraft", draftConstructor.DisplayName);
        Assert.Equal("FixtureApp/Models/OrderDraft.cs", draftConstructor.File);
        Assert.Contains(draftConstructorId, Assert.Single(
            result.Graph.Classes,
            item => item.Id == "FixtureApp.Models.OrderDraft").MethodIds);
        Assert.True(
            hasEdge("FixtureApp.Services.OrderService.CreateAsync(int)", draftConstructorId),
            string.Join(Environment.NewLine, logs));

        // 생성자 본문과 프로퍼티 접근자 본문 안의 호출도 잡혀야 한다.
        Assert.True(hasEdge(
            "FixtureApp.Repositories.OrderRepository..ctor()",
            "FixtureApp.Repositories.OrderRepository.CreateDraft()"));
        Assert.True(hasEdge(
            "FixtureApp.Repositories.OrderRepository.get_PendingCount()",
            "FixtureApp.Repositories.OrderRepository.CountPending()"));

        // 대상 타입을 적지 않는 new()도 같은 생성자로 이어져야 한다.
        Assert.True(hasEdge("FixtureApp.Repositories.OrderRepository.CreateDraft()", draftConstructorId));
    }

    [Fact]
    public async Task AnalyzeAsync_WithObjectCreationDisabled_KeepsOnlyMethodCalls()
    {
        var logs = new ConcurrentQueue<string>();
        await using var provider = CreateProvider(logs);
        var configPath = Path.Combine(Path.GetDirectoryName(GetFixtureSolutionPath())!, "tracelens.config.json");
        await File.WriteAllTextAsync(configPath, """{ "includeObjectCreation": false }""");

        try
        {
            var result = await provider.GetRequiredService<ITraceLensAnalyzer>().AnalyzeAsync(GetFixtureSolutionPath());

            // new로만 등장하던 DTO 생성자는 사라지고, 직접 선언한 생성자 노드는 그대로 남는다.
            Assert.DoesNotContain(result.Graph.Nodes, node => node.Id == "FixtureApp.Models.OrderDraft..ctor()");
            Assert.DoesNotContain(result.Graph.Edges, edge => edge.To.EndsWith("OrderDraft..ctor()", StringComparison.Ordinal));
            Assert.Contains(result.Graph.Nodes, node => node.Id == "FixtureApp.Repositories.OrderRepository..ctor()");
            Assert.Contains(
                result.Graph.Edges,
                edge => edge.From == "FixtureApp.Services.OrderService.CreateAsync(int)" &&
                        edge.To == "FixtureApp.Repositories.OrderRepository.SaveAsync(int)");
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [Fact]
    public async Task AnalyzeAsync_ListsEveryClassIncludingOnesNoEntryPointReaches()
    {
        var logs = new ConcurrentQueue<string>();
        await using var provider = CreateProvider(logs);

        var result = await provider.GetRequiredService<ITraceLensAnalyzer>().AnalyzeAsync(GetFixtureSolutionPath());

        // 컨트롤러·서비스·리포지터리·DI 등록 모양 픽스처(RegistrationShapes)까지 포함한 수다.
        Assert.Equal(43, result.Graph.Classes.Count);
        var job = Assert.Single(result.Graph.Classes, item => item.Name == "CleanupJob");
        Assert.Equal("FixtureApp.Jobs", job.Namespace);
        Assert.Equal("FixtureApp/Jobs/CleanupJob.cs", job.File);
        Assert.Equal(["FixtureApp.Jobs.CleanupJob.RunAsync(int)"], job.MethodIds);
        // 스키마 v4부터 인터페이스도 목록에 들어간다(노드는 여전히 본문이 있는 멤버만이다).
        Assert.Equal(10, result.Graph.Classes.Count(item => item.TypeKind == ClassTypeKind.Interface));
        Assert.DoesNotContain(result.Graph.Nodes, node => node.ClassId == "FixtureApp.Services.IOrderService");

        // 진입점에서 도달하지 못하는 클래스도 호출 엣지를 가진다.
        Assert.Contains(
            result.Graph.Edges,
            edge => edge.From == "FixtureApp.Jobs.CleanupJob.RunAsync(int)" &&
                    edge.To == "FixtureApp.Repositories.OrderRepository.SaveAsync(int)");

        // 모든 노드는 목록에 있는 클래스에 속한다.
        var classIds = result.Graph.Classes.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        Assert.All(result.Graph.Nodes, node => Assert.Contains(node.ClassId, classIds));
    }

    [Fact]
    public async Task AnalyzeAsync_WithEntryPointScope_KeepsGraphReachableOnlyButStillListsAllClasses()
    {
        var logs = new ConcurrentQueue<string>();
        await using var provider = CreateProvider(logs);
        var configPath = Path.Combine(Path.GetDirectoryName(GetFixtureSolutionPath())!, "tracelens.config.json");
        await File.WriteAllTextAsync(configPath, """{ "analysisScope": "entryPoints" }""");

        try
        {
            var result = await provider.GetRequiredService<ITraceLensAnalyzer>().AnalyzeAsync(GetFixtureSolutionPath());

            Assert.DoesNotContain(result.Graph.Nodes, node => node.DisplayName == "CleanupJob.RunAsync");
            Assert.Equal(12, result.Graph.Nodes.Count);
            Assert.Equal(43, result.Graph.Classes.Count);
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [Fact]
    public async Task AnalyzeAsync_CollectsTypeMetadataInheritanceAndMembers()
    {
        var logs = new ConcurrentQueue<string>();
        await using var provider = CreateProvider(logs);

        var result = await provider.GetRequiredService<ITraceLensAnalyzer>().AnalyzeAsync(GetFixtureSolutionPath());
        var byId = result.Graph.Classes.ToDictionary(item => item.Id, StringComparer.Ordinal);

        // 인터페이스 선언과 그 구현 — interfaceIds는 직접 구현만 담는다.
        var repositoryInterface = byId["FixtureApp.Repositories.IOrderRepository"];
        Assert.Equal(ClassTypeKind.Interface, repositoryInterface.TypeKind);
        Assert.False(repositoryInterface.IsAbstract);
        Assert.Equal(
            ["FixtureApp.Repositories.IOrderRepository"],
            byId["FixtureApp.Repositories.OrderRepository"].InterfaceIds);

        // 추상 기반 클래스와 파생 둘.
        var entityBase = byId["FixtureApp.Domain.EntityBase"];
        Assert.True(entityBase.IsAbstract);
        Assert.Equal(ClassTypeKind.Class, entityBase.TypeKind);
        Assert.Equal(entityBase.Id, byId["FixtureApp.Domain.Order"].BaseTypeId);
        Assert.Equal(entityBase.Id, byId["FixtureApp.Domain.Payment"].BaseTypeId);
        Assert.Equal("EntityBase", byId["FixtureApp.Domain.Order"].BaseTypeName);

        // 솔루션 밖 상위 타입은 id 없이 이름만 남는다.
        var controller = byId["FixtureApp.Controllers.OrdersController"];
        Assert.Null(controller.BaseTypeId);
        Assert.Equal("ControllerBase", controller.BaseTypeName);

        // 필드로 인터페이스를 주입받으면 멤버 typeIds가 그 인터페이스를 가리킨다.
        var injected = Assert.Single(
            byId["FixtureApp.Services.ReportService"].Members,
            member => member.MemberKind == MemberKind.Field);
        Assert.Equal("repository", injected.Name);
        Assert.Equal(MemberAccessibility.Private, injected.Accessibility);
        Assert.Equal("IOrderRepository", injected.Type);
        Assert.Equal(["FixtureApp.Repositories.IOrderRepository"], injected.TypeIds);
        Assert.Null(injected.NodeId);

        // 제네릭 인자 안의 솔루션 타입도 재귀로 뽑는다.
        var find = Assert.Single(
            byId["FixtureApp.Services.ReportService"].Members,
            member => member.Name == "FindAsync");
        Assert.Equal(["FixtureApp.Domain.Order"], find.TypeIds);
        Assert.Equal("FixtureApp.Services.ReportService.FindAsync(int)", find.NodeId);

        // 자동 프로퍼티만 있는 record는 members에는 뜨고 nodes에는 안 뜬다.
        var request = byId["FixtureApp.Contracts.CreateOrderRequest"];
        Assert.Equal(ClassTypeKind.Record, request.TypeKind);
        Assert.Equal(
            ["CustomerId", "Note"],
            request.Members.Where(member => member.MemberKind == MemberKind.Property).Select(member => member.Name));
        Assert.All(request.Members, member => Assert.Null(member.NodeId));
        Assert.DoesNotContain(result.Graph.Nodes, node => node.ClassId == request.Id);

        // 인터페이스 메서드는 members에만 있고 노드가 되지 않는다.
        var interfaceMethod = Assert.Single(repositoryInterface.Members);
        Assert.Equal("SaveAsync(int)", interfaceMethod.Signature);
        Assert.Null(interfaceMethod.NodeId);

        // 노드에도 접근제한자·반환타입·파라미터 타입이 붙는다.
        var save = Assert.Single(
            result.Graph.Nodes,
            node => node.Id == "FixtureApp.Repositories.OrderRepository.SaveAsync(int)");
        Assert.Equal(MemberAccessibility.Public, save.Accessibility);
        Assert.Equal("Task", save.ReturnType);
        Assert.Equal(["int"], save.ParameterTypes);

        var privateMethod = Assert.Single(
            result.Graph.Nodes,
            node => node.Id == "FixtureApp.Repositories.OrderRepository.CountPending()");
        Assert.Equal(MemberAccessibility.Private, privateMethod.Accessibility);
        Assert.Empty(privateMethod.ParameterTypes);
    }

    [Fact]
    public async Task AnalyzeAsync_WithMembersDisabled_KeepsClassesButEmptiesMembers()
    {
        var logs = new ConcurrentQueue<string>();
        await using var provider = CreateProvider(logs);
        var configPath = Path.Combine(Path.GetDirectoryName(GetFixtureSolutionPath())!, "tracelens.config.json");
        await File.WriteAllTextAsync(configPath, """{ "includeMembers": false }""");

        try
        {
            var result = await provider.GetRequiredService<ITraceLensAnalyzer>().AnalyzeAsync(GetFixtureSolutionPath());

            Assert.Equal(43, result.Graph.Classes.Count);
            Assert.All(result.Graph.Classes, item => Assert.Empty(item.Members));
            Assert.Equal(0, result.MemberCount);
            // 멤버를 꺼도 상속·구현 정보는 남아 관계도는 그릴 수 있다.
            Assert.Contains(
                result.Graph.Classes,
                item => item.Id == "FixtureApp.Domain.Order" && item.BaseTypeId == "FixtureApp.Domain.EntityBase");
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [Fact]
    public async Task AnalyzeAsync_WithInterfacesDisabled_FallsBackToSchemaV3Behavior()
    {
        var logs = new ConcurrentQueue<string>();
        await using var provider = CreateProvider(logs);
        var configPath = Path.Combine(Path.GetDirectoryName(GetFixtureSolutionPath())!, "tracelens.config.json");
        await File.WriteAllTextAsync(configPath, """{ "includeInterfaces": false }""");

        try
        {
            var result = await provider.GetRequiredService<ITraceLensAnalyzer>().AnalyzeAsync(GetFixtureSolutionPath());

            Assert.Equal(33, result.Graph.Classes.Count);
            Assert.DoesNotContain(result.Graph.Classes, item => item.TypeKind == ClassTypeKind.Interface);
            // 목록에서 빠진 인터페이스는 그릴 대상이 없으므로 구현 관계에서도 빠진다.
            Assert.Empty(Assert.Single(
                result.Graph.Classes,
                item => item.Id == "FixtureApp.Repositories.OrderRepository").InterfaceIds);
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [Fact]
    public async Task AnalyzeAsync_CollectsStartupFlowFromProgramCs()
    {
        var logs = new ConcurrentQueue<string>();
        await using var provider = CreateProvider(logs);

        var result = await provider.GetRequiredService<ITraceLensAnalyzer>().AnalyzeAsync(GetFixtureSolutionPath());
        var flow = result.Graph.StartupFlow;

        Assert.NotNull(flow);
        Assert.Equal("FixtureApp/Program.cs", flow!.ProgramFile);

        // builder.Services: AddControllers(설정) → AddScoped 두 번(등록) → AddFixtureServices(확장 메서드, 하위에 AddSingleton 하나).
        Assert.Equal(4, flow.ServiceSteps.Count);
        Assert.Equal(StartupStepKind.Configuration, flow.ServiceSteps[0].Kind);
        Assert.Equal("AddControllers()", flow.ServiceSteps[0].Label);

        var orderServiceStep = flow.ServiceSteps[1];
        Assert.Equal(StartupStepKind.Registration, orderServiceStep.Kind);
        Assert.Equal("IOrderService", orderServiceStep.ServiceType);
        Assert.Equal("FixtureApp.Services.IOrderService", orderServiceStep.ServiceTypeId);
        Assert.Equal("OrderService", orderServiceStep.ImplementationType);
        Assert.Equal("FixtureApp.Services.OrderService", orderServiceStep.ImplementationTypeId);
        Assert.Equal(StartupLifetime.Scoped, orderServiceStep.Lifetime);
        Assert.Equal("IOrderService → OrderService", orderServiceStep.Label);

        var repositoryStep = flow.ServiceSteps[2];
        Assert.Equal("IOrderRepository", repositoryStep.ServiceType);
        Assert.Equal(StartupLifetime.Scoped, repositoryStep.Lifetime);

        // Program.cs가 아닌 ServiceRegistration.cs 안의 AddSingleton까지 따라 들어가야 한다.
        var extensionStep = flow.ServiceSteps[3];
        Assert.Equal(StartupStepKind.ExtensionCall, extensionStep.Kind);
        Assert.Equal("AddFixtureServices()", extensionStep.Label);
        var nested = Assert.Single(extensionStep.Children);
        Assert.Equal(StartupStepKind.Registration, nested.Kind);
        Assert.Equal("INotificationService", nested.ServiceType);
        Assert.Equal("NotificationService", nested.ImplementationType);
        Assert.Equal(StartupLifetime.Singleton, nested.Lifetime);
        Assert.Contains("ServiceRegistration.cs", nested.File);

        // app: MapControllers() → Run().
        Assert.Equal(2, flow.MiddlewareSteps.Count);
        Assert.Equal("MapControllers()", flow.MiddlewareSteps[0].Label);
        Assert.Equal(StartupStepKind.Middleware, flow.MiddlewareSteps[0].Kind);
        Assert.Equal("Run()", flow.MiddlewareSteps[1].Label);
    }

    private static ServiceProvider CreateProvider(ConcurrentQueue<string> logs)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder
            .SetMinimumLevel(LogLevel.Debug)
            .AddProvider(new CollectingLoggerProvider(logs)));
        services.AddTraceLensAnalyzer();
        return services.BuildServiceProvider();
    }

    private static string GetFixtureSolutionPath() =>
        Path.Combine(GetRepositoryRoot(), "tests", "TraceLens.Analyzer.Tests", "Fixtures", "FixtureApp.sln");

    private static string GetRepositoryRoot([System.Runtime.CompilerServices.CallerFilePath] string sourcePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourcePath)!, "..", ".."));

    private sealed class CollectingLoggerProvider(ConcurrentQueue<string> logs) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CollectingLogger(categoryName, logs);
        public void Dispose() { }
    }

    private sealed class CollectingLogger(string categoryName, ConcurrentQueue<string> logs) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            logs.Enqueue($"[{logLevel}] {categoryName}: {formatter(state, exception)}");
    }
}
