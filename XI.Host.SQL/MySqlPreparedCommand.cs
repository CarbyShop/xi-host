using XI.Host.Common;
using MySqlConnector;
using System;
using System.Data;
using System.IO;
using System.Reflection;
using System.Text;

namespace XI.Host.SQL
{
    public class MySqlPreparedCommand : IDisposable
    {
        public MySqlConnection Connection { get; private set; }
        public MySqlTransaction Transaction { get; private set; }

        public MySqlPreparedCommand(in MySqlConnection connection)
        {
            Connection = connection;
        }

        public MySqlPreparedCommand(in MySqlConnection connection, in MySqlTransaction transaction) : this(connection)
        {
            Transaction = transaction;
        }

        private MySqlCommand TryPrepare(ReadOnlySpan<byte> statement, params Couple<object>[] conditions)
        {
            int exceptionRaisedCount = 0;
            MySqlCommand command;

            // Some dead code if the connection never times out.
            do
            {
                try
                {
                    command = new MySqlCommand(Encoding.UTF8.GetString(statement), Connection, Transaction);

                    if (conditions != null && conditions.Length > 0)
                    {
                        for (int i = 0; i < conditions.Length; i++)
                        {
                            command.Parameters.AddWithValue("@" + conditions[i].Key, conditions[i].Value);
                        }
                    }

                    // https://mysqlconnector.net/troubleshooting/transaction-usage/
                    // Command must have transaction set if provided.
                    //if (Transaction != null) // && command.Transaction == null)
                    //{
                    //    command.Transaction = Transaction;
                    //}

                    command.Prepare(); // If the connection were timed out by the server, exception would throw here.

                    break; // All good, return.
                }
                // "Unable to write data to the transport connection: An established connection was aborted by the software in your host machine."
                catch (IOException ex)
                {
                    command = null; // Don't return a bad command.
                    exceptionRaisedCount++;

                    try
                    {
                        Connection.Close();
                    }
                    finally
                    {
                        Connection.Open();
                    }

                    if (exceptionRaisedCount >= 3) // TODO make configurable
                    {
                        Logger.Error(ex, MethodBase.GetCurrentMethod());
                    }
                }
            }
            while (exceptionRaisedCount < 3); // TODO make configurable

            return command;
        }

        public DataTable TrySelect(ReadOnlySpan<byte> statement, Couple<Type>[] columns, params Couple<object>[] conditions)
        {
            DataTable result = null;

            try
            {
                using (MySqlCommand command = TryPrepare(statement, conditions))
                {
                    using (MySqlDataReader reader = command.ExecuteReader())
                    {
                        if (reader.HasRows)
                        {
                            result = new DataTable();

                            for (int i = 0; i < columns.Length; i++)
                            {
                                // For value types, let boxing/unboxing implicitly happen at the DataTable level.
                                // Avoid explicit boxing/unboxing (besides, doing so just makes messy and harder
                                // to maintain code.
                                result.Columns.Add(columns[i].Key, columns[i].Value);
                            }

                            DataRow row;

                            while (reader.Read())
                            {
                                row = result.NewRow();

                                for (int i = 0; i < result.Columns.Count; i++)
                                {
                                    row[i] = reader.GetValue(i);

                                    // If we need to deserialize/marshal anything to an object/structure.  Use the Activator.
                                    //Activator.CreateInstance(result.Columns[i].DataType, reader.GetValue(i));
                                }

                                result.Rows.Add(row);
                            }

                            result.AcceptChanges();
                        }

                        reader.Close();
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, MethodBase.GetCurrentMethod());
            }

            return result;
        }

        public DataTable TrySelect(in QueryParameterContainer queryParameterContainer)
        {
            return TrySelect(queryParameterContainer.Statement, queryParameterContainer.Columns, queryParameterContainer.Parameters);
        }

        // 'protected internal' means: directly invokable only by code in 'this' assembly.
        protected internal bool TryModify(ReadOnlySpan<byte> statement, params Couple<object>[] parameters)
        {
            bool result = true;

            try
            {
                using (MySqlCommand command = TryPrepare(statement, parameters))
                {
                    //command.Connection = Connection;
                    //command.Transaction = Transaction;
                    result = (command.ExecuteNonQuery() > 0);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, MethodBase.GetCurrentMethod());
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
					// Do not dispose the Transation, others might be using it!
                    //Transaction?.Dispose();
                }

                //-TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
                //-TODO: set large fields to null.

                disposedValue = true;
            }
        }

        //-TODO: override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
        // ~MySqlPreparedCommand()
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
