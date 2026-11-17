using Microsoft.EntityFrameworkCore;
using System.Linq;

namespace AMAIS.WKFLO.MD.FunctionalTests.Common.Utilities
{
    public static class EFCleanHelper
    {
        public static void Clear<T>(this DbContext context) where T : class
        {
            DbSet<T> dbSet = context.Set<T>();
            if (dbSet.Any())
            {
                dbSet.RemoveRange(dbSet.ToList());
            }
        }

        public static void Clear<T>(this DbSet<T> dbSet) where T : class
        {
            if (dbSet.Any())
            {
                dbSet.RemoveRange(dbSet.ToList());
            }
        }

        public static string Truncate(this DbContext context, string tableNameWithSchema)
        {
            string cmd = $"TRUNCATE TABLE {tableNameWithSchema}";
            context.Database.ExecuteSqlRaw(cmd);
            return cmd;
        }

        public static string Delete(this DbContext context, string tableNameWithSchema)
        {
            string cmd = $"DELETE FROM {tableNameWithSchema}";
            context.Database.ExecuteSqlRaw(cmd);
            return cmd;
        }
    }
}
