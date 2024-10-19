#region Copyright
// // -----------------------------------------------------------------------
// // <copyright company="Chinchilla Software Limited">
// // 	Copyright Chinchilla Software Limited. All rights reserved.
// // </copyright>
// // -----------------------------------------------------------------------
#endregion

using System;
using System.Linq;
using Azure.Data.Tables;
using Chinchilla.Logging;
using Cqrs.Configuration;
using Cqrs.Domain;
using Cqrs.Events;
using Cqrs.Snapshots;

#if NET472
#else
using System.Threading.Tasks;
#endif

namespace Cqrs.Azure.Storage.Events
{
	/// <summary>
	/// An Azure Storage based <see cref="SnapshotStore"/>.
	/// </summary>
	public class TableStorageSnapshotStore
		: SnapshotStore
	{
		/// <summary>
		/// The pattern used to generate the stream name.
		/// </summary>
		protected const string TableCqrsSnapshotStoreStreamNamePattern = "{0}.{1}";

		/// <summary>
		/// Gets or sets the underlying <see cref="TableStorageStore"/> used for persisting and reading <see cref="Snapshot"/> data.
		/// </summary>
		protected RawTableStorageSnapshotStore TableStorageStore { get; set; }

		/// <summary>
		/// Initializes a new instance of the <see cref="TableStorageEventStore{TAuthenticationToken}"/> class using the specified container.
		/// </summary>
		public TableStorageSnapshotStore(IConfigurationManager configurationManager, ISnapshotDeserialiser eventDeserialiser, ILogger logger, ICorrelationIdHelper correlationIdHelper, ISnapshotBuilder snapshotBuilder, ITableStorageSnapshotStoreConnectionStringFactory tableStorageSnapshotStoreConnectionStringFactory, Func<ILogger, ITableStorageSnapshotStoreConnectionStringFactory, RawTableStorageSnapshotStore> createRawTableStorageSnapshotStoreFunction = null)
			: base(configurationManager, eventDeserialiser, snapshotBuilder, logger, correlationIdHelper)
		{
			if (createRawTableStorageSnapshotStoreFunction == null)
				createRawTableStorageSnapshotStoreFunction = (logger1, tableStorageSnapshotStoreConnectionStringFactory1) => new RawTableStorageSnapshotStore(logger1, tableStorageSnapshotStoreConnectionStringFactory1);
			TableStorageStore = createRawTableStorageSnapshotStoreFunction(logger, tableStorageSnapshotStoreConnectionStringFactory);
		}

		#region Overrides of SnapshotStore

		/// <summary>
		/// Get the latest <see cref="Snapshot"/> from storage.
		/// </summary>
		/// <returns>The most recent <see cref="Snapshot"/> of</returns>
		protected override
#if NET472
			Snapshot Get
#else
			async Task<Snapshot> GetAsync
#endif
				(Type aggregateRootType, string streamName)
		{
			string partitionKey = StorageStore<object, object, object>.GetSafeStorageKey(streamName);
			var query = TableStorageStore.Collection.Cast<ITableEntity>()
				.Where(e => e.PartitionKey == partitionKey)
				.Cast<EventDataTableEntity<EventData>>()
				.Select(eventData => eventData.EventData)
				.Where(eventData => eventData.AggregateId == streamName)
				.OrderByDescending(eventData => eventData.Version)
				.Take(1)
				.Select(EventDeserialiser.Deserialise);

			var result = query
				.SingleOrDefault();

			return
#if NET472
				result;
#else
				await Task.FromResult(result);
#endif
		}

		/// <summary>
		/// Saves the provided <paramref name="snapshot"/> into storage.
		/// </summary>
		/// <param name="snapshot">the <see cref="Snapshot"/> to save and store.</param>
		public override
#if NET472
			void Save
#else
			async Task SaveAsync
#endif
				(Snapshot snapshot)
		{
			Logger.LogDebug("Adding data to the table storage snapshot-store aggregate folder", "TableStorageStore\\Add");
#if NET472
			TableStorageStore.Add
#else
			await TableStorageStore.AddAsync
#endif
				(BuildEventData(snapshot));
			Logger.LogDebug("Added data to the table storage snapshot-store aggregate folder", "TableStorageStore\\Save");
		}

		/// <summary>
		/// Generate a unique stream name based on the provided <paramref name="aggregateRootType"/> and the <paramref name="aggregateId"/>.
		/// </summary>
		/// <param name="aggregateRootType">The <see cref="Type"/> of the <see cref="IAggregateRoot{TAuthenticationToken}"/>.</param>
		/// <param name="aggregateId">The ID of the <see cref="IAggregateRoot{TAuthenticationToken}"/>.</param>
		protected override string GenerateStreamName(Type aggregateRootType, Guid aggregateId)
		{
			return string.Format(TableCqrsSnapshotStoreStreamNamePattern, aggregateRootType.FullName, aggregateId);
		}

		#endregion

		/// <summary>
		/// An Azure Storage based <see cref="TableStorageStore{TData,TCollectionItemData}"/>.
		/// </summary>
		public class RawTableStorageSnapshotStore
			: TableStorageStore<EventDataTableEntity<EventData>, EventData>
		{
			private string TableName { get; set; }

			/// <summary>
			/// Initializes a new instance of the <see cref="RawTableStorageSnapshotStore"/> class using the specified container.
			/// </summary>
			public RawTableStorageSnapshotStore(ILogger logger, ITableStorageSnapshotStoreConnectionStringFactory tableStorageSnapshotStoreConnectionStringFactory)
				: base(logger)
			{
				GetContainerName = tableStorageSnapshotStoreConnectionStringFactory.GetBaseContainerName;
				IsContainerPublic = () => false;

				TableName = "SnapshotStore";

#if NET472
				Initialise(tableStorageSnapshotStoreConnectionStringFactory);
#else
				SafeTask.RunSafelyAsync(async () => {
					await InitialiseAsync(tableStorageSnapshotStoreConnectionStringFactory);
				}).Wait();
#endif
			}

			#region Overrides of StorageStore<EventData,CloudTable>

			/// <summary>
			/// Returns <see cref="TableName"/>.
			/// </summary>
			/// <param name="sourceName">Is not used.</param>
			/// <returns><see cref="TableName"/></returns>
			protected override string GetSafeSourceName(string sourceName)
			{
				return TableName;
			}

			#endregion

			#region Overrides of TableStorageStore<EventData>

			/// <summary>
			/// Creates a new <see cref="EventDataTableEntity{TEventData}"/>.
			/// </summary>
			protected override ITableEntity CreateTableEntity(EventData data)
			{
				return new EventDataTableEntity<EventData>(data);
			}

			/// <summary>
			/// Will mark the <paramref name="data"/> as logically (or soft).
			/// </summary>
			public override
#if NET472
				void Remove
#else
				Task RemoveAsync
#endif
					(EventData data)
			{
				throw new InvalidOperationException("Snapshot store entries are not deletable.");
			}

			/// <summary>
			/// Will throw an <see cref="InvalidOperationException"/> as this is not supported.
			/// </summary>
			protected override (string PartitionKey, string RowKey) GetUpdatableTableEntity(EventData data)
			{
				throw new InvalidOperationException("Snapshot store entries are not updateable.");
			}

			/// <summary>
			/// Will throw an <see cref="InvalidOperationException"/> as this is not supported.
			/// </summary>
			protected override (string PartitionKey, string RowKey) GetUpdatableTableEntity(EventDataTableEntity<EventData> data)
			{
				return GetUpdatableTableEntity(data.EventData);
			}

			#endregion
		}
	}
}