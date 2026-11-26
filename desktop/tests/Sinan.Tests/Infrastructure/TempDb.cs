using System.IO;
using Microsoft.Data.Sqlite;
using Sinan.Core.Models;
using Sinan.Data;
using Sinan.Data.Excel;
using Sinan.Data.Repositories;
using Sinan.Data.Sync;

namespace Sinan.Tests.Infrastructure;

/// <summary>
/// 每个测试一个独立临时库：真实执行 shared/db/schema.sql + 真实 36 条种子，
/// 不用内存 SQLite（SharedCache 与 WAL 行为不同，容易测出假绿）。
/// 测完删目录；SQLite 连接池必须先清，否则 Windows 上文件被占用删不掉。
/// </summary>
public sealed class TempDb : IDisposable
{
    public DataPaths Paths { get; }
    public Database Db { get; }
    public UserRepository Users { get; }
    public IssueRepository Issues { get; }
    public ProjectRepository Projects { get; }
    public AttachmentRepository Attachments { get; }
    public ExcelImportService Import { get; }
    public ExcelExportService Export { get; }
    public SyncRepository Sync { get; }

    public TempDb()
    {
        Paths = new DataPaths(Path.Combine(
            Path.GetTempPath(), "sinan-tests", Guid.NewGuid().ToString("N")));
        Db = new Database(Paths);
        Db.Initialize();

        Users = new UserRepository(Db);
        Issues = new IssueRepository(Db);
        Projects = new ProjectRepository(Db, Issues);
        Attachments = new AttachmentRepository(Db);
        Import = new ExcelImportService(Db, Issues, Projects);
        Export = new ExcelExportService(Db, Issues, Projects, Attachments);
        Sync = new SyncRepository(Db, Issues, Projects, Users);
    }

    /// <summary>种子管理员（admin / 123456）；每次取新实例，避免测试共享可变对象。</summary>
    public User Admin => Users.Authenticate("admin", "123456")
        ?? throw new InvalidOperationException("种子管理员登录失败");

    /// <summary>种子工程师（engineer / 123456）。</summary>
    public User Engineer => Users.Authenticate("engineer", "123456")
        ?? throw new InvalidOperationException("种子工程师登录失败");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(Paths.Root)) Directory.Delete(Paths.Root, recursive: true);
        }
        catch
        {
            // 临时目录清理失败不影响测试结论；系统临时目录会被回收。
        }
    }
}
