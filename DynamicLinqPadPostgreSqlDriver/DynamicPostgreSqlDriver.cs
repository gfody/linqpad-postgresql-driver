﻿using LINQPad.Extensibility.DataContext;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Npgsql;
using System.Threading;
using System.Reflection.Emit;
using System.IO;
using System.Data;
using DynamicLinqPadPostgreSqlDriver.UI;
using DynamicLinqPadPostgreSqlDriver.Shared.Extensions;
using LinqToDB.DataProvider;
using LinqToDB.DataProvider.PostgreSQL;
using System;

namespace DynamicLinqPadPostgreSqlDriver
{
   public class DynamicPostgreSqlDriver : DynamicDataContextDriver
   {
      public override string Author => "Frederik Knust";

      public override string Name => "PostgreSQL (LINQ to DB)";

      public override string GetConnectionDescription(IConnectionInfo cxInfo)
      {
         if (!string.IsNullOrWhiteSpace(cxInfo.DisplayName))
            return cxInfo.DisplayName;

         if (string.IsNullOrWhiteSpace(cxInfo.DatabaseInfo.CustomCxString))
            return $"{cxInfo.DatabaseInfo.Server} - {cxInfo.DatabaseInfo.Database}";

         return "PostgreSql";
      }

      public override IDbConnection GetIDbConnection(IConnectionInfo cxInfo)
      {
         var connectionString = cxInfo.GetPostgreSqlConnectionString();

         var connection = new NpgsqlConnection(connectionString);
         return connection;
      }

      public override IEnumerable<string> GetAssembliesToAdd(IConnectionInfo cxInfo)
      {
         yield return "linq2db.dll";
         yield return "Npgsql.dll";
      }

      public override IEnumerable<string> GetNamespacesToAdd(IConnectionInfo cxInfo)
      {
         yield return "System.Net"; // for IPAddress type
         yield return "System.Net.NetworkInformation"; // for PhysicalAddress type
         yield return "LinqToDB";
         yield return "NpgsqlTypes";
      }

      public override void ClearConnectionPools(IConnectionInfo cxInfo)
      {
         NpgsqlConnection.ClearAllPools();
      }

      public override ParameterDescriptor[] GetContextConstructorParameters(IConnectionInfo cxInfo)
      {
         var dataProviderParameter = new ParameterDescriptor("dataProvider", "LinqToDB.DataProvider.IDataProvider");
         var connectionStringParameter = new ParameterDescriptor("connectionString", "System.String");

         return new[] { dataProviderParameter, connectionStringParameter };
      }

      public override object[] GetContextConstructorArguments(IConnectionInfo cxInfo)
      {
         return new object[] { new PostgreSQLDataProvider(), cxInfo.GetPostgreSqlConnectionString() };
      }

      public override List<ExplorerItem> GetSchemaAndBuildAssembly(IConnectionInfo cxInfo, AssemblyName assemblyToBuild, ref string nameSpace, ref string typeName)
      {
         var fileName = Path.GetFileName(assemblyToBuild.CodeBase);
         var directory = Path.GetDirectoryName(assemblyToBuild.CodeBase);

         var assemblyBuilder = Thread.GetDomain().DefineDynamicAssembly(assemblyToBuild, AssemblyBuilderAccess.RunAndSave, directory);
         var moduleBuilder = assemblyBuilder.DefineDynamicModule(assemblyToBuild.Name, fileName);

         var dataContextTypeBuilder = moduleBuilder.DefineType(string.Format("{0}.{1}", nameSpace, typeName), TypeAttributes.Public, typeof(TypedDataContextBase));

         var explorerItems = new List<ExplorerItem>();

         using (var connection = GetIDbConnection(cxInfo))
         {
            connection.Open();

            var manager = new DatabaseObjectProviderManager(cxInfo, moduleBuilder, connection, nameSpace);
            foreach (var provider in manager.DatabaseObjectProviders.OrderBy(x => x.Priority))
            {
                 explorerItems.Add(provider.EmitCodeAndGetExplorerItemTree(dataContextTypeBuilder));
            }
         }

         // fetch the base constructor which shall be called by the new constructor
         var baseConstructor = typeof(TypedDataContextBase).GetConstructor(new[] { typeof(IDataProvider), typeof(string) });

         // create the typed data context's constructor
         var dataContextConstructor = dataContextTypeBuilder.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, new[] { typeof(IDataProvider), typeof(string) });
         var ilGenerator = dataContextConstructor.GetILGenerator();

         ilGenerator.Emit(OpCodes.Ldarg_0);
         ilGenerator.Emit(OpCodes.Ldarg_1);
         ilGenerator.Emit(OpCodes.Ldarg_2);

         // call the base constructor
         ilGenerator.Emit(OpCodes.Call, baseConstructor);

         ilGenerator.Emit(OpCodes.Ret);

         dataContextTypeBuilder.CreateType();

         assemblyBuilder.Save(fileName);

         return explorerItems;
      }

      [Obsolete]
      public override bool ShowConnectionDialog(IConnectionInfo cxInfo, bool isNewConnection)
      {
         return UIHelper.ShowConfigurationWindow(cxInfo);
      }
   }
}
