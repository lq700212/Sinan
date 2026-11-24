using Microsoft.Data.Sqlite;

namespace Sinan.Data.Repositories;

/// <summary>
/// Microsoft.Data.Sqlite 的读取小工具。库字段大量可空字符串，统一把 NULL 读成 ""，
/// 避免每个映射方法重复判 DBNull。
/// </summary>
internal static class SqlReaderExtensions
{
    public static string Str(this SqliteDataReader reader, string column) =>
        reader.IsDBNull(reader.GetOrdinal(column))
            ? ""
            : Convert.ToString(reader.GetValue(reader.GetOrdinal(column))) ?? "";

    public static string? StrOrNull(this SqliteDataReader reader, string column) =>
        reader.IsDBNull(reader.GetOrdinal(column))
            ? null
            : Convert.ToString(reader.GetValue(reader.GetOrdinal(column)));

    public static int Int(this SqliteDataReader reader, string column) =>
        reader.IsDBNull(reader.GetOrdinal(column))
            ? 0
            : Convert.ToInt32(reader.GetValue(reader.GetOrdinal(column)));

    public static int? IntOrNull(this SqliteDataReader reader, string column) =>
        reader.IsDBNull(reader.GetOrdinal(column))
            ? null
            : Convert.ToInt32(reader.GetValue(reader.GetOrdinal(column)));

    public static long Long(this SqliteDataReader reader, string column) =>
        reader.IsDBNull(reader.GetOrdinal(column))
            ? 0
            : Convert.ToInt64(reader.GetValue(reader.GetOrdinal(column)));

    public static bool Bool(this SqliteDataReader reader, string column) =>
        !reader.IsDBNull(reader.GetOrdinal(column)) &&
        Convert.ToInt32(reader.GetValue(reader.GetOrdinal(column))) != 0;

    public static DateOnly? DateOnlyOrNull(this SqliteDataReader reader, string column)
    {
        var raw = reader.StrOrNull(column);
        return DateOnly.TryParse(raw, out var value) ? value : null;
    }
}

internal static class SqlCommandExtensions
{
    public static void Add(this SqliteParameterCollection parameters, string name, object? value)
    {
        parameters.AddWithValue(name, value ?? DBNull.Value);
    }

    public static void AddText(this SqliteParameterCollection parameters, string name, string? value)
    {
        parameters.AddWithValue(name, string.IsNullOrEmpty(value) ? DBNull.Value : value);
    }
}
