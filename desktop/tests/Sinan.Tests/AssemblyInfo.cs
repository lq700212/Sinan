using Xunit;

// 关闭跨测试集合的并行执行。
//
// 原因不是"跑得慢"，而是正确性：TempDb.Dispose 里必须调用
// SqliteConnection.ClearAllPools() 才能删掉 WAL 文件（Windows 上文件被连接池占用）。
// 这是**进程级**操作，而 xUnit 默认让不同测试类并行跑——A 组测试清理连接池时，
// 正好会把 B 组正在使用的连接一起关掉，表现为随机一条测试失败、且无法稳定复现。
//
// 每个用例本来就是独立临时库，串行的代价只是总时长（约 7s → 20s），
// 换来的是"失败了就是真的坏了"，而不是先怀疑测试自己抽风。
[assembly: CollectionBehavior(DisableTestParallelization = true)]
