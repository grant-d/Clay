#region License

// Copyright (c) K2 Workflow (SourceCode Technology Holdings Inc.). All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

#endregion

using System;
using System.Data;
using System.Data.SqlClient;
using System.Threading.Tasks;

namespace SourceCode.Clay.Data.SqlClient
{
    /// <summary>
    /// Represents extensions for <see cref="SqlConnection"/> instances.
    /// </summary>
    /// <seealso cref="SqlConnection"/>
    public static class SqlConnectionExtensions
    {
        #region SqlCommand

        /// <summary>
        /// Create a <see cref="SqlCommand"/> using the provided parameters.
        /// </summary>
        /// <param name="sqlCon">The <see cref="SqlConnection"/> to use.</param>
        /// <param name="commandText">The sql command text to use.</param>
        /// <param name="commandType">The type of command.</param>
        /// <returns></returns>
        public static SqlCommand CreateCommand(this SqlConnection sqlCon, string commandText, CommandType commandType)
        {
            if (sqlCon == null) throw new ArgumentNullException(nameof(sqlCon));
            if (string.IsNullOrWhiteSpace(commandText)) throw new ArgumentNullException(nameof(commandText));

            var cmd = new SqlCommand(commandText, sqlCon)
            {
                CommandType = commandType
            };

            return cmd;
        }

