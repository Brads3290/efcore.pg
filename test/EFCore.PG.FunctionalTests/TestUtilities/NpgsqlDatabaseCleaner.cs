﻿using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.EntityFrameworkCore.Scaffolding.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.Logging;
using Npgsql.EntityFrameworkCore.PostgreSQL.Diagnostics.Internal;
using Npgsql.EntityFrameworkCore.PostgreSQL.Scaffolding.Internal;
using Npgsql.EntityFrameworkCore.PostgreSQL.Storage.Internal;

namespace Npgsql.EntityFrameworkCore.PostgreSQL.TestUtilities
{
    public class NpgsqlDatabaseCleaner : RelationalDatabaseCleaner
    {
        readonly NpgsqlSqlGenerationHelper _sqlGenerationHelper;

        public NpgsqlDatabaseCleaner()
            => _sqlGenerationHelper = new NpgsqlSqlGenerationHelper(new RelationalSqlGenerationHelperDependencies());

        protected override IDatabaseModelFactory CreateDatabaseModelFactory(ILoggerFactory loggerFactory)
            => new NpgsqlDatabaseModelFactory(
                new DiagnosticsLogger<DbLoggerCategory.Scaffolding>(
                    loggerFactory,
                    new LoggingOptions(),
                    new DiagnosticListener("Fake"),
                    new NpgsqlLoggingDefinitions()));

        protected override bool AcceptIndex(DatabaseIndex index)
            => false;

        const string GetExtensions = @"
SELECT name FROM pg_available_extensions WHERE installed_version IS NOT NULL AND name <> 'plpgsql'";

        public override void Clean(DatabaseFacade facade)
        {
            // The following is somewhat hacky
            // PostGIS creates some system tables (e.g. spatial_ref_sys) which can't be dropped until the extension
            // is dropped. But our tests create some user tables which depend on PostGIS. So we clean out PostGIS
            // and all tables that depend on it (CASCADE) before the database model is built.
            var creator = facade.GetService<IRelationalDatabaseCreator>();
            var connection = facade.GetService<IRelationalConnection>();
            if (creator.Exists())
            {
                connection.Open();
                try
                {
                    var dbConnection = (NpgsqlConnection)connection.DbConnection;

                    List<string> extensions;
                    using (var cmd = new NpgsqlCommand(GetExtensions, dbConnection))
                    using (var reader = cmd.ExecuteReader())
                        extensions = reader.Cast<DbDataRecord>().Select(r => r.GetString(0)).ToList();

                    var dropExtensionsSql = string.Join("", extensions.Select(e => $"DROP EXTENSION \"{e}\" CASCADE;"));
                    using (var cmd = new NpgsqlCommand(dropExtensionsSql, dbConnection))
                        cmd.ExecuteNonQuery();
                }
                finally
                {
                    connection.Close();
                }
            }

            base.Clean(facade);
        }

        protected override string BuildCustomSql(DatabaseModel databaseModel)
            // Some extensions create tables (e.g. PostGIS), so we must drop them first.
            => databaseModel.Npgsql()
                            .PostgresExtensions
                            .Select(e => _sqlGenerationHelper.DelimitIdentifier(e.Name, e.Schema))
                            .Aggregate(new StringBuilder(),
                                (builder, s) => builder.Append("DROP EXTENSION ").Append(s).Append(";"),
                                builder => builder.ToString());

        protected override string BuildCustomEndingSql(DatabaseModel databaseModel)
            => databaseModel.Npgsql()
                            .PostgresEnums
                            .Select(e => _sqlGenerationHelper.DelimitIdentifier(e.Name, e.Schema))
                            .Aggregate(new StringBuilder(),
                                (builder, s) => builder.Append("DROP TYPE ").Append(s).Append(" CASCADE;"),
                                builder => builder.ToString());
    }
}
