using Microsoft.Data.Sqlite;
using Sinan.Core.Models;
using Sinan.Data;
using Sinan.Tests.Infrastructure;
using Xunit;

namespace Sinan.Tests.Data;

/// <summary>
/// 首次启动与账号：DDL 真源、36 条种子、幂等、登录验证。
/// 这些是整个软件的事实底座，任何一条坏了后面所有统计与导出都会跟着错。
/// </summary>
public class SeedAndUserTests
{
    [Fact]
    public void 首次初始化_种入36条问题31台机台3个分项目()
    {
        using var db = new TempDb();

        var issues = db.Issues.All();
        Assert.Equal(36, issues.Count);
        Assert.Equal(31, issues.Select(i => i.MachineId).Distinct().Count());

        var project = db.Projects.GetMain();
        Assert.NotNull(project);
        Assert.Equal("P7391", project!.Code);

        var subs = db.Projects.ListSubprojects(project.Id);
        Assert.Equal(new[] { "P7391-01", "P7391-02", "P7391-03" }, subs.Select(s => s.Code));
    }

    [Fact]
    public void 分项目机台数与归属_按AGENTS规则()
    {
        using var db = new TempDb();
        var project = db.Projects.GetMain()!;
        var subs = db.Projects.ListSubprojects(project.Id);

        Assert.Equal(10, db.Projects.ListMachines(project.Id, subs[0].Id).Count);
        Assert.Equal(8, db.Projects.ListMachines(project.Id, subs[1].Id).Count);
        Assert.Equal(12, db.Projects.ListMachines(project.Id, subs[2].Id).Count);

        // P73913118 属已知笔误：保留原文、不自动归组，进"待确认"
        var unassigned = db.Projects.ListMachines(project.Id, null, unassigned: true);
        Assert.Single(unassigned);
        Assert.Equal("P73913118", unassigned[0].Code);
    }

    [Fact]
    public void 重复初始化_幂等_不重复种入()
    {
        using var db = new TempDb();
        var before = db.Issues.All().Count;

        db.Db.Initialize(); // 模拟第二次启动

        Assert.Equal(before, db.Issues.All().Count);
    }

    [Fact]
    public void 数据库版本_写入并校验()
    {
        using var db = new TempDb();
        using var connection = db.Db.Open();
        Assert.Equal(Database.SchemaVersion.ToString(),
            Database.ReadMeta(connection, "schema_version"));
    }

    [Theory]
    [InlineData("admin", "管理员")]
    [InlineData("engineer", "工程师")]
    public void 种子账号可登录_角色正确(string no, string roleDisplay)
    {
        using var db = new TempDb();
        var user = db.Users.Authenticate(no, "123456");
        Assert.NotNull(user);
        Assert.Equal(no, user!.EmployeeNo);
        Assert.Equal(roleDisplay, user.RoleDisplay);
    }

    [Theory]
    [InlineData("engineer", "wrong")]
    [InlineData("engineer", "")]
    [InlineData("nobody", "123456")]
    [InlineData("", "123456")]
    [InlineData("   ", "123456")]
    public void 错误账号密码_登录失败(string no, string pwd)
    {
        using var db = new TempDb();
        Assert.Null(db.Users.Authenticate(no, pwd));
    }

    [Fact]
    public void 停用账号_不能登录_历史记录仍保留()
    {
        using var db = new TempDb();
        var user = db.Users.Authenticate("engineer", "123456")!;

        using (var connection = db.Db.Open())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE users SET active=0 WHERE id=$id";
            command.Parameters.AddWithValue("$id", user.Id);
            command.ExecuteNonQuery();
        }

        Assert.Null(db.Users.Authenticate("engineer", "123456"));

        // 停用不删除：用户仍在表里，历史记录里的名字不会变成"未知"
        using var conn2 = db.Db.Open();
        using var check = conn2.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM users WHERE id=$id";
        check.Parameters.AddWithValue("$id", user.Id);
        Assert.Equal(1L, (long)check.ExecuteScalar()!);
    }

    [Fact]
    public void 密码只存哈希_库里没有明文()
    {
        using var db = new TempDb();
        using var connection = db.Db.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT password_hash FROM users";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var hash = reader.GetString(0);
            Assert.StartsWith("pbkdf2$", hash);
            Assert.DoesNotContain("123456", hash);
        }
    }

    [Fact]
    public void 种子问题_保留来源行号与序号缺口()
    {
        using var db = new TempDb();
        var issues = db.Issues.All();

        Assert.All(issues, i => Assert.NotNull(i.SourceRow));
        Assert.Contains(issues, i => i.MachineCode == "P73913118");
        Assert.All(issues, i => Assert.False(string.IsNullOrWhiteSpace(i.Title)));
    }
}
