using Dapper;
using System;
using System.Collections.Generic;
using System.Data;
using System.Text;

namespace Hangfire.DMStorage.CommonExtension
{
    public static class EntityUtils
    {
        public static long GetNextId(this IDbConnection connection)
        {
            return connection.QuerySingle<long>("SELECT SEQUENCED.NEXTVAL FROM dual");
        }

        public static long GetNextJobId(this IDbConnection connection)
        {
            return connection.QuerySingle<long>("SELECT JOB_ID_SEQ.NEXTVAL FROM dual");
        }
    }
}