        /// <summary>
        /// Create a <see cref="SqlCommand"/> using the provided parameters.
        /// </summary>
        /// <param name="sqlCon">The <see cref="SqlConnection"/> to use.</param>
        /// <param name="commandText">The sql command text to use.</param>
        /// <param name="commandType">The type of command.</param>
        /// <param name="timeoutSeconds">The command timeout.</param>
        /// <returns></returns>
        public static SqlCommand CreateCommand(this SqlConnection sqlCon, string commandText, CommandType commandType, int timeoutSeconds)
        {
            var cmd = CreateCommand(sqlCon, commandText, commandType);
            cmd.CommandTimeout = timeoutSeconds;

            return cmd;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Reopens the specified <see cref="SqlConnection"/>.
        /// </summary>
        /// <param name="sqlCon">The connection.</param>
        public static void Reopen(this SqlConnection sqlCon)
        {
            if (sqlCon == null) throw new ArgumentNullException(nameof(sqlCon));

            switch (sqlCon.State)
            {
                case ConnectionState.Broken:
                    {
                        sqlCon.Close();
                        sqlCon.Open();
                        return;
                    }

                case ConnectionState.Closed:
                    {
                        sqlCon.Open();
                        return;
                    }

                default: return;
            }
        }

        /// <summary>
        /// Reopens the specified <see cref="SqlConnection"/>.
        /// </summary>
        /// <param name="sqlCon">The connection.</param>
        public static async Task ReopenAsync(this SqlConnection sqlCon)
        {
            if (sqlCon == null) throw new ArgumentNullException(nameof(sqlCon));

            switch (sqlCon.State)
            {
                case ConnectionState.Broken:
                    {
                        sqlCon.Close();
                        await sqlCon.OpenAsync().ConfigureAwait(false);
                        return;
                    }

                case ConnectionState.Closed:
                    {
                        await sqlCon.OpenAsync().ConfigureAwait(false);
                        return;
                    }

                default:
                    return;
            }
        }

        /// <summary>
        /// Opens the specified <see cref="SqlConnection"/> using impersonation.
        /// </summary>
        /// <param name="sqlCon">The SqlConnection to use.</param>
        /// <param name="impersonatedUsername">The username to be impersonated.</param>
        /// <returns>Cookie value that will be required to close the connection</returns>
        public static byte[] OpenImpersonated(this SqlConnection sqlCon, string impersonatedUsername)
        {
            if (sqlCon == null) throw new ArgumentNullException(nameof(sqlCon));

            // Open the underlying connection
            sqlCon.Reopen();

            // If username is null, empty or whitespace-only then don't try impersonate
            if (string.IsNullOrEmpty(impersonatedUsername))
                return null;

            // Set impersonation context using EXECUTE AS
            try
            {
                var user = impersonatedUsername;

                // We need to properly-quote the username in order to avoid injection attacks
                const string sql = "SELECT QUOTENAME(@username, N'''') AS [username];";
                using (var cmd = sqlCon.CreateCommand(sql, CommandType.Text))
                {
                    cmd.Parameters.AddWithValue("username", user);

                    var o = cmd.ExecuteScalar();

                    // Check that the result is non-empty
                    if (o == null)
                        throw new ArgumentNullException(nameof(impersonatedUsername));

                    user = o.ToString();
                    if (string.IsNullOrEmpty(user))
                        throw new ArgumentNullException(nameof(impersonatedUsername));
                }

                // If we successfully quoted the username, then execute the impersonation switch
                // Remember to use the cookie option so we can deterministically undo the impersonation
                // and put the connection back in the connection pool when we are done with it
                var sql1 = $@"
                    DECLARE @cookie VARBINARY(100);
                    EXECUTE AS LOGIN = {user} WITH COOKIE INTO @cookie;
                    SELECT @cookie;";
                using (var cmd = sqlCon.CreateCommand(sql1, CommandType.Text))
                {
                    // Do not use ExecuteNonQuery(), it doesn't like the COOKIE option
                    var oc = cmd.ExecuteScalar();

                    var cookie = (byte[])oc;
                    return cookie;
                }
            }
            catch
            {
                sqlCon.Close();
                throw;
            }
        }

        /// <summary>
        /// Opens the specified <see cref="SqlConnection"/> using impersonation.
        /// </summary>
        /// <param name="sqlCon">The SqlConnection to use.</param>
        /// <param name="impersonatedUsername">The username to be impersonated.</param>
        /// <returns>Cookie value that will be required to close the connection</returns>
        public static async Task<byte[]> OpenImpersonatedAsync(this SqlConnection sqlCon, string impersonatedUsername)
        {
            if (sqlCon == null) throw new ArgumentNullException(nameof(sqlCon));

            // Open the underlying connection
            await sqlCon.ReopenAsync().ConfigureAwait(false);

            // If username is null, empty or whitespace-only then don't try impersonate
            if (string.IsNullOrEmpty(impersonatedUsername))
                return null;

            // Set impersonation context using EXECUTE AS
            try
            {
                var user = impersonatedUsername;

                // We need to properly-quote the username in order to avoid injection attacks
                const string sql = "SELECT QUOTENAME(@username, N'''') AS [username];";
                using (var cmd = sqlCon.CreateCommand(sql, CommandType.Text))
                {
                    cmd.Parameters.AddWithValue("username", user);

                    var o = await cmd.ExecuteScalarAsync().ConfigureAwait(false);

                    // Check that the result is non-empty
                    if (o == null)
                        throw new ArgumentNullException(nameof(impersonatedUsername));

                    user = o.ToString();
                    if (string.IsNullOrEmpty(user))
                        throw new ArgumentNullException(nameof(impersonatedUsername));
                }

                // If we successfully quoted the username, then execute the impersonation switch
                // Remember to use the cookie option so we can deterministically undo the impersonation
                // and put the connection back in the connection pool when we are done with it
                var sql1 = $@"
                    DECLARE @cookie VARBINARY(100);
                    EXECUTE AS LOGIN = {user} WITH COOKIE INTO @cookie;
                    SELECT @cookie;";
                using (var cmd = sqlCon.CreateCommand(sql1, CommandType.Text))
                {
                    // Do not use ExecuteNonQuery(), it doesn't like the COOKIE option
                    var oc = await cmd.ExecuteScalarAsync().ConfigureAwait(false);

                    var cookie = (byte[])oc;
                    return cookie;
                }
            }
            catch
            {
                sqlCon.Close();
                throw;
            }
        }

        /// <summary>
        /// Close the specified <see cref="SqlConnection"/> and revert any impersonation.
        /// </summary>
        /// <param name="sqlCon">The SqlConnection to use.</param>
        /// <param name="cookie">The impersonation cookie returned from the Open() method</param>
        public static void CloseImpersonated(this SqlConnection sqlCon, byte[] cookie)
        {
            if (sqlCon == null) throw new ArgumentNullException(nameof(sqlCon));

            // Check that the underlying connection is still open
            if (sqlCon.State == ConnectionState.Open)
            {
                try
                {
                    // Only revert the cookie if it is provided
                    if (cookie != null && cookie.Length > 0) // @COOKIE is VARBINARY(100)
                    {
                        const string sql = "REVERT WITH COOKIE = @cookie;";
                        using (var cmd = sqlCon.CreateCommand(sql, CommandType.Text))
                        {
                            var p = cmd.Parameters.Add("cookie", SqlDbType.VarBinary, 100);
                            p.Value = cookie;

                            cmd.ExecuteNonQuery();
                        }
                    }
                }
                finally
                {
                    sqlCon.Close();
                }
            }
        }

        /// <summary>
        /// Close the specified <see cref="SqlConnection"/> and revert any impersonation.
        /// </summary>
        /// </summary>
        /// <param name="sqlCon">The SqlConnection to use.</param>
        /// <param name="cookie">The impersonation cookie returned from the Open() method</param>
        public static async Task CloseImpersonatedAsync(this SqlConnection sqlCon, byte[] cookie)
        {
            if (sqlCon == null) throw new ArgumentNullException(nameof(sqlCon));

            // Check that the underlying connection is still open
            if (sqlCon.State == ConnectionState.Open)
            {
                try
                {
                    // Only revert the cookie if it is provided
                    if (cookie != null && cookie.Length > 0)
                    {
                        const string sql = "REVERT WITH COOKIE = @cookie;";
                        using (var cmd = sqlCon.CreateCommand(sql, CommandType.Text))
                        {
                            var p = cmd.Parameters.Add("cookie", SqlDbType.VarBinary, 100); // @COOKIE is VARBINARY(100)
                            p.Value = cookie;

                            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                        }
                    }
                }
                finally
                {
                    sqlCon.Close();
                }
            }
        }

        #endregion
    }
}
