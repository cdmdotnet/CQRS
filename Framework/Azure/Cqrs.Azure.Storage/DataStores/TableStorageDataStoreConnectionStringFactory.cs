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
using System.Text;
using Cqrs.Configuration;
using Chinchilla.Logging;
using Cqrs.DataStores;
using Cqrs.Exceptions;

namespace Cqrs.Azure.Storage.DataStores
{
	/// <summary>
	/// A factory for getting connection strings and container names for <see cref="IDataStore{TData}"/> access.
	/// This factory supports reading and writing from separate storage accounts. Specifically you can have as many different storage accounts as you want to configure when writing.
	/// This allows for manual mirroring of data while reading from the fastest/closest location possible.
	/// </summary>
	public class TableStorageDataStoreConnectionStringFactory : ITableStorageDataStoreConnectionStringFactory
	{
		/// <summary>
		/// The name of the app setting in <see cref="IConfigurationManager"/> that will have the connection string of the readable storage account if using a separate storage account for reads and writes.
		/// </summary>
		public static string TableStorageReadableDataStoreConnectionStringKey = "Cqrs.Azure.TableStorage.DataStore.Read.ConnectionStringName";

		/// <summary>
		/// The name of the app setting in <see cref="IConfigurationManager"/> that will have the connection string of the writeable storage account if using a separate storage account for reads and writes.
		/// This value gets appended with a ".1", ".2" etc allowing you to write to as many different locations as possible.
		/// </summary>
		public static string TableStorageWritableDataStoreConnectionStringKey = "Cqrs.Azure.TableStorage.DataStore.Write.ConnectionStringName";

		/// <summary>
		/// The name of the app setting in <see cref="IConfigurationManager"/> that will have the connection string if using a single storage account for both reads and writes.
		/// </summary>
		public static string TableStorageDataStoreConnectionStringKey = "Cqrs.Azure.TableStorage.DataStore.ConnectionStringName";

		/// <summary>
		/// The name of the app setting in <see cref="IConfigurationManager"/> that will have the base name of the container used.
		/// </summary>
		public static string TableStorageBaseContainerNameKey = "Cqrs.Azure.TableStorage.DataStore.BaseContainerName";

		/// <summary>
		/// Gets or sets the <see cref="IConfigurationManager"/>.
		/// </summary>
		protected IConfigurationManager ConfigurationManager { get; private set; }

		/// <summary>
		/// Gets or sets the <see cref="ILogger"/>.
		/// </summary>
		protected ILogger Logger { get; private set; }

		/// <summary>
		/// Instantiates a new instance of <see cref="TableStorageDataStoreConnectionStringFactory"/>.
		/// </summary>
		public TableStorageDataStoreConnectionStringFactory(IConfigurationManager configurationManager, ILogger logger)
		{
			ConfigurationManager = configurationManager;
			Logger = logger;
		}

		/// <summary>
		/// Gets all writeable connection strings. If using a single storage account, then <see cref="TableStorageDataStoreConnectionStringKey"/> will most likely be returned.
		/// If a value for <see cref="TableStorageWritableDataStoreConnectionStringKey"/> is found, it will append ".1", ".2" etc returning any additionally found connection string values in <see cref="ConfigurationManager"/>.
		/// </summary>
		public virtual IEnumerable<string> GetWritableConnectionStrings()
		{
			Logger.LogDebug("Getting table storage writeable connection strings", "TableStorageDataStoreConnectionStringFactory\\GetWritableConnectionStrings");
			try
			{
				var collection = new List<string> ();

				string blobStorageWritableDataStoreConnectionString = ConfigurationManager.GetConnectionString(TableStorageWritableDataStoreConnectionStringKey) ?? ConfigurationManager.GetSetting(TableStorageWritableDataStoreConnectionStringKey);
				if (blobStorageWritableDataStoreConnectionString == null)
				{
					Logger.LogDebug($"No application setting named '{TableStorageWritableDataStoreConnectionStringKey}' was found in the configuration file with the cloud storage connection string.", "TableStorageDataStoreConnectionStringFactory\\GetWritableConnectionStrings");
					blobStorageWritableDataStoreConnectionString = ConfigurationManager.GetConnectionString(TableStorageDataStoreConnectionStringKey) ?? ConfigurationManager.GetSetting(TableStorageDataStoreConnectionStringKey);
				}

				int writeIndex = 1;
				while (!string.IsNullOrWhiteSpace(blobStorageWritableDataStoreConnectionString))
				{
					collection.Add(blobStorageWritableDataStoreConnectionString);

					blobStorageWritableDataStoreConnectionString = ConfigurationManager.GetConnectionString($"{TableStorageWritableDataStoreConnectionStringKey}.{writeIndex}") ?? ConfigurationManager.GetSetting($"{TableStorageWritableDataStoreConnectionStringKey}.{writeIndex}");
					writeIndex++;
				}

				if (!collection.Any())
					throw new MissingConnectionStringException(TableStorageDataStoreConnectionStringKey);

				return collection;
			}
			catch (NullReferenceException exception)
			{
				throw new MissingConnectionStringException(TableStorageDataStoreConnectionStringKey, exception);
			}
			finally
			{
				Logger.LogDebug("Getting table storage writeable connection string... Done", "TableStorageDataStoreConnectionStringFactory\\GetWritableConnectionStrings");
			}
		}

