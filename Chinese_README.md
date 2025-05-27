# Hangfire.DMStorage


##  [English](README.md)

Hangfire.DMStorage 是一个扩展组件，它为 Hangfire 使用 Dameng 数据库提供支持。


<a href="http://hangfire.io/" target="_blank">Hangfire</a> 的 dameng 存储实现 - .NET 的即发即弃、延迟和重复任务运行器。可扩展且可靠的后台作业运行程序。支持多个服务器、CPU 和 I/O 密集型、长时间运行和短期运行的作业。

## 安装
安装 Hangfire.DMStorage

在 NuGet 包管理器控制台运行以下命令以安装 Hangfire.DMStorage：

```
Install-Package DMStorage.Hangfire
```

## 用法

使用以下方法之一进行初始化： `DMStorage`: 
- 使用带有连接字符串构造函数参数的新实例创建 DMStorage，并通过 UseStorage 方法将其传递给配置。
```csharp
  GlobalConfiguration.Configuration.UseStorage(
    new DMStorage(connectionString));
```
- 可以将一个或多个选项作为参数传递给 DMStorage:
```csharp
GlobalConfiguration.Configuration.UseStorage(
    new DMStorage(
        connectionString, 
        new DMStorageOptions
        {
            TransactionIsolationLevel =IsolationLevel.ReadCommitted,
            QueuePollInterval = TimeSpan.FromSeconds(15),
            JobExpirationCheckInterval = TimeSpan.FromHours(1),
            CountersAggregateInterval = TimeSpan.FromMinutes(5),
            PrepareSchemaIfNecessary = true,
            DashboardJobListLimit = 50000,
            TransactionTimeout = TimeSpan.FromMinutes(1),
            SchemaName= "SYSDBA"
        }));
```
## 在Hangfire.HttpJob中使用
```csharp
     context.Services.AddHangfire(x => x.UseStorage(new DMStorage(connectionString, new DMStorageOptions()
            {
                TransactionIsolationLevel = System.Data.IsolationLevel.ReadCommitted,
                QueuePollInterval = TimeSpan.FromSeconds(15),
                JobExpirationCheckInterval = TimeSpan.FromHours(1),
                CountersAggregateInterval = TimeSpan.FromMinutes(5),
                PrepareSchemaIfNecessary = true,
                DashboardJobListLimit = 50000,
                TransactionTimeout = TimeSpan.FromMinutes(1),
                SchemaName = "SYSDBA"
            }))
            .UseConsole()
            .UseHangfireHttpJob());
```

可选参数的描述：
- `TransactionIsolationLevel` - 事务隔离级别。默认是读取已提交。
- `QueuePollInterval` - 作业队列轮询间隔。默认值为15秒。
- `JobExpirationCheckInterval` - 作业到期检查间隔（管理过期记录）。默认值为1小时。
- `CountersAggregateInterval` - 聚合计数器的间隔。默认为5分钟。
- `PrepareSchemaIfNecessary` - 如果设置为true，则创建数据库表。默认是true。
- `DashboardJobListLimit` - 仪表板作业列表限制。默认值为50000。
- `TransactionTimeout` - 事务超时。默认为1分钟。
- `SchemaName` - 模式. 

### 如何限制打开连接的数量

打开连接的数量取决于 Hangfire 的工作线程数。您可以通过设置 BackgroundJobServerOptions 中的 WorkerCount 属性值来限制工作线程数。
```csharp
app.UseHangfireServer(
   new BackgroundJobServerOptions
   {
      WorkerCount = 1
   });
```
查看更多: <a target="_blank" href="http://hangfire.io/features.html#concurrency-level-control">http://hangfire.io/features.html#concurrency-level-control</a>


## 构建
请使用 Visual Studio 或您选择的任何其他工具来构建解决方案。
