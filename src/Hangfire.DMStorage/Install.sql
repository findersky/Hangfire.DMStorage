DECLARE
    exists_count      INTEGER;
    TYPE seq_list IS TABLE OF VARCHAR2(30);
    v_seqs           seq_list := seq_list('SEQUENCED', 'JOB_ID_SEQ');
    v_owner          CONSTANT VARCHAR2(30) := '[SchemaNameOnly]';  -- 模式名变量（空表示当前模式）
    v_owner_filter   VARCHAR2(60);  -- 动态生成的模式过滤条件
    v_owner_prefix   VARCHAR2(60);  -- 动态生成的模式名前缀
BEGIN
    -- 动态生成模式名前缀和过滤条件
    IF v_owner IS NOT NULL AND v_owner != '' THEN
        v_owner_prefix := v_owner || '.';
        v_owner_filter := 'AND SEQUENCE_OWNER = ''' || UPPER(v_owner) || '''';  -- 达梦默认大写
    ELSE
        v_owner_prefix := '';  -- 当前模式
        v_owner_filter := 'AND SEQUENCE_OWNER = USER';  -- 使用当前用户模式
    END IF;

    -- 遍历所有序列名
    FOR i IN 1..v_seqs.COUNT LOOP
        -- 动态构建查询语句
        EXECUTE IMMEDIATE 
            'SELECT COUNT(*) FROM ALL_SEQUENCES 
             WHERE SEQUENCE_NAME = :1 
             ' || v_owner_filter  -- 动态拼接过滤条件
        INTO exists_count
        USING UPPER(v_seqs(i));  -- 绑定序列名（统一大写）

        -- 创建序列（如果不存在）
        IF exists_count = 0 THEN
            EXECUTE IMMEDIATE 
                'CREATE SEQUENCE ' || v_owner_prefix || v_seqs(i) || 
                ' START WITH 1 
                 INCREMENT BY 1 
                 MAXVALUE 9999999999999999999999999999 
                 MINVALUE 1 
                 NOCYCLE 
                 CACHE 20';
            DBMS_OUTPUT.PUT_LINE('序列 ' || v_owner_prefix || v_seqs(i) || ' 创建成功');
        ELSE
            DBMS_OUTPUT.PUT_LINE('序列 ' || v_owner_prefix || v_seqs(i) || ' 已存在');
        END IF;
    END LOOP;
END;
/

-- ----------------------------
-- Table structure for `Job`
-- ----------------------------
CREATE TABLE IF NOT EXISTS [SchemaName]"Job" (
    "Id"              INT PRIMARY KEY,
    "StateId"        INT,
    "StateName"      VARCHAR(20),
    "InvocationData" CLOB,
    "Arguments"       CLOB,
    "CreatedAt"      TIMESTAMP,
    "ExpireAt"       TIMESTAMP
);
/

DECLARE
    index_exists INTEGER;
    v_owner VARCHAR2(30) := '[SchemaNameOnly]';  -- 指定模式名
BEGIN
    -- 检查索引是否存在（跨模式）
    EXECUTE IMMEDIATE '
        SELECT COUNT(*) 
        FROM ALL_INDEXES 
        WHERE INDEX_NAME = ''IX_HangFire_Job_StateName''
          AND OWNER = UPPER(:1)'
    INTO index_exists
    USING v_owner;

    IF index_exists = 0 THEN
        EXECUTE IMMEDIATE '
            CREATE INDEX "' || v_owner || '"."IX_HangFire_Job_StateName" 
            ON "' || v_owner || '"."Job" ("StateName")';
    END IF;
END;
/

DECLARE
    v_schema_name   VARCHAR2(30) := '[SchemaNameOnly]';        -- 指定模式名（为空时使用当前模式）
    v_table_name    VARCHAR2(30) := 'Job';           -- 表名（需与定义时大小写一致）
    v_index_name    VARCHAR2(30) := 'IX_HangFire_Job_ExpireAt'; -- 索引名（保留大小写）
    index_exists    INTEGER := 0;
BEGIN
    IF v_schema_name IS NOT NULL AND v_schema_name != '' THEN
        EXECUTE IMMEDIATE '
            SELECT COUNT(*) 
            FROM ALL_INDEXES 
            WHERE OWNER = :1 
              AND INDEX_NAME = :2 
              AND TABLE_NAME = :3'
        INTO index_exists
        USING UPPER(v_schema_name), v_index_name, v_table_name;
    ELSE
        SELECT COUNT(*) INTO index_exists
        FROM USER_INDEXES
        WHERE INDEX_NAME = v_index_name 
          AND TABLE_NAME = v_table_name;
    END IF;

    IF index_exists = 0 THEN
        EXECUTE IMMEDIATE '
            CREATE INDEX "' || v_index_name || '" 
            ON "' || 
                CASE WHEN v_schema_name != '' THEN v_schema_name || '"."' ELSE '' END 
                || v_table_name || '" 
            ("ExpireAt")';
        DBMS_OUTPUT.PUT_LINE('索引 "' || v_index_name || '" 创建成功');
    END IF;
EXCEPTION
    WHEN OTHERS THEN
        DBMS_OUTPUT.PUT_LINE('错误: ' || SQLERRM);
END;
/

-- ----------------------------
-- Table structure for `Counter`
-- ----------------------------
CREATE TABLE IF NOT EXISTS [SchemaName]"Counter" (
    "Id"        INT PRIMARY KEY,
    "Key"       VARCHAR(255),
    "Value"     INT,
    "ExpireAt"  TIMESTAMP
);
/

-- ----------------------------
-- Table structure for `AggregatedCounter`
-- ----------------------------
CREATE TABLE IF NOT EXISTS [SchemaName]"AggregatedCounter" (
    "Id"        INT PRIMARY KEY,
    "Key"       VARCHAR(255),
    "Value"     INT,
    "ExpireAt" TIMESTAMP
);
/

DECLARE
    constraint_exists INTEGER;
BEGIN
    -- 检查是否存在包含 "Key" 和 "Value" 列的唯一约束
    SELECT COUNT(*) 
    INTO constraint_exists
    FROM (
        SELECT c.CONSTRAINT_NAME
        FROM USER_CONSTRAINTS c
        JOIN USER_CONS_COLUMNS col 
          ON c.CONSTRAINT_NAME = col.CONSTRAINT_NAME
        WHERE c.OWNER='[SchemaNameOnly]' AND c.TABLE_NAME = 'AggregatedCounter'  -- 注意表名大小写需与实际存储一致
          AND c.CONSTRAINT_TYPE = 'U'  -- U 表示唯一约束
          AND col.COLUMN_NAME IN ('Key', 'Value')  -- 达梦默认列名大写，若定义时用双引号需精确匹配
        GROUP BY c.CONSTRAINT_NAME
        HAVING COUNT(DISTINCT col.COLUMN_NAME) = 2  -- 确保两个列都参与约束
    );

    -- 如果不存在则创建唯一约束
    IF constraint_exists = 0 THEN
        EXECUTE IMMEDIATE '
            ALTER TABLE [SchemaName]"AggregatedCounter" 
            ADD CONSTRAINT UQ_AggCounter_KeyVal  -- 显式命名约束便于管理
            UNIQUE ("Key", "Value")
        ';
        DBMS_OUTPUT.PUT_LINE('唯一约束添加成功');
    END IF;
END;
/


-- ----------------------------
-- Table structure for `DistributedLock`
-- ----------------------------
CREATE TABLE IF NOT EXISTS [SchemaName]"DistributedLock" (
    "Resource"   VARCHAR(100),
    "CreatedAt" TIMESTAMP
);
/
-- ----------------------------
-- Table structure for `Hash`
-- ----------------------------
CREATE TABLE IF NOT EXISTS [SchemaName]"Hash" (
    "Id"        INT PRIMARY KEY,
    "Key"       VARCHAR(255),
    "Value"     CLOB,
    "ExpireAt" TIMESTAMP,
    "Field"     VARCHAR(40)
);
/

DECLARE
    constraint_exists INTEGER;
BEGIN
    SELECT COUNT(*) 
    INTO constraint_exists
    FROM (
        SELECT c.CONSTRAINT_NAME
        FROM USER_CONSTRAINTS c
        JOIN USER_CONS_COLUMNS col 
          ON c.CONSTRAINT_NAME = col.CONSTRAINT_NAME
        WHERE c.OWNER='[SchemaNameOnly]' AND c.TABLE_NAME = 'Hash' 
          AND c.CONSTRAINT_TYPE = 'U'  
          AND col.COLUMN_NAME IN ('Key', 'Field')  
        GROUP BY c.CONSTRAINT_NAME
        HAVING COUNT(DISTINCT col.COLUMN_NAME) = 2
    );

    -- 如果不存在则创建唯一约束
    IF constraint_exists = 0 THEN
        EXECUTE IMMEDIATE '
            ALTER TABLE [SchemaName]"Hash" 
            ADD CONSTRAINT UQ_Hash_KeyField  -- 显式命名约束便于管理
            UNIQUE ("Key", "Field")
        ';
        DBMS_OUTPUT.PUT_LINE('唯一约束添加成功');
    END IF;
END;
/

-- ----------------------------
-- Table structure for `JobParameter`
-- ----------------------------
CREATE TABLE IF NOT EXISTS [SchemaName]"JobParameter" (
    "Id"      INT PRIMARY KEY,
    "Name"    VARCHAR(40),
    "Value"   CLOB,
    "JobId"  INT
);
/


DECLARE
    table_exists       INTEGER;
    constraint_exists  INTEGER;
    v_owner           VARCHAR2(30) := '[SchemaNameOnly]';  -- 指定模式名，空字符串表示当前模式
BEGIN
    -- 检查表是否存在
    IF v_owner IS NOT NULL AND v_owner != '' THEN
        EXECUTE IMMEDIATE '
            SELECT COUNT(*) 
            FROM ALL_TABLES 
            WHERE TABLE_NAME = ''JobParameter''
              AND OWNER = :1'
        INTO table_exists
        USING v_owner;
    ELSE
        SELECT COUNT(*) INTO table_exists
        FROM USER_TABLES 
        WHERE TABLE_NAME = 'JobParameter';
    END IF;

    IF table_exists = 0 THEN
        DBMS_OUTPUT.PUT_LINE('表 JobParameter 不存在，请先创建');
        RETURN;
    END IF;

    -- 检查外键约束
    IF v_owner IS NOT NULL AND v_owner != '' THEN
        EXECUTE IMMEDIATE '
            SELECT COUNT(*) 
            FROM ALL_CONSTRAINTS 
            WHERE CONSTRAINT_NAME = ''FK_JOB_PARAMETER_JOB''
              AND CONSTRAINT_TYPE = ''R''
              AND TABLE_NAME = ''JobParameter''
              AND OWNER = :1'
        INTO constraint_exists
        USING v_owner;
    ELSE
        SELECT COUNT(*) INTO constraint_exists
        FROM USER_CONSTRAINTS
        WHERE CONSTRAINT_NAME = 'FK_JOB_PARAMETER_JOB'
          AND CONSTRAINT_TYPE = 'R'
          AND TABLE_NAME = 'JobParameter';
    END IF;

    -- 动态执行DDL
    IF constraint_exists = 0 THEN
        EXECUTE IMMEDIATE '
            ALTER TABLE ' || 
            CASE WHEN v_owner != '' THEN v_owner || '.' END || 
            '"JobParameter" 
            ADD CONSTRAINT FK_JOB_PARAMETER_JOB 
            FOREIGN KEY ("JobId") 
            REFERENCES ' || 
            CASE WHEN v_owner != '' THEN v_owner || '.' END || 
            '"Job" ("Id") 
            ON DELETE CASCADE
        ';
        DBMS_OUTPUT.PUT_LINE('外键约束添加成功');
    ELSE
        DBMS_OUTPUT.PUT_LINE('外键约束已存在');
    END IF;
EXCEPTION
    WHEN OTHERS THEN
        DBMS_OUTPUT.PUT_LINE('错误: ' || SQLERRM);
END;
/

-- ----------------------------
-- Table structure for `JobQueue`
-- ----------------------------
CREATE TABLE IF NOT EXISTS [SchemaName]"JobQueue" (
    "Id"           INT PRIMARY KEY,
    "JobId"      INT,
    "Queue"        VARCHAR(50),
    "FetchedAt"   TIMESTAMP,
    "FetchToken"  VARCHAR(36)
);
/

DECLARE
    table_exists      INTEGER := 0;
    constraint_exists INTEGER := 0;
    v_owner          VARCHAR2(30) := '[SchemaNameOnly]';  -- 模式名变量（空字符串表示当前模式）
BEGIN
    -- [1] 检查表是否存在（防止表不存在时报错）
    IF v_owner IS NOT NULL AND v_owner != '' THEN
        -- 跨模式检查表存在性
        EXECUTE IMMEDIATE '
            SELECT COUNT(*) 
            FROM ALL_TABLES 
            WHERE TABLE_NAME = ''JobQueue''     -- 注意达梦默认存储大写
              AND OWNER = UPPER(:1)'            -- 模式名转大写
        INTO table_exists
        USING v_owner;
    ELSE
        -- 当前模式检查
        SELECT COUNT(*) INTO table_exists
        FROM USER_TABLES 
        WHERE TABLE_NAME = 'JobQueue';          -- 未用双引号时表名大写
    END IF;

    -- 表不存在时终止执行
    IF table_exists = 0 THEN
        DBMS_OUTPUT.PUT_LINE('错误: 表 JobQueue 不存在');
        RETURN;
    END IF;

    -- [2] 检查外键约束是否存在
    IF v_owner IS NOT NULL AND v_owner != '' THEN
        -- 跨模式检查约束
        EXECUTE IMMEDIATE '
            SELECT COUNT(*) 
            FROM ALL_CONSTRAINTS 
            WHERE CONSTRAINT_NAME = ''FK_JOB_QUEUE_JOB''
              AND CONSTRAINT_TYPE = ''R''
              AND TABLE_NAME = ''JobQueue''
              AND OWNER = UPPER(:1)'
        INTO constraint_exists
        USING v_owner;
    ELSE
        -- 当前模式检查
        SELECT COUNT(*) INTO constraint_exists
        FROM USER_CONSTRAINTS
        WHERE CONSTRAINT_NAME = 'FK_JOB_QUEUE_JOB'
          AND CONSTRAINT_TYPE = 'R'
          AND TABLE_NAME = 'JobQueue';
    END IF;

    -- [3] 动态执行DDL
    IF constraint_exists = 0 THEN
        EXECUTE IMMEDIATE '
            ALTER TABLE ' || 
            CASE WHEN v_owner != '' THEN '"' || v_owner || '"."JobQueue"' ELSE '"JobQueue"' END || 
            ' ADD CONSTRAINT FK_JOB_QUEUE_JOB 
            FOREIGN KEY ("JobId") 
            REFERENCES ' || 
            CASE WHEN v_owner != '' THEN '"' || v_owner || '"."Job"' ELSE '"Job"' END || 
            ' ("Id") 
            ON DELETE CASCADE
        ';
        DBMS_OUTPUT.PUT_LINE('外键约束 FK_JOB_QUEUE_JOB 添加成功');
    ELSE
        DBMS_OUTPUT.PUT_LINE('外键约束 FK_JOB_QUEUE_JOB 已存在');
    END IF;
END;
/


-- ----------------------------
-- Table structure for `State`
-- ----------------------------
CREATE TABLE  IF NOT EXISTS  [SchemaName]"State" (
    "Id"          INT PRIMARY KEY,
    "JobId"      INT,
    "Name"        VARCHAR(20),
    "Reason"      VARCHAR(100),
    "CreatedAt"  TIMESTAMP,
    "Data"        CLOB
);
/

DECLARE
    table_exists      INTEGER := 0;
    constraint_exists INTEGER := 0;
    v_owner          VARCHAR2(30) := '[SchemaNameOnly]';  -- 模式名变量
BEGIN
    -- [1] 检查表是否存在（跨模式）
    EXECUTE IMMEDIATE '
        SELECT COUNT(*) 
        FROM ALL_TABLES 
        WHERE TABLE_NAME = ''State'' 
          AND OWNER = :1'  -- 注意模式名大小写需与实际一致
    INTO table_exists
    USING v_owner;

    IF table_exists = 0 THEN
        DBMS_OUTPUT.PUT_LINE('错误: 表 "' || v_owner || '"."State" 不存在');
        RETURN;
    END IF;

    -- [2] 检查外键约束是否存在（跨模式）
    EXECUTE IMMEDIATE '
        SELECT COUNT(*) 
        FROM ALL_CONSTRAINTS c
        JOIN ALL_CONS_COLUMNS col 
          ON c.OWNER = col.OWNER 
         AND c.CONSTRAINT_NAME = col.CONSTRAINT_NAME
        WHERE c.OWNER = :1
          AND c.TABLE_NAME = ''State''
          AND c.CONSTRAINT_TYPE = ''R''
          AND c.CONSTRAINT_NAME = ''FK_STATE_JOB''
          AND col.COLUMN_NAME = ''JobId'''
    INTO constraint_exists
    USING v_owner;

    -- [3] 动态执行DDL（带模式名前缀）
    IF constraint_exists = 0 THEN
        EXECUTE IMMEDIATE '
            ALTER TABLE "' || v_owner || '"."State" 
            ADD CONSTRAINT "FK_STATE_JOB" 
            FOREIGN KEY ("JobId") 
            REFERENCES "' || v_owner || '"."Job" ("Id") 
            ON DELETE CASCADE
        ';
        DBMS_OUTPUT.PUT_LINE('外键约束 "' || v_owner || '"."FK_STATE_JOB" 添加成功');
    ELSE
        DBMS_OUTPUT.PUT_LINE('外键约束 "' || v_owner || '"."FK_STATE_JOB" 已存在');
    END IF;
