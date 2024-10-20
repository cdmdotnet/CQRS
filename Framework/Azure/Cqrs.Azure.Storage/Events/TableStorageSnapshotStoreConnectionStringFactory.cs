#region Copyright
// // -----------------------------------------------------------------------
// // <copyright company="Chinchilla Software Limited">
// // 	Copyright Chinchilla Software Limited. All rights reserved.
// // </copyright>
// // -----------------------------------------------------------------------
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using Cqrs.Configuration;
using Chinchilla.Logging;
using Cqrs.Exceptions;
using Cqrs.Snapshots;

namespace Cqrs.Azure.Storage.Events
{
	/// <summary>
	/// A factory for getting connection strings and container names for <see cref="ISnapshotStore"/> access.
	/// This factory supports reading and writing from separate storage accounts. Specifically you can have as many different storage accounts as you want to configure when writing.
	/// This allows for manual mirroring of data while reading from the fastest/closest location possible.
	/// </summary>
	public class TableStorageSnapshotStoreConnectionStringFactory
		: TableStorageEventStoreConnectionStringFactory
		, ITableStorageSnapshotStoreConnectionStringFactory
	{
		/// <summary>
		/// The name of the app setting in <see cref="IConfigurationManager"/> that will have the connection string of the readable storage account if using a separate storage account for reads and writes.
		/// </summary>
		public static string TableStorageReadableSnapshotStoreConnectionStringKey = "Cqrs.Azure.TableStorage.SnapshotStore.Read.ConnectionStringName";

		/// <summary>
		/// The name of the app setting in <see cref="IConfigurationManager"/> that will have the connection string of the writeable storage account if using a separate storage account for reads and writes.
		/// This value gets appended with a ".1", ".2" etc allowing you to write to as many different locations as possible.
		/// </summary>
		public static string TableStorageWritableSnapshotStoreConnectionStringKey = "Cqrs.Azure.TableStorage.SnapshotStore.Write.ConnectionStringName";

		/// <summary>
		/// The name of the app setting in <see cref="IConfigurationManager"/> that will have the connection string if using a single storage account for both reads and writes.
		/// </summary>
		public static string TableStorageSnapshotStoreConnectionStringKey = "Cqrs.Azure.TableStorage.SnapshotStore.ConnectionStringName";

		/// <summary>
		/// The name of the app setting in <see cref="IConfigurationManager"/> that will have the base name of the container used.
		/// </summary>
		public static string TableStorageSnapshotBaseContainerNameKey = "Cqrs.Azure.TableStorage.SnapshotStore.BaseContainerName";

		/// <summary>
		/// Instantiates a new instance of <see cref="TableStorageSnapshotStoreConnectionStringFactory"/>.
		/// </summary>
		public TableStorageSnapshotStoreConnectionStringFactory(IConfigurationManager configurationManager, ILogger logger)
			: base(configurationManager, logger)
		{
		}

		/// <summary>
		/// Gets all writeable connection strings. If using a single storage account, then <see cref="TableStorageSnapshotStoreConnectionStringKey"/> will most likely be returned.
		/// If a value for <see cref="TableStorageWritableSnapshotStoreConnectionStringKey"/> is found, it will append ".1", ".2" etc returning any additionally found connection string values in <see cref="ConfigurationManager"/>.
		/// </summary>
		public override IEnumerable<string> GetWritableConnectionStrings()
		{
			Logger.LogDebug("Getting blob storage writeable connection strings", "TableStorageSnapshotStoreConnectionStringFactory\\GetWritableConnectionStrings");
			try
			{
				var collection = new List<string> ();

				string tableStorageWritableSnapshotStoreConnectionString = ConfigurationManager.GetConnectionString(TableStorageWritableSnapshotStoreConnectionStringKey) ?? ConfigurationManager.GetSetting(TableStorageWritableSnapshotStoreConnectionStringKey);
				if (string.IsNullOrWhiteSpace(tableStorageWritableSnapshotStoreConnectionString))
				{
					Logger.LogDebug($"No application setting named '{TableStorageWritableSnapshotStoreConnectionStringKey}' in the configuration file.", "TableStorageSnapshotStoreConnectionStringFactory\\GetWritableConnectionStrings");
					tableStorageWritableSnapshotStoreConnectionString = ConfigurationManager.GetConnectionString(TableStorageSnapshotStoreConnectionStringKey) ?? ConfigurationManager.GetSetting(TableStorageSnapshotStoreConnectionStringKey);
				}

				int writeIndex = 1;
				while (!string.IsNullOrWhiteSpace(tableStorageWritableSnapshotStoreConnectionString))
				{
					collection.Add(tableStorageWritableSnapshotStoreConnectionString);

					tableStorageWritableSnapshotStoreConnectionString = ConfigurationManager.GetConnectionString($"{TableStorageWritableSnapshotStoreConnectionStringKey}.{writeIndex}") ?? ConfigurationManager.GetSetting($"{TableStorageWritableSnapshotStoreConnectionStringKey}.{writeIndex}");

					writeIndex++;
				}

				if (!collection.Any())
					throw new MissingConnectionStringException(TableStorageSnapshotStoreConnectionStringKey);

				return collection;
			}
			catch (NullReferenceException exception)
			{
				throw new MissingConnectionStringException(TableStorageSnapshotStoreConnectionStringKey, exception);
			}
			finally
			{
				Logger.LogDebug("Getting blob storage writeable connection string... Done", "TableStorageSnapshotStoreConnectionStringFactory\\GetWritableConnectionStrings");
			}
		}

		/// <summary>
		/// Gets the readable connection string. If using a single storage account, then <see cref="TableStorageSnapshotStoreConnectionStringKey"/> will most likely be returned.
		/// If a value for <see cref="TableStorageReadableSnapshotStoreConnectionStringKey"/> is found, that will be returned instead.
		/// </summary>
		public override string GetReadableConnectionString()
		{
			Logger.LogDebug("Getting blob storage readable connection strings", "TableStorageSnapshotStoreConnectionStringFactory\\GetReadableConnectionStrings");
			try
			{
				string tableStorageWritableSnapshotStoreConnectionString = ConfigurationManager.GetConnectionString(TableStorageReadableSnapshotStoreConnectionStringKey) ?? ConfigurationManager.GetSetting(TableStorageReadableSnapshotStoreConnectionStringKey);
				if (string.IsNullOrWhiteSpace(tableStorageWritableSnapshotStoreConnectionString))
				{
					Logger.LogDebug($"No application setting named '{TableStorageReadableSnapshotStoreConnectionStringKey}' in the configuration file.", "TableStorageSnapshotStoreConnectionStringFactory\\GetReadableConnectionStrings");
					tableStorageWritableSnapshotStoreConnectionString = ConfigurationManager.GetConnectionString(TableStorageSnapshotStoreConnectionStringKey) ?? ConfigurationManager.GetSetting(TableStorageSnapshotStoreConnectionStringKey);
				}

				if (string.IsNullOrWhiteSpace(tableStorageWritableSnapshotStoreConnectionString))
					throw new MissingConnectionStringException(TableStorageSnapshotStoreConnectionStringKey);

				return tableStorageWritableSnapshotStoreConnectionString;
			}
			catch (NullReferenceException exception)
			{
				throw new MissingConnectionStringException(TableStorageSnapshotStoreConnectionStringKey, exception);
			}
			finally
			{
				Logger.LogDebug("Getting blob storage readable connection string... Done", "TableStorageSnapshotStoreConnectionStringFactory\\GetReadableConnectionStrings");
			}
		}

		/// <summary>
		/// Returns the name of the base contain to be used.
		/// This will be the value from <see cref="ConfigurationManager"/> keyed <see cref="TableStorageSnapshotBaseContainerNameKey"/>.
		/// </summary>
		public override string GetBaseContainerName()
		{
			Logger.LogDebug("Getting blob storage base container name", "TableStorageSnapshotStoreConnectionStringFactory\\GetBaseContainerName");
			try
			{
				string result = ConfigurationManager.GetSetting(TableStorageSnapshotBaseContainerNameKey);

				if (string.IsNullOrWhiteSpace(result))
					throw new NullReferenceException();

				return result;
			}
			catch (NullReferenceException exception)
			{
				throw new MissingApplicationSettingException(TableStorageSnapshotBaseContainerNameKey, exception);
			}
			finally
			{
				Logger.LogDebug("Getting blob storage base container name... Done", "TableStorageSnapshotStoreConnectionStringFactory\\GetBaseContainerName");
			}
		}
	}
}