﻿// -----------------------------------------------------------------------
//  <copyright file="RavenCoreTestBase.cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Raven.Client.Connection;
using Raven.Client.Document;
using Raven.Client.Extensions;
using Raven.Server;
using Xunit;

namespace Raven.Tests.Core
{
	public class RavenCoreTestBase : IUseFixture<TestServerFixture>, IDisposable
	{
		private readonly List<string> createdDbs = new List<string>();
		private readonly List<DocumentStore> createdStores = new List<DocumentStore>();

		protected RavenDbServer Server { get; private set; }

		public void SetFixture(TestServerFixture coreTestFixture)
		{
			Server = coreTestFixture.Server;
		}

		protected DocumentStore GetDocumentStore([CallerMemberName] string databaseName = null, string dbSuffixIdentifier = null)
		{
			var serverClient = (ServerClient)Server.DocumentStore.DatabaseCommands.ForSystemDatabase();

			serverClient.ForceReadFromMaster();

			var doc = MultiDatabase.CreateDatabaseDocument(databaseName);

			if (serverClient.Get(doc.Id) != null)
				throw new InvalidOperationException(string.Format("Database '{0}' already exists", databaseName));

			serverClient.GlobalAdmin.CreateDatabase(doc);

			createdDbs.Add(databaseName);

			var documentStore = new DocumentStore
			{
				HttpMessageHandler = Server.DocumentStore.HttpMessageHandler,
				Url = "http://localhost",
				DefaultDatabase = databaseName
			};
			documentStore.Initialize();

			createdStores.Add(documentStore);

			return documentStore;
		}

		public static void WaitForIndexing(DocumentStore store, string db = null, TimeSpan? timeout = null)
		{
			var databaseCommands = store.DatabaseCommands;
			if (db != null)
				databaseCommands = databaseCommands.ForDatabase(db);
			var spinUntil = SpinWait.SpinUntil(() => databaseCommands.GetStatistics().StaleIndexes.Length == 0, timeout ?? TimeSpan.FromSeconds(20));

			Assert.True(spinUntil, "Indexes took took long to become unstale");
		}

		public virtual void Dispose()
		{
			foreach (var store in createdStores)
			{
				store.Dispose();
			}

			foreach (var db in createdDbs)
			{
				Server.DocumentStore.DatabaseCommands.GlobalAdmin.DeleteDatabase(db, hardDelete: true);
			}
		}
	}
}