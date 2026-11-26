using System.IO;
using Sinan.Core.Utils;
using Sinan.Data.Repositories;
using Sinan.Tests.Infrastructure;
using Xunit;

namespace Sinan.Tests.Data;

/// <summary>
/// 附件仓储：复制归档、哈希、大小限制、路径越界、演示附件、删除语义。
/// 红线：文件按 attachments/{issueId}/{uuid} 存，DB 只存相对路径；删除问题不删附件。
/// </summary>
public class AttachmentRepositoryTests
{
    private static string TempJpg(string name = "现场照片.jpg")
    {
        var dir = Path.Combine(Path.GetTempPath(), "sinan-att-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        File.WriteAllBytes(path, new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x01, 0x02, 0x03, 0xFF, 0xD9 });
        return path;
    }

    [Fact]
    public void Add_复制文件并登记元数据()
    {
        using var db = new TempDb();
        var source = TempJpg();
        try
        {
            var attachment = db.Attachments.Add("P7391-001", source, db.Engineer.Id);

            Assert.Equal("P7391-001", attachment.IssueId);
            Assert.Equal("现场照片.jpg", attachment.FileName);
            Assert.Equal("image/jpeg", attachment.Mime);
            Assert.Equal("local", attachment.SyncState);
            Assert.False(attachment.IsDemo);
            Assert.True(attachment.Size > 0);
            Assert.Equal(64, attachment.Sha256.Length); // SHA-256 十六进制
            Assert.True(db.Attachments.FileExists(attachment));

            var stored = db.Attachments.AbsolutePath(attachment.StoredPath);
            Assert.True(File.Exists(stored));
            Assert.StartsWith(db.Db.Paths.AttachmentsDir, stored, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("/P7391-001/", "/" + attachment.StoredPath.Replace('\\', '/'));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(source)!, recursive: true);
        }
    }

    [Fact]
    public void Add_文件不存在_抛异常()
    {
        using var db = new TempDb();
        Assert.Throws<FileNotFoundException>(() =>
            db.Attachments.Add("P7391-001", Path.Combine(Path.GetTempPath(), "not-exist-xyz.jpg"), "u"));
    }

    [Fact]
    public void Add_超过20MB_抛异常()
    {
        using var db = new TempDb();
        var source = TempJpg("big.jpg");
        try
        {
            using (var stream = new FileStream(source, FileMode.Open, FileAccess.Write))
                stream.SetLength(AttachmentRepository.MaxFileSize + 1);

            var ex = Assert.Throws<InvalidOperationException>(() =>
                db.Attachments.Add("P7391-001", source, "u"));
            Assert.Contains("20 MB", ex.Message);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(source)!, recursive: true);
        }
    }

    [Fact]
    public void Delete_删除记录与文件()
    {
        using var db = new TempDb();
        var source = TempJpg();
        try
        {
            var attachment = db.Attachments.Add("P7391-001", source, "u");
            var stored = db.Attachments.AbsolutePath(attachment.StoredPath);

            db.Attachments.Delete(attachment.Id);

            Assert.Null(db.Attachments.Get(attachment.Id));
            Assert.False(File.Exists(stored));
            Assert.Empty(db.Attachments.List("P7391-001"));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(source)!, recursive: true);
        }
    }

    [Theory]
    [InlineData("../../evil.jpg")]
    [InlineData("..\\..\\evil.jpg")]
    [InlineData("P7391-001/../../evil.jpg")]
    public void AbsolutePath_目录穿越_拒绝(string storedPath)
    {
        using var db = new TempDb();
        Assert.Throws<InvalidOperationException>(() => db.Attachments.AbsolutePath(storedPath));
    }

    [Fact]
    public void EnsureDemoAttachments_12张6条_幂等()
    {
        using var db = new TempDb();

        var added = db.Attachments.EnsureDemoAttachments();
        Assert.Equal(12, added);

        var issues = db.Issues.All();
        Assert.Equal(12, issues.Sum(i => i.PhotoCount));
        Assert.Equal(6, issues.Count(i => i.PhotoCount > 0));

        // 再启动一次不应重复种入
        Assert.Equal(0, db.Attachments.EnsureDemoAttachments());
        Assert.Equal(12, db.Issues.All().Sum(i => i.PhotoCount));
    }

    [Fact]
    public void 演示附件不伪造文件_FileExists为假()
    {
        using var db = new TempDb();
        db.Attachments.EnsureDemoAttachments();

        var demo = db.Attachments.List("P7391-001").FirstOrDefault()
                   ?? db.Issues.All()
                       .SelectMany(i => db.Attachments.List(i.Id))
                       .First();
        Assert.True(demo.IsDemo);
        Assert.False(db.Attachments.FileExists(demo)); // 占位元数据，不伪造现场照片
    }
}
