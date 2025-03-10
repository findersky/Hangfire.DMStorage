# Hangfire.DMStorage

## <a target="_blank" href="README.md">English</a>

Hangfire.DMStorage ��һ����չ�������Ϊ Hangfire ʹ�� Dameng ���ݿ��ṩ֧�֡�


<a href="http://hangfire.io/" target="_blank">Hangfire</a> �� dameng �洢ʵ�� - .NET �ļ����������ӳٺ��ظ�����������������չ�ҿɿ��ĺ�̨��ҵ���г���֧�ֶ����������CPU �� I/O �ܼ��͡���ʱ�����кͶ������е���ҵ��

## ��װ
��װ Hangfire.DMStorage

�� NuGet ������������̨�������������԰�װ Hangfire.DMStorage��

```
Install-Package DMStorage.Hangfire
```

## �÷�

ʹ�����·���֮һ���г�ʼ���� `DMStorage`: 
- ʹ�ô��������ַ������캯����������ʵ������ DMStorage����ͨ�� UseStorage �������䴫�ݸ����á�
```csharp
  GlobalConfiguration.Configuration.UseStorage(
    new DMStorage(connectionString));
```
- ���Խ�һ������ѡ����Ϊ�������ݸ� DMStorage:
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
## ��Hangfire.HttpJob��ʹ��
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

��ѡ������������
- `TransactionIsolationLevel` - ������뼶��Ĭ���Ƕ�ȡ���ύ��
- `QueuePollInterval` - ��ҵ������ѯ�����Ĭ��ֵΪ15�롣
- `JobExpirationCheckInterval` - ��ҵ���ڼ������������ڼ�¼����Ĭ��ֵΪ1Сʱ��
- `CountersAggregateInterval` - �ۺϼ������ļ����Ĭ��Ϊ5���ӡ�
- `PrepareSchemaIfNecessary` - �������Ϊtrue���򴴽����ݿ��Ĭ����true��
- `DashboardJobListLimit` - �Ǳ����ҵ�б����ơ�Ĭ��ֵΪ50000��
- `TransactionTimeout` - ����ʱ��Ĭ��Ϊ1���ӡ�
- `SchemaName` - ģʽ. 

### ������ƴ����ӵ�����

�����ӵ�����ȡ���� Hangfire �Ĺ����߳�����������ͨ������ BackgroundJobServerOptions �е� WorkerCount ����ֵ�����ƹ����߳�����
```csharp
app.UseHangfireServer(
   new BackgroundJobServerOptions
   {
      WorkerCount = 1
   });
```
�鿴����: <a target="_blank" href="http://hangfire.io/features.html#concurrency-level-control">http://hangfire.io/features.html#concurrency-level-control</a>


## ����
��ʹ�� Visual Studio ����ѡ����κ������������������������
