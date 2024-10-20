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
	public class BlobStorageSnapshotStoreConnectionStringFactory
		: BlobStorageEventStoreConnectionStringFactory
		, IBlobStorageSnapshotStoreConnectionStringFactory
	{
		/// <summary>
		/// The name of the app setting in <see cref="IConfigurationManager"/> that will have the connection string of the readable storage account if using a separate storage account for reads and writes.
		/// </summary>
		public static string BlobStorageReadableSnapshotStoreConnectionStringKey = "Cqrs.Azure.BlobStorage.SnapshotStore.Read.ConnectionStringName";

		/// <summary>
		/// The name of the app setting in <see cref="IConfigurationManager"/> that will have the connection string of the writeable storage account if using a separate storage account for reads and writes.
		/// This value gets appended with a ".1", ".2" etc allowing you to write to as many different locations as possible.
		/// </summary>
		public static string BlobStorageWritableSnapshotStoreConnectionStringKey = "Cqrs.Azure.BlobStorage.SnapshotStore.Write.ConnectionStringName";

		/// <summary>
		/// The name of the app setting in <see cref="IConfigurationManager"/> that will have the connection string if using a single storage account for both reads and writes.
		/// </summary>
		public static string BlobStorageSnapshotStoreConnectionStringKey = "Cqrs.Azure.BlobStorage.SnapshotStore.ConnectionStringName";

		/// <summary>
		/// The name of the app setting in <see cref="IConfigurationManager"/> that will have the base name of the container used.
		/// </summary>
		public static string BlobStorageSnapshotBaseContainerNameKey = "Cqrs.Azure.BlobStorage.SnapshotStore.BaseContainerName";

		/// <summary>
		/// Instantiates a new instance of <see cref="BlobStorageSnapshotStoreConnectionStringFactory"/>.
		/// </summary>
		public BlobStorageSnapshotStoreConnectionStringFactory(IConfigurationManager configurationManager, ILogger logger)
			: base(configurationManager, logger)
		{
		}

		/// <summary>
		/// Gets all writeable connection strings. If using a single storage account, then <see cref="BlobStorageSnapshotStoreConnectionStringKey"/> will most likely be returned.
		/// If a value for <see cref="BlobStorageWritableSnapshotStoreConnectionStringKey"/> is found, it will append ".1", ".2" etc returning any additionally found connection string values in <see cref="ConfigurationManager"/>.
		/// </summary>
		public override IEnumerable<string> GetWritableConnectionStrings()
		{
			Logger.LogDebug("Getting blob storage writeable connection strings", "BlobStorageSnapshotStoreConnectionStringFactory\\GetWritableConnectionStrings");
			try
			{
				var collection = new List<string> ();

				string blobStorageWritableSnapshotStoreConnectionString = ConfigurationManager.GetConnectionString(BlobStorageWritableSnapshotStoreConnectionStringKey) ?? ConfigurationManager.GetSetting(BlobStorageWritableSnapshotStoreConnectionStringKey);
				if (string.IsNullOrWhiteSpace(blobStorageWritableSnapshotStoreConnectionString))
				{
					Logger.LogDebug($"No application setting named '{BlobStorageWritableSnapshotStoreConnectionStringKey}' in the configuration file.", "BlobStorageSnapshotStoreConnectionStringFactory\\GetWritableConnectionStrings");
					blobStorageWritableSnapshotStoreConnectionString = ConfigurationManager.GetConnectionString(BlobStorageSnapshotStoreConnectionStringKey) ?? ConfigurationManager.GetSetting(BlobStorageSnapshotStoreConnectionStringKey);
				}

				int writeIndex = 1;
				while (!string.IsNullOrWhiteSpace(blobStorageWritableSnapshotStoreConnectionString))
				{
					collection.Add(blobStorageWritableSnapshotStoreConnectionString);

					blobStorageWritableSnapshotStoreConnectionString = ConfigurationManager.GetConnectionString($"{BlobStorageWritableSnapshotStoreConnectionStringKey}.{writeIndex}") ?? ConfigurationManager.GetSetting($"{BlobStorageWritableSnapshotStoreConnectionStringKey}.{writeIndex}");

					writeIndex++;
				}

				if (!collection.Any())
					throw new MissingConnectionStringException(BlobStorageSnapshotStoreConnectionStringKey);

				return collection;
			}
			catch (NullReferenceException exception)
			{
				throw new MissingConnectionStringException(BlobStorageSnapshotStoreConnectionStringKey, exception);
			}
			finally
			{
				Logger.LogDebug("Getting blob storage writeable connection string... Done", "BlobStorageSnapshotStoreConnectionStringFactory\\GetWritableConnectionStrings");
			}
		}

		/// <summary>
		/// Gets the readable connection string. If using a single storage account, then <see cref="BlobStorageSnapshotStoreConnectionStringKey"/> will most likely be returned.
		/// If a value for <see cref="BlobStorageReadableSnapshotStoreConnectionStringKey"/> is found, that will be returned instead.
		/// </summary>
		public override string GetReadableConnectionString()
		{
			Logger.LogDebug("Getting blob storage readable connection strings", "BlobStorageSnapshotStoreConnectionStringFactory\\GetReadableConnectionStrings");
			try
			{
				string blobStorageWritableSnapshotStoreConnectionString = ConfigurationManager.GetConnectionString(BlobStorageReadableSnapshotStoreConnectionStringKey) ?? ConfigurationManager.GetSetting(BlobStorageReadableSnapshotStoreConnectionStringKey);
				if (string.IsNullOrWhiteSpace(blobStorageWritableSnapshotStoreConnectionString))
				{
					Logger.LogDebug($"No application setting named '{BlobStorageReadableSnapshotStoreConnectionStringKey}' in the configuration file.", "BlobStorageSnapshotStoreConnectionStringFactory\\GetReadableConnectionStrings");
					blobStorageWritableSnapshotStoreConnectionString = ConfigurationManager.GetConnectionString(BlobStorageSnapshotStoreConnectionStringKey) ?? ConfigurationManager.GetSetting(BlobStorageSnapshotStoreConnectionStringKey);
				}

				if (string.IsNullOrWhiteSpace(blobStorageWritableSnapshotStoreConnectionString))
					throw new MissingConnectionStringException(BlobStorageSnapshotStoreConnectionStringKey);

				return blobStorageWritableSnapshotStoreConnectionString;
			}
			catch (NullReferenceException exception)
			{
				throw new MissingConnectionStringException(BlobStorageSnapshotStoreConnectionStringKey, exception);
			}
			finally
			{
				Logger.LogDebug("Getting blob storage readable connection string... Done", "BlobStorageSnapshotStoreConnectionStringFactory\\GetReadableConnectionStrings");
			}
		}

		/// <summary>
		/// Returns the name of the base contain to be used.
		/// This will be the value from <see cref="ConfigurationManager"/> keyed <see cref="BlobStorageSnapshotBaseContainerNameKey"/>.
		/// </summary>
		public override string GetBaseContainerName()
		{
			Logger.LogDebug("Getting blob storage base container name", "BlobStorageSnapshotStoreConnectionStringFactory\\GetBaseContainerName");
			try
			{
				string result = ConfigurationManager.GetSetting(BlobStorageSnapshotBaseContainerNameKey);

				if (string.IsNullOrWhiteSpace(result))
					throw new NullReferenceException();

				return result;
			}
			catch (NullReferenceException exception)
			{
				throw new MissingApplicationSettingException(BlobStorageSnapshotBaseContainerNameKey, exception);
			}
			finally
			{
				Logger.LogDebug("Getting blob storage base container name... Done", "BlobStorageSnapshotStoreConnectionStringFactory\\GetBaseContainerName");
			}
		}
	}
}