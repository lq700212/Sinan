using Microsoft.Data.Sqlite;

namespace Sinan.Data;

/// <summary>
/// 运行时数据路径。所有可变数据都放 %LOCALAPPDATA%\Sinan，仓库里不落库：
///   data\sinan.db / attachments\ / logs\ / archive\
/// 环境变量 SINAN_DATA_DIR 可整体改根目录（现场把数据放 D 盘或共享盘时用）。
/// </summary>
public sealed class DataPaths
{
    public const string RootEnvVar = "SINAN_DATA_DIR";

    public string Root { get; }
    public string DataDir { get; }
    public string DbPath { get; }
    public string AttachmentsDir { get; }
    public string LogsDir { get; }
    public string ArchiveDir { get; }

    public DataPaths(string root)
    {
        Root = Path.GetFullPath(root);
        DataDir = Path.Combine(Root, "data");
        DbPath = Path.Combine(DataDir, "sinan.db");
        AttachmentsDir = Path.Combine(Root, "attachments");
        LogsDir = Path.Combine(Root, "logs");
        ArchiveDir = Path.Combine(Root, "archive");
    }

    public static DataPaths CreateDefault()
    {
        var env = Environment.GetEnvironmentVariable(RootEnvVar);
        var root = string.IsNullOrWhiteSpace(env)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sinan")
            : env.Trim();
        return new DataPaths(root);
    }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(AttachmentsDir);
        Directory.CreateDirectory(LogsDir);
        Directory.CreateDirectory(ArchiveDir);
    }
}

/// <summary>
/// SQLite 主库入口：建目录、执行 shared/db/schema.sql（嵌入资源）、写 schema_version、首启种子。
/// 连接采用短连接 + WAL；业务仓储每次自行 Open，不做跨线程共享连接。
/// </summary>
public sealed class Database
{
    public const int SchemaVersion = 1;

    private readonly string _connectionString;

    public DataPaths Paths { get; }

    public Database(DataPaths? paths = null)
    {
        Paths = paths ?? DataPaths.CreateDefault();
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Paths.DbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
            Pooling = true
        }.ToString();
    }

    public SqliteConnection Open()
    {
        Paths.EnsureDirectories();
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA synchronous=NORMAL;
            PRAGMA busy_timeout=5000;
            """;
        pragma.ExecuteNonQuery();
        return connection;
    }

    /// <summary>
    /// 首次启动：建表 + 写元数据 + 种入 57 条样例。已初始化则只校验版本。
    /// 版本不匹配时抛异常而不是静默改库，避免现场数据被错误结构破坏。
    /// </summary>
    public void Initialize()
    {
        Paths.EnsureDirectories();
        using var connection = Open();
        ExecuteSchema(connection);

        var version = ReadMeta(connection, "schema_version");
        if (version == null)
        {
            WriteMeta(connection, "schema_version", SchemaVersion.ToString());
        }
        else if (!int.TryParse(version, out var parsed) || parsed > SchemaVersion)
        {
            throw new InvalidOperationException(
                $"数据库结构版本 {version} 高于当前程序支持的 {SchemaVersion}，请升级司南程序。");
        }

        SeedBootstrapper.Seed(connection);
    }

    private static void ExecuteSchema(SqliteConnection connection)
    {
        var assembly = typeof(Database).Assembly;
        using var stream = assembly.GetManifestResourceStream("Sinan.Data.Schema.sql")
            ?? throw new InvalidOperationException("缺少嵌入资源 Sinan.Data.Schema.sql");
        using var reader = new StreamReader(stream);
        var sql = reader.ReadToEnd();

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public static string? ReadMeta(SqliteConnection connection, string key)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM app_meta WHERE key=$k";
        command.Parameters.AddWithValue("$k", key);
        return command.ExecuteScalar() as string;
    }

    public static void WriteMeta(SqliteConnection connection, string key, string value)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO app_meta(key, value) VALUES($k, $v)
            ON CONFLICT(key) DO UPDATE SET value=excluded.value
            """;
        command.Parameters.AddWithValue("$k", key);
        command.Parameters.AddWithValue("$v", value);
        command.ExecuteNonQuery();
    }
}
