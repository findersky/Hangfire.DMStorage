# Hangfire.DMStorage

Hangfire.DMStorage is an extension component that provides support for Hangfire to use the Dameng database.



Dameng storage implementation of <a href="http://hangfire.io/" target="_blank">Hangfire</a>- fire-and-forget, delayed and recurring tasks runner for .NET. Scalable and reliable background job runner. Supports multiple servers, CPU and I/O intensive, long-running and short-running jobs.

## Installation
Install Hangfire.DMStorage

Run the following command in the NuGet Package Manager console to install Hangfire.DMStorage:

```
Install-Package DMStorage.Hangfire
```

## Usage

Use one the following ways to initialize `DMStorage`: 
- Create new instance of `DMStorage` with connection string constructor parameter and pass it to `Configuration` with `UseStorage` method:
```csharp
  GlobalConfiguration.Configuration.UseStorage(
    new DMStorage(connectionString));
```
- Alternatively one or more options can be passed as a parameter to `DMStorage`:
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
## Use in Hangfire.HttpJob
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

Description of optional parameters:
- `TransactionIsolationLevel` - transaction isolation level. Default is read committed. Didn't test with other options!
- `QueuePollInterval` - job queue polling interval. Default is 15 seconds.
- `JobExpirationCheckInterval` - job expiration check interval (manages expired records). Default is 1 hour.
- `CountersAggregateInterval` - interval to aggregate counter. Default is 5 minutes.
- `PrepareSchemaIfNecessary` - if set to `true`, it creates database tables. Default is `true`.
- `DashboardJobListLimit` - dashboard job list limit. Default is 50000.
- `TransactionTimeout` - transaction timeout. Default is 1 minute.
- `SchemaName` - schema name. 

### How to limit number of open connections

Number of opened connections depends on Hangfire worker count. You can limit worker count by setting `WorkerCount` property value in `BackgroundJobServerOptions`:
```csharp
app.UseHangfireServer(
   new BackgroundJobServerOptions
   {
      WorkerCount = 1
   });
```
More info: <a target="_blank" href="http://hangfire.io/features.html#concurrency-level-control">http://hangfire.io/features.html#concurrency-level-control</a>


## Build
Please use Visual Studio or any other tool of your choice to build the solution.
