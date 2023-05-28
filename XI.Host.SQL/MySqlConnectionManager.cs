using XI.Host.Common;
using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Data;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace XI.Host.SQL
{
    public class MySqlConnectionManager : IDisposable
    {
        private readonly object synchronizer = new object();

        private MySqlConnection Connection;
        private MySqlTransaction Transaction;

        public MySqlConnectionStringBuilder ConnectionString { get; private set; }

        public MySqlConnectionManager() : this(Global.Config["SQL_HOST"], Global.GetConfigAsUShort("SQL_PORT"), Global.Config["SQL_DATABASE"], Global.Config["SQL_LOGIN"], Global.Config["SQL_PASSWORD"]) { }

        public MySqlConnectionManager(string database, string user, string password) : this(IPAddress.Loopback.ToString(), 3306, database, user, password) { }

        public MySqlConnectionManager(string server, in ushort port, string database, string user, string password)
        {
            ConnectionString = new MySqlConnectionStringBuilder()
            {
                Server = server,
                UserID = user,
                Database = database,
                Port = port,
                Password = password,
                //IgnorePrepare = false,
                //UseXaTransactions = false,
                //IgnoreCommandTransaction = true,
            };
            
            Connection = new MySqlConnection(ConnectionString.ToString());
        }

        private bool TryConnect()
        {
            bool result = true;

            try
            {
                if (Connection.State == ConnectionState.Closed || Connection.State == ConnectionState.Broken)
                {
                    Connection.Open();

                    if (Connection.State != ConnectionState.Open)
                    {
                        Logger.Warning("Unable to connect.", MethodBase.GetCurrentMethod());
                        result = false;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, MethodBase.GetCurrentMethod());
                result = false;
            }

            return result;
        }

        public DataTable Select(ReadOnlySpan<byte> statement, Couple<Type>[] columns, params Couple<object>[] parameters)
        {
            DataTable result = null;

            // Critical section; one attempt at a time (all threads).
            lock (synchronizer)
            {
                if (TryConnect())
                {
                    using (var command = new MySqlPreparedCommand(Connection))
                    {
                        result = command.TrySelect(statement, columns, parameters);
                    }
                }
            }

            return result;
        }

        public DataTable Select(in QueryParameterContainer queryParameterContainer)
        {
            DataTable result = null;

            // Critical section; one attempt at a time (all threads).
            lock (synchronizer)
            {
                if (TryConnect())
                {
                    using (var command = new MySqlPreparedCommand(Connection))
                    {
                        result = command.TrySelect(queryParameterContainer);
                    }
                }
            }

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool Modify(ReadOnlySpan<byte> statement, params Couple<object>[] parameters)
        {
            bool result = false;

            // Critical section; one attempt at a time (all threads).
            lock (synchronizer)
            {
                if (TryConnect())
                {
                    using (var command = new MySqlPreparedCommand(Connection, Transaction))
                    {
                        result = command.TryModify(statement, parameters);
                    }
                }
            }

            return result;
        }

        public bool Update(ReadOnlySpan<byte> statement, params Couple<object>[] parameters)
        {
            return Modify(statement, parameters);
        }

        public bool Delete(ReadOnlySpan<byte> statement, params Couple<object>[] parameters)
        {
            return Modify(statement, parameters);
        }

        public bool Insert(ReadOnlySpan<byte> statement, params Couple<object>[] parameters)
        {
            return Modify(statement, parameters);
        }

        public bool Replace(ReadOnlySpan<byte> statement, params Couple<object>[] parameters)
        {
            return Modify(statement, parameters);
        }

        public bool SelectInvoke(in Func<DataTable, bool> action, in QueryParameterContainer queryParameterContainer)
        {
            bool result = false;

            using (DataTable dataTable = Select(queryParameterContainer))
            {
                result = action.Invoke(dataTable);
            }

            return result;
        }

        public T SelectInvoke<T>(in Func<DataRow, T> action, in QueryParameterContainer queryParameterContainer)
        {
            T result = default;

            using (DataTable dataTable = Select(queryParameterContainer))
            {
                if (dataTable != null && dataTable.Rows.Count > 0)
                {
                    result = action.Invoke(dataTable.Rows[0]);
                }
            }

            return result;
        }

        public List<T> SelectInvokeEachRow<T>(in Func<DataRow, T> action, ReadOnlySpan<byte> statement, Couple<Type>[] columns, params Couple<object>[] parameters)
        {
            List<T> result = new List<T>();

            using (DataTable dataTable = Select(statement, columns, parameters))
            {
                if (dataTable != null && dataTable.Rows.Count > 0)
                {
                    for (int i = 0; i < dataTable.Rows.Count; i++)
                    {
                        result.Add(action.Invoke(dataTable.Rows[i]));
                    }
                }
            }

            return result;
        }

        public bool TryTransactionBegin()
        {
            bool result = true;

            if (Transaction == null)
            {
                result = TryConnect();

                if (result)
                {
                    try
                    {
                        // Sources:
                        // Maria https://mariadb.com/kb/en/mariadb-transactions-and-isolation-levels-for-sql-server-users/
                        // MariaDB supports the following isolation levels: READ UNCOMMITTED, READ COMMITTED, REPEATABLE
                        // READ, SERIALIZABLE.  The default, the isolation level in MariaDB is REPEATABLE READ.
                        // MySQL https://dev.mysql.com/doc/refman/5.5/en/innodb-transaction-isolation-levels.html
                        // InnoDB offers all four transaction isolation levels described by the SQL:1992 standard: READ
                        // UNCOMMITTED, READ COMMITTED, REPEATABLE READ, and SERIALIZABLE. The default isolation level for
                        // InnoDB is REPEATABLE READ. 
                        Transaction = Connection.BeginTransaction(IsolationLevel.ReadUncommitted);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, MethodBase.GetCurrentMethod());
                        result = false;
                    }
                }
            }
            else
            {
                Logger.Warning("Transaction pending.", MethodBase.GetCurrentMethod());
                result = false;
            }

            return result;
        }

        public bool TryTransactionRollback()
        {
            bool result = true;

            if (Transaction != null)
            {
                try
                {
                    Transaction.Rollback();
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, MethodBase.GetCurrentMethod());
                    result = false;
                }
                finally
                {
                    Transaction.Dispose();
                    Transaction = null;
                }
            }
            else
            {
                Logger.Warning("No transaction to rollback.", MethodBase.GetCurrentMethod());
                result = false;
            }

            return result;
        }

        public bool TryTransactionCommit()
        {
            bool result = true;

            if (Transaction != null)
            {
                try
                {
                    // Read-only.
                    //Transaction.Connection = Connection;
                    Transaction.Commit();
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, MethodBase.GetCurrentMethod());
                    result = false;
                }
                // Do not dispose the Transaction here, because rollback needs it.
                //finally
                //{
                //    Transaction.Dispose();
                //    Transaction;
                //}
            }
            else
            {
                Logger.Warning("No transaction to commit.", MethodBase.GetCurrentMethod());
                result = false;
            }

            return result;
        }

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    //-TODO: dispose managed state (managed objects).
                    if (Transaction != null)
                    {
                        Transaction.Dispose();
                        // Do not set to null.
                    }

                    if (Connection != null)
                    {
                        if (Connection.State == ConnectionState.Open)
                        {
                            Connection.Close();
                        }

                        Connection.Dispose();
                    }
                }

                //-TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
                //-TODO: set large fields to null.

                disposedValue = true;
            }
        }

        //-TODO: override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
        // ~SqlConnectionManager()
        // {
        //   // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
        //   Dispose(false);
        // }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
            //-TODO: uncomment the following line if the finalizer is overridden above.
            // GC.SuppressFinalize(this);
        }
        #endregion
    }
}
