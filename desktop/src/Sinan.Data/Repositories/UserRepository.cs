using Microsoft.Data.Sqlite;
using Sinan.Core.Models;
using Sinan.Core.Utils;

namespace Sinan.Data.Repositories;

/// <summary>
/// 账号仓储：登录验证与用户列表。密码只以 PBKDF2 哈希存取。
/// MVP 只做页面门禁：管理员 / 工程师 / 只读。
/// </summary>
public sealed class UserRepository
{
    private readonly Database _database;

    public UserRepository(Database database) => _database = database;

    public User? Authenticate(string employeeNo, string password)
    {
        if (string.IsNullOrWhiteSpace(employeeNo) || string.IsNullOrEmpty(password)) return null;
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT * FROM users
            WHERE employee_no=$no AND active=1
            """;
        command.Parameters.AddWithValue("$no", employeeNo.Trim());
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;

        var hash = reader.Str("password_hash");
        if (!PasswordHasher.Verify(password, hash)) return null;

        var user = Map(reader);
        reader.Close();
        UpdateLastLogin(connection, user.Id);
        return user;
    }

    public User? GetById(string id)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM users WHERE id=$id";
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public List<User> ListActive()
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM users WHERE active=1 ORDER BY role, name";
        using var reader = command.ExecuteReader();
        var list = new List<User>();
        while (reader.Read()) list.Add(Map(reader));
        return list;
    }

    private static void UpdateLastLogin(SqliteConnection connection, string userId)
    {
        // users 表没有 last_login 字段；登录成功只更新 updated_at 会污染业务语义，
        // 这里暂不落库。将来如需登录审计，写入 activity_log 的 entity='auth' 即可。
        _ = connection;
        _ = userId;
    }

    private static User Map(SqliteDataReader reader) => new()
    {
        Id = reader.Str("id"),
        EmployeeNo = reader.Str("employee_no"),
        Name = reader.Str("name"),
        Role = RoleNames.Parse(reader.Str("role")),
        Dept = reader.Str("dept"),
        Phone = reader.Str("phone"),
        Active = reader.Bool("active"),
        CreatedAt = reader.Str("created_at"),
        UpdatedAt = reader.Str("updated_at")
    };
}
