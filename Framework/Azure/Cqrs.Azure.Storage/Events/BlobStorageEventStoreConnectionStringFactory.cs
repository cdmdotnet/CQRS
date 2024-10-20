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
using Cqrs.Events;
using Cqrs.Exceptions;

namespace Cqrs.Azure.Storage.Events
{
	/// <summary>
	/// A factory for getting connection strings and container names for <see cref="IEventStore{TAuthenticationToken}"/> access.
	/// This factory supports reading and writing from separate storage accounts. Specifically you can have as many different storage accounts as you want to configure when writing.
	/// This allows for manual mirroring of data while reading from the fastest/closest location possible.
	/// </summary>
	public class BlobStorageEventStoreConnectionStringFactory
		: IBlobStorageStoreConnectionStringFactory
	{
		/// <summary>
		/// The name of the app setting in <see cref="IConfigurationManager"/> that will have the connection string of the readable storage account if using a separate storage account for reads and writes.
		/// </summary>
		public static string BlobStorageReadableEventStoreConnectionStringKey = "Cqrs.Azure.BlobStorage.EventStore.Read.ConnectionStringName";

		/// <summary>
		/// The name of the app setting in <see cref="IConfigurationManager"/> that will have the connection string of the writeable storage account if using a separate storage account for reads and writes.
		/// This value gets appended with a ".1", ".2" etc allowing you to write to as many different locations as possible.
		/// </summary>
		public static string BlobStorageWritableEventStoreConnectionStringKey = "Cqrs.Azure.BlobStorage.EventStore.Write.ConnectionStringName";

		/// <summary>
		/// The name of the app setting in <see cref="IConfigurationManager"/> that will have the connection string if using a single storage account for both reads and writes.
		/// </summary>
		public static string BlobStorageEventStoreConnectionStringKey = "Cqrs.Azure.BlobStorage.EventStore.ConnectionStringName";

		/// <summary>
		/// The name of the app setting in <see cref="IConfigurationManager"/> that will have the base name of the container used.
		/// </summary>
		public static string BlobStorageBaseContainerNameKey = "Cqrs.Azure.BlobStorage.EventStore.BaseContainerName";

		/// <summary>
		/// Gets or sets the <see cref="IConfigurationManager"/>.
		/// </summary>
		protected IConfigurationManager ConfigurationManager { get; private set; }

		/// <summary>
		/// Gets or sets the <see cref="ILogger"/>.
		/// </summary>
		protected ILogger Logger { get; private set; }

		/// <summary>
		/// Instantiates a new instance of <see cref="BlobStorageEventStoreConnectionStringFactory"/>.
		/// </summary>
		public BlobStorageEventStoreConnectionStringFactory(IConfigurationManager configurationManager, ILogger logger)
		{
			ConfigurationManager = configurationManager;
			Logger = logger;
		}

		/// <summary>
		/// Gets all writeable connection strings. If using a single storage account, then <see cref="BlobStorageEventStoreConnectionStringKey"/> will most likely be returned.
		/// If a value for <see cref="BlobStorageWritableEventStoreConnectionStringKey"/> is found, it will append ".1", ".2" etc returning any additionally found connection string values in <see cref="ConfigurationManager"/>.
		/// </summary>
		public virtual IEnumerable<string> GetWritableConnectionStrings()
		{
			Logger.LogDebug("Getting blob storage writeable connection strings", "BlobStorageEventStoreConnectionStringFactory\\GetWritableConnectionStrings");
			try
			{
				var collection = new List<string> ();

				string blobStorageWritableEventStoreConnectionString = ConfigurationManager.GetConnectionString(BlobStorageWritableEventStoreConnectionStringKey) ?? ConfigurationManager.GetSetting(BlobStorageWritableEventStoreConnectionStringKey);
				if (string.IsNullOrWhiteSpace(blobStorageWritableEventStoreConnectionString))
				{
					Logger.LogDebug($"No application setting named '{BlobStorageWritableEventStoreConnectionStringKey}' in the configuration file.", "BlobStorageEventStoreConnectionStringFactory\\GetWritableConnectionStrings");
					blobStorageWritableEventStoreConnectionString = ConfigurationManager.GetConnectionString(BlobStorageEventStoreConnectionStringKey) ?? ConfigurationManager.GetSetting(BlobStorageEventStoreConnectionStringKey);
				}

				int writeIndex = 1;
				while (!string.IsNullOrWhiteSpace(blobStorageWritableEventStoreConnectionString))
				{
					collection.Add(blobStorageWritableEventStoreConnectionString);

					blobStorageWritableEventStoreConnectionString = ConfigurationManager.GetConnectionString($"{BlobStorageWritableEventStoreConnectionStringKey}.{writeIndex}") ?? ConfigurationManager.GetSetting($"{BlobStorageWritableEventStoreConnectionStringKey}.{writeIndex}");

					writeIndex++;
				}

				if (!collection.Any())
					throw new MissingConnectionStringException(BlobStorageEventStoreConnectionStringKey);

				return collection;
			}
			catch (NullReferenceException exception)
			{
				throw new MissingConnectionStringException(BlobStorageEventStoreConnectionStringKey, exception);
			}
			finally
			{
				Logger.LogDebug("Getting blob storage writeable connection string... Done", "BlobStorageEventStoreConnectionStringFactory\\GetWritableConnectionStrings");
			}
		}

		/// <summary>
		/// Gets the readable connection string. If using a single storage account, then <see cref="BlobStorageEventStoreConnectionStringKey"/> will most likely be returned.
		/// If a value for <see cref="BlobStorageReadableEventStoreConnectionStringKey"/> is found, that will be returned instead.
		/// </summary>
		public virtual string GetReadableConnectionString()
		{
			Logger.LogDebug("Getting blob storage readable connection strings", "BlobStorageEventStoreConnectionStringFactory\\GetReadableConnectionStrings");
			try
			{
				string blobStorageWritableEventStoreConnectionString = ConfigurationManager.GetConnectionString(BlobStorageReadableEventStoreConnectionStringKey) ?? ConfigurationManager.GetSetting(BlobStorageReadableEventStoreConnectionStringKey);
				if (string.IsNullOrWhiteSpace(blobStorageWritableEventStoreConnectionString))
				{
					Logger.LogDebug($"No application setting named '{BlobStorageReadableEventStoreConnectionStringKey}' in the configuration file.", "BlobStorageEventStoreConnectionStringFactory\\GetReadableConnectionStrings");
					blobStorageWritableEventStoreConnectionString = ConfigurationManager.GetConnectionString(BlobStorageEventStoreConnectionStringKey) ?? ConfigurationManager.GetSetting(BlobStorageEventStoreConnectionStringKey);
				}

				if (string.IsNullOrWhiteSpace(blobStorageWritableEventStoreConnectionString))
					throw new MissingConnectionStringException(BlobStorageEventStoreConnectionStringKey);

				return blobStorageWritableEventStoreConnectionString;
			}
			catch (NullReferenceException exception)
			{
				throw new MissingConnectionStringException(BlobStorageEventStoreConnectionStringKey, exception);
			}
			finally
			{
				Logger.LogDebug("Getting blob storage readable connection string... Done", "BlobStorageEventStoreConnectionStringFactory\\GetReadableConnectionStrings");
			}
		}

		/// <summary>
		/// Returns the name of the base contain to be used.
		/// This will be the value from <see cref="ConfigurationManager"/> keyed <see cref="BlobStorageBaseContainerNameKey"/>.
		/// </summary>
		public virtual string GetBaseContainerName()
		{
			Logger.LogDebug("Getting blob storage base container name", "BlobStorageEventStoreConnectionStringFactory\\GetBaseContainerName");
			try
			{
				string result = ConfigurationManager.GetSetting(BlobStorageBaseContainerNameKey);

				if (string.IsNullOrWhiteSpace(result))
					throw new MissingApplicationSettingException(BlobStorageBaseContainerNameKey);

				return result;
			}
			catch (NullReferenceException exception)
			{
				throw new MissingApplicationSettingException(BlobStorageBaseContainerNameKey, exception);
			}
			finally
			{
				Logger.LogDebug("Getting blob storage base container name... Done", "BlobStorageEventStoreConnectionStringFactory\\GetBaseContainerName");
			}
		}
	}
}