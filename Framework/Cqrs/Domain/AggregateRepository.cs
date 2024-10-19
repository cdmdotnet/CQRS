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
using Chinchilla.Logging;
using Cqrs.Configuration;
using Cqrs.Domain.Exceptions;
using Cqrs.Domain.Factories;
using Cqrs.Events;

#if NET472
#else
using System.Threading.Tasks;
#endif

namespace Cqrs.Domain
{
	/// <summary>
	/// Provides basic repository methods for operations with instances of <see cref="IAggregateRoot{TAuthenticationToken}"/> using an <see cref="IEventStore{TAuthenticationToken}"/>
	/// that also publishes events once saved.
	/// </summary>
	/// <typeparam name="TAuthenticationToken">The <see cref="Type"/> of authentication token.</typeparam>
	public class AggregateRepository<TAuthenticationToken>
		: IAggregateRepository<TAuthenticationToken>
	{
		/// <summary>
		/// Gets or sets the <see cref="IEventStore{TAuthenticationToken}"/> used to store and retrieve events from.
		/// </summary>
		protected IEventStore<TAuthenticationToken> EventStore { get; private set; }

		/// <summary>
		/// Gets or sets the Publisher used to publish events on once saved into the <see cref="EventStore"/>.
		/// </summary>
		protected
#if NET40
			IEventPublisher
#else
			IAsyncEventPublisher
#endif
				<TAuthenticationToken> Publisher { get; private set; }

		/// <summary>
		/// Gets or set the <see cref="IAggregateFactory"/>.
		/// </summary>
		protected IAggregateFactory AggregateFactory { get; private set; }

		/// <summary>
		/// Gets or set the <see cref="ICorrelationIdHelper"/>.
		/// </summary>
		protected ICorrelationIdHelper CorrelationIdHelper { get; private set; }

		/// <summary>
		/// Gets or sets the <see cref="IConfigurationManager"/>.
		/// </summary>
		protected IConfigurationManager ConfigurationManager { get; private set; }

		/// <summary>
		/// Instantiates a new instance of <see cref="AggregateRepository{TAuthenticationToken}"/>
		/// </summary>
		public AggregateRepository(IAggregateFactory aggregateFactory, IEventStore<TAuthenticationToken> eventStore,
#if NET40
			IEventPublisher
#else
			IAsyncEventPublisher
#endif
				<TAuthenticationToken> publisher, ICorrelationIdHelper correlationIdHelper, IConfigurationManager configurationManager)
		{
			EventStore = eventStore;
			Publisher = publisher;
			CorrelationIdHelper = correlationIdHelper;
			ConfigurationManager = configurationManager;
			AggregateFactory = aggregateFactory;
		}

		/// <summary>
		/// Save and persist the provided <paramref name="aggregate"/>, optionally providing the version number the <see cref="IAggregateRoot{TAuthenticationToken}"/> is expected to be at.
		/// </summary>
		/// <typeparam name="TAggregateRoot">The <see cref="Type"/> of the <see cref="IAggregateRoot{TAuthenticationToken}"/>.</typeparam>
		/// <param name="aggregate">The <see cref="IAggregateRoot{TAuthenticationToken}"/> to save and persist.</param>
		/// <param name="expectedVersion">The version number the <see cref="IAggregateRoot{TAuthenticationToken}"/> is expected to be at.</param>
		public virtual
#if NET40
			void Save
#else
			async Task SaveAsync
#endif
				<TAggregateRoot>(TAggregateRoot aggregate, int? expectedVersion = null)
			where TAggregateRoot : IAggregateRoot<TAuthenticationToken>
		{
			IList<IEvent<TAuthenticationToken>> uncommittedChanges = aggregate.GetUncommittedChanges().ToList();
			if (!uncommittedChanges.Any())
				return;

			if (expectedVersion != null)
			{
				IEnumerable<IEvent<TAuthenticationToken>> eventStoreResults = null;
#if NET40
				eventStoreResults = EventStore.Get(aggregate.GetType(), aggregate.Id, false, expectedVersion.Value);
#else
				eventStoreResults = await EventStore.GetAsync(aggregate.GetType(), aggregate.Id, false, expectedVersion.Value);
#endif
				if (eventStoreResults.Any())
					throw new ConcurrencyException(aggregate.Id);
			}

			var eventsToPublish = new List<IEvent<TAuthenticationToken>>();

			int i = 0;
			int version = aggregate.Version;
			foreach (IEvent<TAuthenticationToken> @event in uncommittedChanges)
			{
				var eventWithIdentity = @event as IEventWithIdentity<TAuthenticationToken>;
				if (eventWithIdentity != null)
				{
					if (eventWithIdentity.Rsn == Guid.Empty)
						eventWithIdentity.Rsn = aggregate.Id;
					if (eventWithIdentity.Rsn == Guid.Empty)
						throw new AggregateOrEventMissingIdException(aggregate.GetType(), @event.GetType());
				}
				else
				{
					if (@event.Id == Guid.Empty)
						@event.Id = aggregate.Id;
					if (@event.Id == Guid.Empty)
						throw new AggregateOrEventMissingIdException(aggregate.GetType(), @event.GetType());
				}

				i++;
				version++;

				@event.Version = version;
				@event.TimeStamp = DateTimeOffset.UtcNow;
				@event.CorrelationId = CorrelationIdHelper.GetCorrelationId();

#if NET40
				EventStore.Save
#else
				await EventStore.SaveAsync
#endif
					(aggregate.GetType(), @event);
				eventsToPublish.Add(@event);
			}

			aggregate.MarkChangesAsCommitted();
			foreach (IEvent<TAuthenticationToken> @event in eventsToPublish)
#if NET40
				PublishEvent
#else
				await PublishEventAsync
#endif
				(@event);
		}

		/// <summary>
		/// Publish the saved <paramref name="event"/>.
		/// </summary>
		protected virtual
#if NET40
			void PublishEvent
#else
			async Task PublishEventAsync
#endif
				(IEvent<TAuthenticationToken> @event)
		{
#if NET40
			Publisher.Publish
#else
			await Publisher.PublishAsync
#endif
				(@event);
		}

		/// <summary>
		/// Retrieves an <see cref="IAggregateRoot{TAuthenticationToken}"/> of type <typeparamref name="TAggregateRoot"/>.
		/// </summary>
		/// <typeparam name="TAggregateRoot">The <see cref="Type"/> of the <see cref="IAggregateRoot{TAuthenticationToken}"/>.</typeparam>
		/// <param name="aggregateId">The identifier of the <see cref="IAggregateRoot{TAuthenticationToken}"/> to retrieve.</param>
		/// <param name="events">
		/// A collection of <see cref="IEvent{TAuthenticationToken}"/> to replay on the retrieved <see cref="IAggregateRoot{TAuthenticationToken}"/>.
		/// If null, the <see cref="IEventStore{TAuthenticationToken}"/> will be used to retrieve a list of <see cref="IEvent{TAuthenticationToken}"/> for you.
		/// </param>
		public virtual
#if NET40
			TAggregateRoot Get
#else
			async Task<TAggregateRoot> GetAsync
#endif
				<TAggregateRoot>(Guid aggregateId, IList<IEvent<TAuthenticationToken>> events = null)
			where TAggregateRoot : IAggregateRoot<TAuthenticationToken>
		{
			return
#if NET40
				LoadAggregate
#else
				await LoadAggregateAsync
#endif
					<TAggregateRoot>(aggregateId, events);
		}

		/// <summary>
		/// Retrieves an <see cref="IAggregateRoot{TAuthenticationToken}"/> of type <typeparamref name="TAggregateRoot"/> up to and including the provided <paramref name="version"/>.
		/// </summary>
		/// <typeparam name="TAggregateRoot">The <see cref="Type"/> of the <see cref="IAggregateRoot{TAuthenticationToken}"/>.</typeparam>
		/// <param name="aggregateId">The identifier of the <see cref="IAggregateRoot{TAuthenticationToken}"/> to retrieve.</param>
		/// <param name="version">Load events up-to and including from this version</param>
		/// <param name="events">
		/// A collection of <see cref="IEvent{TAuthenticationToken}"/> to replay on the retrieved <see cref="IAggregateRoot{TAuthenticationToken}"/>.
		/// If null, the <see cref="IEventStore{TAuthenticationToken}"/> will be used to retrieve a list of <see cref="IEvent{TAuthenticationToken}"/> for you.
		/// </param>
		public virtual
#if NET40
			TAggregateRoot GetToVersion
#else
			async Task<TAggregateRoot> GetToVersionAsync
#endif
				<TAggregateRoot>(Guid aggregateId, int version, IList<IEvent<TAuthenticationToken>> events = null)
			where TAggregateRoot : IAggregateRoot<TAuthenticationToken>
		{
			return
#if NET40
				LoadAggregateToVersion
#else
				await LoadAggregateToVersionAsync
#endif
					<TAggregateRoot>(aggregateId, version, events);
		}

		/// <summary>
		/// Retrieves an <see cref="IAggregateRoot{TAuthenticationToken}"/> of type <typeparamref name="TAggregateRoot"/> up to and including the provided <paramref name="versionedDate"/>.
		/// </summary>
		/// <typeparam name="TAggregateRoot">The <see cref="Type"/> of the <see cref="IAggregateRoot{TAuthenticationToken}"/>.</typeparam>
		/// <param name="aggregateId">The identifier of the <see cref="IAggregateRoot{TAuthenticationToken}"/> to retrieve.</param>
		/// <param name="versionedDate">Load events up-to and including from this <see cref="DateTime"/></param>
		/// <param name="events">
		/// A collection of <see cref="IEvent{TAuthenticationToken}"/> to replay on the retrieved <see cref="IAggregateRoot{TAuthenticationToken}"/>.
		/// If null, the <see cref="IEventStore{TAuthenticationToken}"/> will be used to retrieve a list of <see cref="IEvent{TAuthenticationToken}"/> for you.
		/// </param>
		public virtual
#if NET40
			TAggregateRoot GetToDate
#else
			async Task<TAggregateRoot> GetToDateAsync
#endif
				<TAggregateRoot>(Guid aggregateId, DateTime versionedDate, IList<IEvent<TAuthenticationToken>> events = null)
			where TAggregateRoot : IAggregateRoot<TAuthenticationToken>
		{
			return
#if NET40
				LoadAggregateToDate
#else
				await LoadAggregateToDateAsync
#endif
					<TAggregateRoot>(aggregateId, versionedDate, events);
		}

		/// <summary>
		/// Calls <see cref="IAggregateFactory.Create"/> to get a, <typeparamref name="TAggregateRoot"/>.
		/// </summary>
		/// <typeparam name="TAggregateRoot">The <see cref="Type"/> of <see cref="IAggregateRoot{TAuthenticationToken}"/>.</typeparam>
		/// <param name="id">The id of the <typeparamref name="TAggregateRoot"/> to create.</param>
		protected virtual TAggregateRoot CreateAggregate<TAggregateRoot>(Guid id)
			where TAggregateRoot : IAggregateRoot<TAuthenticationToken>
		{
			var aggregate = AggregateFactory.Create<TAggregateRoot>(id);

			return aggregate;
		}

		/// <summary>
		/// Calls <see cref="IAggregateFactory.Create"/> to get a, <typeparamref name="TAggregateRoot"/> and then calls LoadAggregateHistory{TAggregateRoot}.
		/// </summary>
		/// <typeparam name="TAggregateRoot">The <see cref="Type"/> of <see cref="IAggregateRoot{TAuthenticationToken}"/>.</typeparam>
		/// <param name="id">The id of the <typeparamref name="TAggregateRoot"/> to create.</param>
		/// <param name="events">
		/// A collection of <see cref="IEvent{TAuthenticationToken}"/> to replay on the retrieved <see cref="IAggregateRoot{TAuthenticationToken}"/>.
		/// If null, the <see cref="IEventStore{TAuthenticationToken}"/> will be used to retrieve a list of <see cref="IEvent{TAuthenticationToken}"/> for you.
		/// </param>
		protected virtual
#if NET40
			TAggregateRoot LoadAggregate
#else
			async Task<TAggregateRoot> LoadAggregateAsync
#endif
				<TAggregateRoot>(Guid id, IList<IEvent<TAuthenticationToken>> events = null)
			where TAggregateRoot : IAggregateRoot<TAuthenticationToken>
		{
			bool tryDependencyResolutionFirst;
			if (!ConfigurationManager.TryGetSetting($"Cqrs.AggregateFactory.TryDependencyResolutionFirst.{typeof(TAggregateRoot).FullName}", out tryDependencyResolutionFirst))
				if (!ConfigurationManager.TryGetSetting("Cqrs.AggregateFactory.TryDependencyResolutionFirst", out tryDependencyResolutionFirst))
					tryDependencyResolutionFirst = false;
			var aggregate = AggregateFactory.Create<TAggregateRoot>(id, tryDependencyResolutionFirst);

#if NET40
			LoadAggregateHistory
#else
			await LoadAggregateHistoryAsync
#endif
				(aggregate, events);
			return aggregate;
		}

		/// <summary>
		/// Calls <see cref="IAggregateFactory.Create"/> to get a, <typeparamref name="TAggregateRoot"/> and then calls LoadAggregateHistory{TAggregateRoot}.
		/// </summary>
		/// <typeparam name="TAggregateRoot">The <see cref="Type"/> of <see cref="IAggregateRoot{TAuthenticationToken}"/>.</typeparam>
		/// <param name="id">The id of the <typeparamref name="TAggregateRoot"/> to create.</param>
		/// <param name="version">Load events up-to and including from this version</param>
		/// <param name="events">
		/// A collection of <see cref="IEvent{TAuthenticationToken}"/> to replay on the retrieved <see cref="IAggregateRoot{TAuthenticationToken}"/>.
		/// If null, the <see cref="IEventStore{TAuthenticationToken}"/> will be used to retrieve a list of <see cref="IEvent{TAuthenticationToken}"/> for you.
		/// </param>
		protected virtual
#if NET40
			TAggregateRoot LoadAggregateToVersion
#else
			async Task<TAggregateRoot> LoadAggregateToVersionAsync
#endif
				<TAggregateRoot>(Guid id, int version, IList<IEvent<TAuthenticationToken>> events = null)
			where TAggregateRoot : IAggregateRoot<TAuthenticationToken>
		{
			bool tryDependencyResolutionFirst;
			if (!ConfigurationManager.TryGetSetting(string.Format("Cqrs.AggregateFactory.TryDependencyResolutionFirst.{0}", typeof(TAggregateRoot).FullName), out tryDependencyResolutionFirst))
				if (!ConfigurationManager.TryGetSetting("Cqrs.AggregateFactory.TryDependencyResolutionFirst", out tryDependencyResolutionFirst))
					tryDependencyResolutionFirst = false;
			var aggregate = AggregateFactory.Create<TAggregateRoot>(id, tryDependencyResolutionFirst);

#if NET40
			LoadAggregateHistoryToVersion
#else
			await LoadAggregateHistoryToVersionAsync
#endif
				(aggregate, version, events);
			return aggregate;
		}

		/// <summary>
		/// Calls <see cref="IAggregateFactory.Create"/> to get a, <typeparamref name="TAggregateRoot"/> and then calls LoadAggregateHistory{TAggregateRoot}.
		/// </summary>
		/// <typeparam name="TAggregateRoot">The <see cref="Type"/> of <see cref="IAggregateRoot{TAuthenticationToken}"/>.</typeparam>
		/// <param name="id">The id of the <typeparamref name="TAggregateRoot"/> to create.</param>
		/// <param name="versionedDate">Load events up-to and including from this <see cref="DateTime"/></param>
		/// <param name="events">
		/// A collection of <see cref="IEvent{TAuthenticationToken}"/> to replay on the retrieved <see cref="IAggregateRoot{TAuthenticationToken}"/>.
		/// If null, the <see cref="IEventStore{TAuthenticationToken}"/> will be used to retrieve a list of <see cref="IEvent{TAuthenticationToken}"/> for you.
		/// </param>
		protected virtual
#if NET40
			TAggregateRoot LoadAggregateToDate
#else
			async Task<TAggregateRoot> LoadAggregateToDateAsync
#endif
				<TAggregateRoot>(Guid id, DateTime versionedDate, IList<IEvent<TAuthenticationToken>> events = null)
			where TAggregateRoot : IAggregateRoot<TAuthenticationToken>
		{
			bool tryDependencyResolutionFirst;
			if (!ConfigurationManager.TryGetSetting(string.Format("Cqrs.AggregateFactory.TryDependencyResolutionFirst.{0}", typeof(TAggregateRoot).FullName), out tryDependencyResolutionFirst))
				if (!ConfigurationManager.TryGetSetting("Cqrs.AggregateFactory.TryDependencyResolutionFirst", out tryDependencyResolutionFirst))
					tryDependencyResolutionFirst = false;
			var aggregate = AggregateFactory.Create<TAggregateRoot>(id, tryDependencyResolutionFirst);

#if NET40
			LoadAggregateHistoryToDate
#else
			await LoadAggregateHistoryToDateAsync
#endif
				(aggregate, versionedDate, events);
			return aggregate;
		}

		/// <summary>
		/// If <paramref name="events"/> is null, loads the events from <see cref="EventStore"/>, checks for duplicates and then
		/// rehydrates the <paramref name="aggregate"/> with the events.
		/// </summary>
		/// <typeparam name="TAggregateRoot">The <see cref="Type"/> of <see cref="IAggregateRoot{TAuthenticationToken}"/>.</typeparam>
		/// <param name="aggregate">The <typeparamref name="TAggregateRoot"/> to rehydrate.</param>
		/// <param name="events">
		/// A collection of <see cref="IEvent{TAuthenticationToken}"/> to replay on the retrieved <see cref="IAggregateRoot{TAuthenticationToken}"/>.
		/// If null, the <see cref="IEventStore{TAuthenticationToken}"/> will be used to retrieve a list of <see cref="IEvent{TAuthenticationToken}"/> for you.
		/// </param>
		/// <param name="throwExceptionOnNoEvents">If true will throw an instance of <see cref="AggregateNotFoundException{TAggregateRoot,TAuthenticationToken}"/> if no aggregate events or provided or found in the <see cref="EventStore"/>.</param>
		public virtual
#if NET40
			void LoadAggregateHistory
#else
			async Task LoadAggregateHistoryAsync
#endif
				<TAggregateRoot>(TAggregateRoot aggregate, IList<IEvent<TAuthenticationToken>> events = null, bool throwExceptionOnNoEvents = true)
			where TAggregateRoot : IAggregateRoot<TAuthenticationToken>
		{
			IList<IEvent<TAuthenticationToken>> theseEvents = events;
			if (theseEvents == null)
			{
				theseEvents = (
#if NET40
					EventStore.Get
#else
					await EventStore.GetAsync
#endif
						<TAggregateRoot>(aggregate.Id)).ToList();
			}
			if (!theseEvents.Any())
			{
				if (throwExceptionOnNoEvents)
					throw new AggregateNotFoundException<TAggregateRoot, TAuthenticationToken>(aggregate.Id);
				return;
			}

			var duplicatedEvents =
				theseEvents.GroupBy(x => x.Version)
					.Select(x => new { Version = x.Key, Total = x.Count() })
					.FirstOrDefault(x => x.Total > 1);
			if (duplicatedEvents != null)
				throw new DuplicateEventException<TAggregateRoot, TAuthenticationToken>(aggregate.Id, duplicatedEvents.Version);

			aggregate.LoadFromHistory(theseEvents);
		}

		/// <summary>
		/// If <paramref name="events"/> is null, loads the events from <see cref="EventStore"/>, checks for duplicates and then
		/// rehydrates the <paramref name="aggregate"/> with the events.
		/// </summary>
		/// <typeparam name="TAggregateRoot">The <see cref="Type"/> of <see cref="IAggregateRoot{TAuthenticationToken}"/>.</typeparam>
		/// <param name="aggregate">The <typeparamref name="TAggregateRoot"/> to rehydrate.</param>
		/// <param name="version">Load events up-to and including from this version</param>
		/// <param name="events">
		/// A collection of <see cref="IEvent{TAuthenticationToken}"/> to replay on the retrieved <see cref="IAggregateRoot{TAuthenticationToken}"/>.
		/// If null, the <see cref="IEventStore{TAuthenticationToken}"/> will be used to retrieve a list of <see cref="IEvent{TAuthenticationToken}"/> for you.
		/// </param>
		/// <param name="throwExceptionOnNoEvents">If true will throw an instance of <see cref="AggregateNotFoundException{TAggregateRoot,TAuthenticationToken}"/> if no aggregate events or provided or found in the <see cref="EventStore"/>.</param>
		public virtual
#if NET40
			void LoadAggregateHistoryToVersion
#else
			async Task LoadAggregateHistoryToVersionAsync
#endif
				<TAggregateRoot>(TAggregateRoot aggregate, int version, IList<IEvent<TAuthenticationToken>> events = null, bool throwExceptionOnNoEvents = true)
			where TAggregateRoot : IAggregateRoot<TAuthenticationToken>
		{
			IList<IEvent<TAuthenticationToken>> theseEvents = events;
			if (theseEvents == null)
			{
				theseEvents = (
#if NET40
					EventStore.GetToVersion
#else
					await EventStore.GetToVersionAsync
#endif
						<TAggregateRoot>(aggregate.Id, version)).ToList();
			}
			if (!theseEvents.Any())
			{
				if (throwExceptionOnNoEvents)
					throw new AggregateNotFoundException<TAggregateRoot, TAuthenticationToken>(aggregate.Id);
				return;
			}

			var duplicatedEvents =
				theseEvents.GroupBy(x => x.Version)
					.Select(x => new { Version = x.Key, Total = x.Count() })
					.FirstOrDefault(x => x.Total > 1);
			if (duplicatedEvents != null)
				throw new DuplicateEventException<TAggregateRoot, TAuthenticationToken>(aggregate.Id, duplicatedEvents.Version);

			aggregate.LoadFromHistory(theseEvents);
		}

		/// <summary>
		/// If <paramref name="events"/> is null, loads the events from <see cref="EventStore"/>, checks for duplicates and then
		/// rehydrates the <paramref name="aggregate"/> with the events.
		/// </summary>
		/// <typeparam name="TAggregateRoot">The <see cref="Type"/> of <see cref="IAggregateRoot{TAuthenticationToken}"/>.</typeparam>
		/// <param name="aggregate">The <typeparamref name="TAggregateRoot"/> to rehydrate.</param>
		/// <param name="versionedDate">Load events up-to and including from this <see cref="DateTime"/></param>
		/// <param name="events">
		/// A collection of <see cref="IEvent{TAuthenticationToken}"/> to replay on the retrieved <see cref="IAggregateRoot{TAuthenticationToken}"/>.
		/// If null, the <see cref="IEventStore{TAuthenticationToken}"/> will be used to retrieve a list of <see cref="IEvent{TAuthenticationToken}"/> for you.
		/// </param>
		/// <param name="throwExceptionOnNoEvents">If true will throw an instance of <see cref="AggregateNotFoundException{TAggregateRoot,TAuthenticationToken}"/> if no aggregate events or provided or found in the <see cref="EventStore"/>.</param>
		public virtual
#if NET40
			void LoadAggregateHistoryToDate
#else
			async Task LoadAggregateHistoryToDateAsync
#endif
				<TAggregateRoot>(TAggregateRoot aggregate, DateTime versionedDate, IList<IEvent<TAuthenticationToken>> events = null, bool throwExceptionOnNoEvents = true)
			where TAggregateRoot : IAggregateRoot<TAuthenticationToken>
		{
			IList<IEvent<TAuthenticationToken>> theseEvents = events;
			if (theseEvents == null)
			{
				theseEvents = (
#if NET40
					EventStore.GetToDate
#else
					await EventStore.GetToDateAsync
#endif
						<TAggregateRoot>(aggregate.Id, versionedDate)).ToList();
			}
			if (!theseEvents.Any())
			{
				if (throwExceptionOnNoEvents)
					throw new AggregateNotFoundException<TAggregateRoot, TAuthenticationToken>(aggregate.Id);
				return;
			}

			var duplicatedEvents =
				theseEvents.GroupBy(x => x.Version)
					.Select(x => new { Version = x.Key, Total = x.Count() })
					.FirstOrDefault(x => x.Total > 1);
			if (duplicatedEvents != null)
				throw new DuplicateEventException<TAggregateRoot, TAuthenticationToken>(aggregate.Id, duplicatedEvents.Version);

			aggregate.LoadFromHistory(theseEvents);
		}
	}
}