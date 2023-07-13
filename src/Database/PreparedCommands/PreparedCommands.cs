using System.Collections.Frozen;
using DSharpPlus.DSharpPlusHelper.Commands;
using Microsoft.Data.Sqlite;

namespace DSharpPlus.DSharpPlusHelper.Database
{
    // TODO: Generate from a Json file?
    public static partial class PreparedCommands
    {
        private static readonly SqliteConnection Connection;
        public static readonly FrozenDictionary<TagOperations, SqliteCommand> Tags = PrepareTags();

        static PreparedCommands()
        {
            Connection = new(new SqliteConnectionStringBuilder()
            {
                Cache = SqliteCacheMode.Shared,
                DataSource = "res/database.db",
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = true
            }.ConnectionString);
            Connection.Open();
        }

        private static SqliteParameter CreateParameter(SqliteCommand command, string name, SqliteType type, int size = default)
        {
            SqliteParameter parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.SqliteType = type;
            parameter.Size = size;
            return parameter;
        }
    }
}