END;
/

-- ----------------------------
-- Table structure for `Server`
-- ----------------------------
CREATE TABLE IF NOT EXISTS [SchemaName]"Server" (
    "Id"            VARCHAR(100) PRIMARY KEY ,
    "Data"          CLOB,
    "LastHeartBeat" TIMESTAMP
);
/

-- ----------------------------
-- Table structure for `Set`
-- ----------------------------
CREATE TABLE  IF NOT EXISTS  [SchemaName]"Set" (
    "Id"        INT PRIMARY KEY,
    "Key"       VARCHAR(255),
    "Value"     VARCHAR(255),
    "Score"     DOUBLE,
    "ExpireAt" TIMESTAMP
);
/

DECLARE
    constraint_exists INTEGER;
BEGIN
    SELECT COUNT(*) 
    INTO constraint_exists
    FROM (
        SELECT c.CONSTRAINT_NAME
        FROM USER_CONSTRAINTS c
        JOIN USER_CONS_COLUMNS col 
          ON c.CONSTRAINT_NAME = col.CONSTRAINT_NAME
        WHERE c.OWNER='[SchemaNameOnly]' AND c.TABLE_NAME = 'Set' 
          AND c.CONSTRAINT_TYPE = 'U'  
          AND col.COLUMN_NAME IN ('Key', 'Value')  
        GROUP BY c.CONSTRAINT_NAME
        HAVING COUNT(DISTINCT col.COLUMN_NAME) = 2
    );

    -- 如果不存在则创建唯一约束
    IF constraint_exists = 0 THEN
        EXECUTE IMMEDIATE '
            ALTER TABLE [SchemaName]"Set" 
            ADD CONSTRAINT UQ_Set_KeyValue  -- 显式命名约束便于管理
            UNIQUE ("Key", "Value")
        ';
        DBMS_OUTPUT.PUT_LINE('唯一约束添加成功');
    END IF;
END;
/


-- ----------------------------
-- Table structure for `List`
-- ----------------------------
CREATE TABLE  IF NOT EXISTS  [SchemaName]"List" (
    "Id"        INT PRIMARY KEY,
    "Key"       VARCHAR(255),
    "Value"     CLOB,
    "ExpireAt" TIMESTAMP
);