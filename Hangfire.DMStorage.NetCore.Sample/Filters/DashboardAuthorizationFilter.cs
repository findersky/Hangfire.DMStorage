using Hangfire.Dashboard;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hangfire.DMStorage.NetCore.Sample.Filters
{
    public class DashboardAuthorizationFilter : IDashboardAuthorizationFilter
    {
        public bool Authorize(DashboardContext context)
        {
            // 在此处可以加入任何授权逻辑，返回 true 表示允许访问控制面板
            // 如果没有授权逻辑，任何人都可以访问控制面板
            return true; // 允许所有用户访问
        }
    }
}
