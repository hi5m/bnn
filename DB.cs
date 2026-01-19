using System.Data;
using System.Data.Common;

namespace DbSpace
{
    public static class DbCommandExtensions
    {
        /// <summary>
        /// Add param to SQL query
        /// </summary>
        public static void AddParam(this DbCommand aDbCommand, string parameterName, object? value = null)
        {
            if (DB.ProviderFactory == null) throw new Exception("DB provider is null");
            var dbParameter = DB.ProviderFactory.CreateParameter();
            if (dbParameter == null) return;
            dbParameter.ParameterName = parameterName;
            dbParameter.Value = value;

            // dbParameter.DbType = DbType;
            if (value is string) dbParameter.DbType = DbType.String;
            if (value is int) dbParameter.DbType = DbType.Int64;
            if (value is DateTime) dbParameter.DbType = DbType.DateTime;

            aDbCommand.Parameters.Add(dbParameter);
        }

        public static long InsertWithAutoinc(this DbCommand dbCommand)
        {
            var queryWords = dbCommand.CommandText.Replace("  ", " ").Trim().Split(' ');
            // var tableToInsert = queryWords[2];
            var autoincFieldName = queryWords[3].Trim('(', ')', ',', ' ');
            long maxId = 0;

            DB.AddParam(dbCommand, autoincFieldName, null);
            dbCommand.CommandText = dbCommand.CommandText + "; SELECT last_insert_rowid() " + autoincFieldName;
            using var dbDataReader = dbCommand.ExecuteReader();
            if (dbDataReader.HasRows)
            {
                while (dbDataReader.Read())
                {
                    if (dbDataReader[autoincFieldName] as long? == null) maxId = -1;
                    else maxId = (long)dbDataReader[autoincFieldName];
                }
            }
            else maxId = -1;
            return maxId;
        }
    }

    public static class DB
    {
        private static readonly string s_dbProviderName = "System.Data.SQLite"; //  "DbProvider_ReadFromJson_Settings!";//  = ConfigurationManager.AppSettings["DbProvider"];
        private static readonly string s_dbConnectionString = @"Data Source=..\bnnc.sq3"; // D:\temp\bnnc\ ;//  = ConfigurationManager.AppSettings["DbConnectionString"];
        private static DbProviderFactory? s_providerFactory;

        public static DbProviderFactory? ProviderFactory { get { return s_providerFactory; } }

        public static DbConnection CreateConnection()
        {
            DbProviderFactories.RegisterFactory(s_dbProviderName, System.Data.SQLite.SQLiteFactory.Instance);
            s_providerFactory = DbProviderFactories.GetFactory(s_dbProviderName);
            var dbConnection = s_providerFactory.CreateConnection() ?? throw new Exception("Connection is not defined");
            dbConnection.ConnectionString = s_dbConnectionString;
            dbConnection.Open();
            return dbConnection;
        }

        public static DbCommand CreateCommand(DbConnection connection, string commandText)
        {
            if (s_providerFactory == null) throw new Exception("Command is not defined");
            var dbCommand = s_providerFactory.CreateCommand() ?? throw new Exception("Command is not defined");
            dbCommand.Connection = connection;
            dbCommand.CommandText = commandText;
            return dbCommand;
        }

        public static void OpenSingleQuery(string commandText, IDictionary<string, object>? parameters, Func<DbDataReader, bool> onData)
        {
            using var dbConnection = CreateConnection();
            OpenQuery(dbConnection, commandText, parameters, onData);
        }

        public static void OpenSingleQuery(string commandText, IDictionary<string, object>? parameters, Action<DbDataReader> onData)
        {
            using var dbConnection = CreateConnection();
            OpenQuery(dbConnection, commandText, parameters, onData);
        }

        public static void OpenQuery(DbConnection connection, string commandText, IDictionary<string, object>? parameters, Action<DbDataReader> onData)
        {
            using var dbCommand = DB.CreateCommand(connection, commandText);
            if (parameters != null)
            {
                foreach (var p in parameters)
                {
                    dbCommand.AddParam(p.Key, p.Value);
                }
            }

            using var dbDataReader = dbCommand.ExecuteReader();
            while (dbDataReader.Read())
            {
                onData(dbDataReader);
            }
        }

        public static void OpenQuery(DbConnection connection, string commandText, IDictionary<string, object>? parameters, Func<DbDataReader, bool> onData)
        {
            using var dbCommand = DB.CreateCommand(connection, commandText);
            if (parameters != null)
            {
                foreach (var p in parameters)
                {
                    dbCommand.AddParam(p.Key, p.Value);
                }
            }

            using var dbDataReader = dbCommand.ExecuteReader();
            while (dbDataReader.Read())
            {
                if (!onData(dbDataReader)) break;
            }
        }

        public static void AddParam(DbCommand dbCommand, string parameterName, object? value)
        {
            if (s_providerFactory == null) throw new Exception("Command is not defined");
            var dbParameter = s_providerFactory.CreateParameter();
            if (dbParameter == null) return;
            dbParameter.ParameterName = parameterName;
            dbParameter.Value = value;

            if (value is string) dbParameter.DbType = DbType.String;
            if (value is int) dbParameter.DbType = DbType.Int64;
            if ((value is DateTime) || (value is TimeSpan)) dbParameter.DbType = DbType.DateTime;

            dbCommand.Parameters.Add(dbParameter);
        }

        public static long SingleInsertWithAutoinc(string commandText, IDictionary<string, object> parameters)
        {
            using var dbConnection = CreateConnection();
            return InsertWithAutoinc(dbConnection, commandText, parameters);
        }

        public static long InsertWithAutoinc(DbConnection connection, string commandText, IDictionary<string, object> parameters)
        {
            using var dbCommand = CreateCommand(connection, commandText);
            if (parameters != null)
            {
                foreach (var p in parameters)
                {
                    dbCommand.AddParam(p.Key, p.Value);
                }
            }
            return dbCommand.InsertWithAutoinc();
        }

        public static int ExecQuery(DbConnection connection, string commandText, IDictionary<string, object>? parameters)
        {
            using var dbCommand = CreateCommand(connection, commandText);
            if (parameters != null)
            {
                foreach (var p in parameters)
                {
                    dbCommand.AddParam(p.Key, p.Value);
                }
            }
            return dbCommand.ExecuteNonQuery();
        }

        public static int ExecSingleQuery(string commandText, IDictionary<string, object>? parameters)
        {
            using var dbConnection = CreateConnection();
            return ExecQuery(dbConnection, commandText, parameters);
        }
    }
}
