namespace FixtureApp.Services;

/// <summary>재정의가 하나뿐이다. 기반 선언으로 가는 엣지와 함께 재정의로 가는 추정 엣지도 생겨야 한다.</summary>
public abstract class ReportFormatterBase
{
    public virtual string Format(int id) => id.ToString();
}

public sealed class JsonReportFormatter : ReportFormatterBase
{
    public override string Format(int id) => $"{{\"id\":{id}}}";
}

/// <summary>재정의가 둘이다. 어느 쪽이 실행될지 확정할 수 없으므로 엣지를 만들지 않는다.</summary>
public abstract class ExportBase
{
    public abstract string Export(int id);
}

public sealed class CsvExport : ExportBase
{
    public override string Export(int id) => $"{id}";
}

public sealed class XmlExport : ExportBase
{
    public override string Export(int id) => $"<id>{id}</id>";
}

/// <summary>기반 타입으로 호출하는 자리. 실행 시 실제 타입이 무엇인지는 이 자리에서 알 수 없다.</summary>
public sealed class DispatchProbe(ReportFormatterBase formatter, ExportBase export)
{
    public string Run(int id) => formatter.Format(id) + export.Export(id);
}
