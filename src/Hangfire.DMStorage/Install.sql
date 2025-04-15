CREATE SEQUENCE SEQUENCED  START WITH 1 INCREMENT BY 1 MAXVALUE 9999999999999999999999999999 MINVALUE 1 NOCYCLE CACHE 20;
CREATE SEQUENCE JOB_ID_SEQ START WITH 1 INCREMENT BY 1 MAXVALUE 9999999999999999999999999999 MINVALUE 1 NOCYCLE CACHE 20;

-- ----------------------------
-- Table structure for `Job`
-- ----------------------------
CREATE TABLE [SchemaName]"Job" (
    "Id"              INT,
    "StateId"        INT,
    "StateName"      VARCHAR(20),
    "InvocationData" CLOB,
    "Arguments"       CLOB,
    "CreatedAt"      TIMESTAMP,
    "ExpireAt"       TIMESTAMP
);

ALTER TABLE [SchemaName]"Job" ADD PRIMARY KEY ("Id");

-- ----------------------------
-- Table structure for `Counter`
-- ----------------------------
CREATE TABLE [SchemaName]"Counter" (
    "Id"        INT,
    "Key"       VARCHAR(255),
    "Value"     INT,
    "ExpireAt"  TIMESTAMP
);

ALTER TABLE [SchemaName]"Counter" ADD PRIMARY KEY ("Id");

-- ----------------------------
-- Table structure for `AggregatedCounter`
-- ----------------------------
CREATE TABLE [SchemaName]"AggregatedCounter" (
    "Id"        INT,
    "Key"       VARCHAR(255),
    "Value"     INT,
    "ExpireAt" TIMESTAMP
);

ALTER TABLE [SchemaName]"AggregatedCounter" ADD PRIMARY KEY ("Id");
ALTER TABLE [SchemaName]"AggregatedCounter" ADD UNIQUE ("Key","Value");

-- ----------------------------
-- Table structure for `DistributedLock`
-- ----------------------------
CREATE TABLE [SchemaName]"DistributedLock" (
    "Resource"   VARCHAR(100),
    "CreatedAt" TIMESTAMP
);

-- ----------------------------
-- Table structure for `Hash`
-- ----------------------------
CREATE TABLE [SchemaName]"Hash" (
    "Id"        INT,
    "Key"       VARCHAR(255),
    "Value"     CLOB,
    "ExpireAt" TIMESTAMP,
    "Field"     VARCHAR(40)
);
ALTER TABLE [SchemaName]"Hash" ADD PRIMARY KEY ("Id");
ALTER TABLE [SchemaName]"Hash" ADD UNIQUE ("Key", "Field");

-- ----------------------------
-- Table structure for `JobParameter`
-- ----------------------------
CREATE TABLE [SchemaName]"JobParameter" (
    "Id"      INT,
    "Name"    VARCHAR(40),
    "Value"   CLOB,
    "JobId"  INT
);

ALTER TABLE [SchemaName]"JobParameter" ADD PRIMARY KEY ("Id");
ALTER TABLE [SchemaName]"JobParameter" 
ADD CONSTRAINT FK_JOB_PARAMETER_JOB 
FOREIGN KEY ("JobId") REFERENCES "Job" ("Id") ON DELETE CASCADE;

-- ----------------------------
-- Table structure for `JobQueue`
-- ----------------------------
CREATE TABLE [SchemaName]"JobQueue" (
    "Id"           INT,
    "JobId"      INT,
    "Queue"        VARCHAR(50),
    "FetchedAt"   TIMESTAMP,
    "FetchToken"  VARCHAR(36)
);

ALTER TABLE [SchemaName]"JobQueue" ADD PRIMARY KEY ("Id");
ALTER TABLE [SchemaName]"JobQueue" 
ADD CONSTRAINT FK_JOB_QUEUE_JOB 
FOREIGN KEY ("JobId") REFERENCES "Job" ("Id") ON DELETE CASCADE;

-- ----------------------------
-- Table structure for `State`
-- ----------------------------
CREATE TABLE [SchemaName]"State" (
    "Id"          INT,
    "JobId"      INT,
    "Name"        VARCHAR(20),
    "Reason"      VARCHAR(100),
    "CreatedAt"  TIMESTAMP,
    "Data"        CLOB
);

ALTER TABLE [SchemaName]"State" ADD PRIMARY KEY ("Id");
ALTER TABLE [SchemaName]"State" 
ADD CONSTRAINT FK_STATE_JOB 
FOREIGN KEY ("JobId") REFERENCES "Job" ("Id") ON DELETE CASCADE;

-- ----------------------------
-- Table structure for `Server`
-- ----------------------------
CREATE TABLE [SchemaName]"Server" (
    "Id"            VARCHAR(100),
    "Data"          CLOB,
    "LastHeartBeat" TIMESTAMP
);

ALTER TABLE [SchemaName]"Server" ADD PRIMARY KEY ("Id");

-- ----------------------------
-- Table structure for `Set`
-- ----------------------------
CREATE TABLE [SchemaName]"Set" (
    "Id"        INT,
    "Key"       VARCHAR(255),
    "Value"     VARCHAR(255),
    "Score"     DOUBLE,
    "ExpireAt" TIMESTAMP
);

ALTER TABLE [SchemaName]"Set" ADD PRIMARY KEY ("Id");
ALTER TABLE [SchemaName]"Set" ADD UNIQUE ("Key", "Value");

-- ----------------------------
-- Table structure for `List`
-- ----------------------------
CREATE TABLE [SchemaName]"List" (
    "Id"        INT,
    "Key"       VARCHAR(255),
    "Value"     CLOB,
    "ExpireAt" TIMESTAMP
);

ALTER TABLE [SchemaName]"List" ADD PRIMARY KEY ("Id");