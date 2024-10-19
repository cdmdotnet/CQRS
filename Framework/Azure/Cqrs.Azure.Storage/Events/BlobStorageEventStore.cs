#region Copyright
// // -----------------------------------------------------------------------
// // <copyright company="Chinchilla Software Limited">
// // 	Copyright Chinchilla Software Limited. All rights reserved.
// // </copyright>
// // -----------------------------------------------------------------------
#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Azure.Storage.Blobs.Models;
using Azure;
using Chinchilla.Logging;
using Cqrs.Domain;
using Cqrs.Events;
using Cqrs.Infrastructure;
using Cqrs.Messages;

#if NET472
#else
using System.Threading.Tasks;
#endif

namespace Cqrs.Azure.Storage.Events
{
	/// <summary>
	/// An Azure blob storage based <see cref="EventStore{TAuthenticationToken}"/>.
	/// </summary>
	/// <typeparam name="TAuthenticationToken">The <see cref="Type"/> of the authentication token.</typeparam>
	public class BlobStorageEventStore<TAuthenticationToken>
		: EventStore<TAuthenticationToken>
	{
		/// <summary>
		/// Get the <see cref="RawBlobStorageEventStore"/>.
		/// </summary>
		protected RawBlobStorageEventStore BlobStorageStore { get; private set; }

		/// <summary>
		/// Initializes a new instance of the <see cref="BlobStorageEventStore{TAuthenticationToken}"/> class using the specified container.
		/// </summary>
		public BlobStorageEventStore(IEventBuilder<TAuthenticationToken> eventBuilder, IEventDeserialiser<TAuthenticationToken> eventDeserialiser, ILogger logger, IBlobStorageStoreConnectionStringFactory blobStorageEventStoreConnectionStringFactory)
			: base(eventBuilder, eventDeserialiser, logger)
		{
			BlobStorageStore = new RawBlobStorageEventStore(logger, blobStorageEventStoreConnectionStringFactory);
		}

		#region Overrides of EventStore<TAuthenticationToken>

		/// <summary>
		/// Gets a collection of <see cref="IEvent{TAuthenticationToken}"/> for the <see cref="IAggregateRoot{TAuthenticationToken}"/> of type <paramref name="aggregateRootType"/> with the ID matching the provided <paramref name="aggregateId"/>.
		/// </summary>
		/// <param name="aggregateRootType"> <see cref="Type"/> of the <see cref="IAggregateRoot{TAuthenticationToken}"/> the <see cref="IEvent{TAuthenticationToken}"/> was raised in.</param>
		/// <param name="aggregateId">The <see cref="IAggregateRoot{TAuthenticationToken}.Id"/> of the <see cref="IAggregateRoot{TAuthenticationToken}"/>.</param>
		/// <param name="useLastEventOnly">Loads only the last event<see cref="IEvent{TAuthenticationToken}"/>.</param>
		/// <param name="fromVersion">Load events starting from this version</param>
		public override
#if NET472
				IEnumerable<IEvent<TAuthenticationToken>> Get
#else
				async Task<IEnumerable<IEvent<TAuthenticationToken>>> GetAsync
#endif
					(Type aggregateRootType, Guid aggregateId, bool useLastEventOnly = false, int fromVersion = -1)
		{
			string streamName = string.Format(CqrsEventStoreStreamNamePattern, aggregateRootType.FullName, aggregateId);

			Func<BlobItem, bool> predicate = null;
			if (fromVersion > 0)
			{
				predicate = blob =>
				{
					string[] parts = blob.Name.Remove(0, streamName.Length).Split(["/"], StringSplitOptions.RemoveEmptyEntries);
					// We're checking first if this isn't numeric... just return true, otherwise if the version matches return true.
					// This is because while we should trust file name conventions... poorly dropped manual files shouldn't break things.
					return !int.TryParse(parts[0], out int v) || v > fromVersion;
				};
			}

			IEnumerable<EventData> query =
#if NET472
				BlobStorageStore.GetByFolder(streamName, predicate)
#else
				(await BlobStorageStore.GetByFolderAsync(streamName, predicate))
#endif
				.Where(eventData => eventData.AggregateId == streamName && eventData.Version > fromVersion)
				.OrderByDescending(eventData => eventData.Version);

			if (useLastEventOnly)
				query = query.AsQueryable().Take(1);

			return query
				.Select(EventDeserialiser.Deserialise)
				.ToList();
		}

		/// <summary>
		/// Gets a collection of <see cref="IEvent{TAuthenticationToken}"/> for the <see cref="IAggregateRoot{TAuthenticationToken}"/> of type <paramref name="aggregateRootType"/> with the ID matching the provided <paramref name="aggregateId"/> up to and including the provided <paramref name="version"/>.
		/// </summary>
		/// <param name="aggregateRootType"> <see cref="Type"/> of the <see cref="IAggregateRoot{TAuthenticationToken}"/> the <see cref="IEvent{TAuthenticationToken}"/> was raised in.</param>
		/// <param name="aggregateId">The <see cref="IAggregateRoot{TAuthenticationToken}.Id"/> of the <see cref="IAggregateRoot{TAuthenticationToken}"/>.</param>
		/// <param name="version">Load events up-to and including from this version</param>
		public override
#if NET472
				IEnumerable<IEvent<TAuthenticationToken>> GetToVersion
#else
				async Task<IEnumerable<IEvent<TAuthenticationToken>>> GetToVersionAsync
#endif
					(Type aggregateRootType, Guid aggregateId, int version)
		{
			string streamName = string.Format(CqrsEventStoreStreamNamePattern, aggregateRootType.FullName, aggregateId);

			IEnumerable<EventData> query =
#if NET472
				BlobStorageStore.GetByFolder(streamName)
#else
				(await BlobStorageStore.GetByFolderAsync(streamName))
#endif
				.Where(eventData => eventData.AggregateId == streamName && eventData.Version <= version)
				.OrderByDescending(eventData => eventData.Version);

			return query
				.Select(EventDeserialiser.Deserialise)
				.ToList();
		}

		/// <summary>
		/// Gets a collection of <see cref="IEvent{TAuthenticationToken}"/> for the <see cref="IAggregateRoot{TAuthenticationToken}"/> of type <paramref name="aggregateRootType"/> with the ID matching the provided <paramref name="aggregateId"/> up to and including the provided <paramref name="versionedDate"/>.
		/// </summary>
		/// <param name="aggregateRootType"> <see cref="Type"/> of the <see cref="IAggregateRoot{TAuthenticationToken}"/> the <see cref="IEvent{TAuthenticationToken}"/> was raised in.</param>
		/// <param name="aggregateId">The <see cref="IAggregateRoot{TAuthenticationToken}.Id"/> of the <see cref="IAggregateRoot{TAuthenticationToken}"/>.</param>
		/// <param name="versionedDate">Load events up-to and including from this <see cref="DateTime"/></param>
		public override
#if NET472
				IEnumerable<IEvent<TAuthenticationToken>> GetToDate
#else
				async Task<IEnumerable<IEvent<TAuthenticationToken>>> GetToDateAsync
#endif
					(Type aggregateRootType, Guid aggregateId, DateTime versionedDate)
		{
			string streamName = string.Format(CqrsEventStoreStreamNamePattern, aggregateRootType.FullName, aggregateId);

			IEnumerable<EventData> query =
#if NET472
				BlobStorageStore.GetByFolder(streamName)
#else
				(await BlobStorageStore.GetByFolderAsync(streamName))
#endif
				.Where(eventData => eventData.AggregateId == streamName && eventData.Timestamp <= versionedDate)
				.OrderByDescending(eventData => eventData.Version);

			return query
				.Select(EventDeserialiser.Deserialise)
				.ToList();
		}

		/// <summary>
		/// Gets a collection of <see cref="IEvent{TAuthenticationToken}"/> for the <see cref="IAggregateRoot{TAuthenticationToken}"/> of type <paramref name="aggregateRootType"/> with the ID matching the provided <paramref name="aggregateId"/> from and including the provided <paramref name="fromVersionedDate"/> up to and including the provided <paramref name="toVersionedDate"/>.
		/// </summary>
		/// <param name="aggregateRootType"> <see cref="Type"/> of the <see cref="IAggregateRoot{TAuthenticationToken}"/> the <see cref="IEvent{TAuthenticationToken}"/> was raised in.</param>
		/// <param name="aggregateId">The <see cref="IAggregateRoot{TAuthenticationToken}.Id"/> of the <see cref="IAggregateRoot{TAuthenticationToken}"/>.</param>
		/// <param name="fromVersionedDate">Load events from and including from this <see cref="DateTime"/></param>
		/// <param name="toVersionedDate">Load events up-to and including from this <see cref="DateTime"/></param>
		public override
#if NET472
				IEnumerable<IEvent<TAuthenticationToken>> GetBetweenDates
#else
				async Task<IEnumerable<IEvent<TAuthenticationToken>>> GetBetweenDatesAsync
#endif
					(Type aggregateRootType, Guid aggregateId, DateTime fromVersionedDate,
			DateTime toVersionedDate)
		{
			string streamName = string.Format(CqrsEventStoreStreamNamePattern, aggregateRootType.FullName, aggregateId);

			IEnumerable<EventData> query =
#if NET472
				BlobStorageStore.GetByFolder(streamName)
#else
				(await BlobStorageStore.GetByFolderAsync(streamName))
#endif
				.Where(eventData => eventData.AggregateId == streamName && eventData.Timestamp >= fromVersionedDate && eventData.Timestamp <= toVersionedDate)
				.OrderByDescending(eventData => eventData.Version);

			return query
				.Select(EventDeserialiser.Deserialise)
				.ToList();
		}

		/// <summary>
		/// Get all <see cref="IEvent{TAuthenticationToken}"/> instances for the given <paramref name="correlationId"/>.
		/// </summary>
		/// <param name="correlationId">The <see cref="IMessage.CorrelationId"/> of the <see cref="IEvent{TAuthenticationToken}"/> instances to retrieve.</param>
		public override
#if NET472
				IEnumerable<EventData> Get
#else
				async Task<IEnumerable<EventData>> GetAsync
#endif
					(Guid correlationId)
		{
			string streamName = string.Format("..\\by-correlation\\{0:N}", correlationId);
			IEnumerable<EventData> query =
#if NET472
				BlobStorageStore.GetByFolder(streamName)
#else
				(await BlobStorageStore.GetByFolderAsync(streamName))
#endif
				.Where(eventData => eventData.CorrelationId == correlationId)
				.OrderBy(eventData => eventData.Timestamp);

				return query.ToList();
		}

		/// <summary>
		/// Saves the provided <paramref name="event"/>.
		/// </summary>
		/// <param name="aggregateRootType"> <see cref="Type"/> of the <see cref="IAggregateRoot{TAuthenticationToken}"/> the <see cref="IEvent{TAuthenticationToken}"/> was raised in.</param>
		/// <param name="event">The <see cref="IEvent{TAuthenticationToken}"/> to be saved.</param>
		public override
#if NET472
			void Save
#else
			async Task SaveAsync
#endif
				(Type aggregateRootType, IEvent<TAuthenticationToken> @event)
		{
#if NET472
			base.Save
#else
			await base.SaveAsync
#endif
			(aggregateRootType, @event);

			// now verify it's available by a load as blob can be delayed, and fast actions can cause missing event errors.
			IEnumerable<IEvent<TAuthenticationToken>> events;
#if NET472
			SpinWait.SpinUntil(() =>
#else
			await SpinWait.SpinUntilAsync(async () =>
#endif
			{
				events =
#if NET472
					Get
#else
					await GetAsync
#endif
						(aggregateRootType, @event.GetIdentity(), true, @event.Version - 1);
				return events.Any();
			}, 5000);
		}

		/// <summary>
		/// Persist the provided <paramref name="eventData"/> into storage.
		/// </summary>
		/// <param name="eventData">The <see cref="EventData"/> to persist.</param>
		protected override
#if NET472
				void PersistEvent
#else
				async Task PersistEventAsync
#endif
					(EventData eventData)
		{
			Logger.LogDebug("Adding data to the blob storage event-store aggregate folder", "BlobStorageStore\\Add");
#if NET472
			BlobStorageStore.Add
#else
			await BlobStorageStore.AddAsync
#endif
				(eventData);

			Logger.LogDebug("Adding data to the blob storage event-store by-correlation folder", "BlobStorageStore\\Add");
#if NET472
			BlobStorageStore.AddToCorrelationFolder
#else
			await BlobStorageStore.AddToCorrelationFolderAsync
#endif
				(eventData);
		}

		#endregion

		/// <summary>
		/// The raw <see cref="BlobStorageStore{TEventData}"/>.
		/// </summary>
		public class RawBlobStorageEventStore
			: BlobStorageStore<EventData>
		{
			/// <summary>
			/// Initializes a new instance of the <see cref="RawBlobStorageEventStore"/> class using the specified container.
			/// </summary>
			public RawBlobStorageEventStore(ILogger logger, IBlobStorageStoreConnectionStringFactory blobStorageEventStoreConnectionStringFactory)
				: base(logger)
			{
				GetContainerName = blobStorageEventStoreConnectionStringFactory.GetBaseContainerName;
				IsContainerPublic = () => false;
				GenerateFileName = data => Path.Combine(data.AggregateId, string.Format("{0:D10}\\{1}",data.Version, data.EventId.ToString("N")));

#if NET472
			Initialise(blobStorageEventStoreConnectionStringFactory);
#else
				Task.Run(async () => {
					await InitialiseAsync(blobStorageEventStoreConnectionStringFactory);
				}).Wait();
#endif
			}

			/// <summary>
			/// Add the provided <paramref name="data"/> into the correlation folder.
			/// </summary>
			public virtual
#if NET472
				void AddToCorrelationFolder
#else
				async Task AddToCorrelationFolderAsync
#endif
					(EventData data)
			{
#if NET472
				SaveData
#else
				await SaveDataAsync
#endif
				(
					data,
#if NET472
#else
					async
#endif
						(taskData, cloudBlockBlob) =>
					{
						try
						{
							Response<BlobContentInfo> result =
#if NET472
							cloudBlockBlob.Upload
#else
								await cloudBlockBlob.UploadAsync
#endif
									(Serialise(taskData), new BlobHttpHeaders { ContentType = "application/json" });
							return cloudBlockBlob.Uri;
						}
						catch (Exception exception)
						{
							Logger.LogError("There was an issue persisting data to blob storage.", exception: exception);
							throw;
						}
					},
					taskData => string.Format("by-correlation\\{0:N}\\{1}", data.CorrelationId, GenerateFileName(data))
				);
			}
		}
	}
}