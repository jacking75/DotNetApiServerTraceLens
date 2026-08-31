namespace FixtureDataApp.Support;

/// <summary>SQL 동사로 시작하는 평범한 문자열. 쿼리로 오해하면 안 된다(오탐 검증용).</summary>
public static class TextUtil
{
    public static bool IsDeleteCommand(string value) => value.StartsWith("Delete", StringComparison.Ordinal);

    public static bool IsSelectCommand(string value) => value.StartsWith("Select", StringComparison.Ordinal);
}