		/// <summary>
		/// Gets the readable connection string. If using a single storage account, then <see cref="TableStorageDataStoreConnectionStringKey"/> will most likely be returned.
		/// If a value for <see cref="TableStorageReadableDataStoreConnectionStringKey"/> is found, that will be returned instead.
		/// </summary>
		public virtual string GetReadableConnectionString()
		{
			Logger.LogDebug("Getting table storage readable connection strings", "TableStorageDataStoreConnectionStringFactory\\GetReadableConnectionStrings");
			try
			{
				string blobStorageWritableDataStoreConnectionString = ConfigurationManager.GetConnectionString(TableStorageReadableDataStoreConnectionStringKey) ?? ConfigurationManager.GetSetting(TableStorageReadableDataStoreConnectionStringKey);
				if (blobStorageWritableDataStoreConnectionString == null)
				{
					Logger.LogDebug($"No application setting named '{TableStorageReadableDataStoreConnectionStringKey}' was found in the configuration file with the cloud storage connection string.", "TableStorageDataStoreConnectionStringFactory\\GetReadableConnectionStrings");
					blobStorageWritableDataStoreConnectionString = ConfigurationManager.GetConnectionString(TableStorageDataStoreConnectionStringKey) ?? ConfigurationManager.GetSetting(TableStorageDataStoreConnectionStringKey);
				}

				if (string.IsNullOrWhiteSpace(blobStorageWritableDataStoreConnectionString))
					throw new MissingConnectionStringException(TableStorageDataStoreConnectionStringKey);

				return blobStorageWritableDataStoreConnectionString;
			}
			catch (NullReferenceException exception)
			{
				throw new MissingConnectionStringException(TableStorageDataStoreConnectionStringKey, exception);
			}
			finally
			{
				Logger.LogDebug("Getting table storage readable connection string... Done", "TableStorageDataStoreConnectionStringFactory\\GetReadableConnectionStrings");
			}
		}

		/// <summary>
		/// Returns the name of the base contain to be used.
		/// This will be the value from <see cref="ConfigurationManager"/> keyed <see cref="TableStorageBaseContainerNameKey"/>.
		/// </summary>
		public string GetBaseContainerName()
		{
			Logger.LogDebug("Getting table storage base container name", "TableStorageDataStoreConnectionStringFactory\\GetContainerName");
			try
			{
				string result = ConfigurationManager.GetSetting(TableStorageBaseContainerNameKey);

				if (string.IsNullOrWhiteSpace(result))
					throw new MissingApplicationSettingException(TableStorageBaseContainerNameKey);

				return result;
			}
			catch (NullReferenceException exception)
			{
				throw new MissingApplicationSettingException(TableStorageBaseContainerNameKey, exception);
			}
			finally
			{
				Logger.LogDebug("Getting table storage base container name... Done", "TableStorageDataStoreConnectionStringFactory\\GetContainerName");
			}
		}

		/// <summary>
		/// Returns <see cref="GetBaseContainerName"/>.
		/// </summary>
		/// <returns><see cref="GetBaseContainerName"/></returns>
		public virtual string GetContainerName()
		{
			return GetBaseContainerName();
		}

		readonly char[] _alphanumericCharacters =
		{
			'0','1','2','3','4','5','6','7','8','9',
			'a','b','c','d','e','f','g','h','i','j','k','l','m','n','o','p','q','r','s','t','u','v','w','x','y','z',
			'A','B','C','D','E','F','G','H','I','J','K','L','M','N','O','P','Q','R','S','T','U','V','W','X','Y','Z'
		};

		/// <summary>
		/// Generates the name of the table for <typeparamref name="TData"/> that matches naming rules for Azure Storage.
		/// </summary>
		/// <typeparam name="TData">The <see cref="Type"/> of data to read or write.</typeparam>
		/// <remarks>https://blogs.msdn.microsoft.com/jmstall/2014/06/12/azure-storage-naming-rules/</remarks>
		public virtual string GetTableName<TData>()
		{
			Type type = typeof (TData);
			string fullTableName = type.AssemblyQualifiedName ?? "Entities";
			StringBuilder sb;

			string name = fullTableName;
			int index1 = name.IndexOf(",", StringComparison.InvariantCultureIgnoreCase);
			int index2 = -1;
			if (index1 > -1)
				index2 = name.IndexOf(",", index1 + 1, StringComparison.InvariantCultureIgnoreCase);
			if (index2 > -1)
			{
				name = name.Substring(0, index2);
				string[] nameParts = name.Split(',');
				if (nameParts.Length == 2)
				{
					if (nameParts[0].StartsWith(nameParts[1].Trim()))
					{
						name = name.Substring(0, index1);
						sb = new StringBuilder();
						foreach (var c in name.Where(c => _alphanumericCharacters.Contains(c)))
							sb.Append(c);

						name = sb.ToString();
						if (name.Length > 36)
							name = name.Substring(name.Length - 36);

						return name;
					}
				}
			}
			else if (index1 > -1)
				name = name.Substring(0, index1);

			sb = new StringBuilder();
			foreach (var c in name.Where(c => _alphanumericCharacters.Contains(c)))
				sb.Append(c);

			name = sb.ToString();
			return name.Substring(0, 36);
		}
	}
}