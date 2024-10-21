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
using Cqrs.Commands;
using Cqrs.Domain.Exceptions;
using Cqrs.Domain.Factories;
using Cqrs.Events;

#if NET40
#else
using System.Threading.Tasks;
#endif

namespace Cqrs.Domain
{
	/// <summary>
	/// Provides basic repository methods for operations with instances of <see cref="ISaga{TAuthenticationToken}"/>.
	/// </summary>
	/// <typeparam name="TAuthenticationToken">The <see cref="Type"/> of authentication token.</typeparam>
	public class SagaRepository<TAuthenticationToken> : ISagaRepository<TAuthenticationToken>
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
				<TAuthenticationToken> EventPublisher { get; private set; }

		/// <summary>
		/// Gets or sets the Publisher used to publish an <see cref="ICommand{TAuthenticationToken}"/>
		/// </summary>
		protected
#if NET40
			ICommandPublisher
#else
			IAsyncCommandPublisher
#endif
				<TAuthenticationToken> CommandPublisher { get; private set; }

		/// <summary>
		/// Gets or set the <see cref="IAggregateFactory"/>.
		/// </summary>
		protected IAggregateFactory SagaFactory { get; private set; }

		/// <summary>
		/// Gets or set the <see cref="ICorrelationIdHelper"/>.
		/// </summary>
		protected ICorrelationIdHelper CorrelationIdHelper { get; private set; }

		/// <summary>
		/// Instantiates a new instance of <see cref="SagaRepository{TAuthenticationToken}"/>
		/// </summary>
		public SagaRepository(IAggregateFactory sagaFactory, IEventStore<TAuthenticationToken> eventStore,
#if NET40
			IEventPublisher
#else
			IAsyncEventPublisher
#endif
				<TAuthenticationToken> eventPublisher,
#if NET40
			ICommandPublisher
#else
			IAsyncCommandPublisher
#endif
				<TAuthenticationToken> commandPublisher, ICorrelationIdHelper correlationIdHelper)
		{
			EventStore = eventStore;
			EventPublisher = eventPublisher;
			CorrelationIdHelper = correlationIdHelper;
			CommandPublisher = commandPublisher;
			SagaFactory = sagaFactory;
		}

		/// <summary>
		/// Save and persist the provided <paramref name="saga"/>, optionally providing the version number the <see cref="ISaga{TAuthenticationToken}"/> is expected to be at.
		/// </summary>
		/// <typeparam name="TSaga">The <see cref="Type"/> of the <see cref="ISaga{TAuthenticationToken}"/>.</typeparam>
		/// <param name="saga">The <see cref="ISaga{TAuthenticationToken}"/> to save and persist.</param>
		/// <param name="expectedVersion">The version number the <see cref="ISaga{TAuthenticationToken}"/> is expected to be at.</param>
		public virtual
#if NET40
			void Save
#else
			async Task SaveAsync
#endif
				<TSaga>(TSaga saga, int? expectedVersion = null)
			where TSaga : ISaga<TAuthenticationToken>
		{
			IList<ISagaEvent<TAuthenticationToken>> uncommittedChanges = saga.GetUncommittedChanges().ToList();
			IEnumerable<ICommand<TAuthenticationToken>> commandsToPublish = saga.GetUnpublishedCommands();
			IEnumerable<IEvent<TAuthenticationToken>> nonSagaEventsToPublish = saga.GetUnpublishedNonSagaEvents();
			if (!uncommittedChanges.Any())
			{
				if (commandsToPublish.Any())
				{
#if NET40
					PublishCommands
#else
					await PublishCommandsAsync
#endif
						(commandsToPublish);
				}

				if (nonSagaEventsToPublish.Any())
				{
#if NET40
					PublishEvents
#else
					await PublishEventsAsync
#endif
						(nonSagaEventsToPublish);
				}
				return;
			}

			if (expectedVersion != null)
			{
				IEnumerable<IEvent<TAuthenticationToken>> eventStoreResults =
#if NET40
					EventStore.Get
#else
					await EventStore.GetAsync
#endif
						(saga.GetType(), saga.Id, false, expectedVersion.Value);
				if (eventStoreResults.Any())
					throw new ConcurrencyException(saga.Id, expectedVersion.Value, eventStoreResults.First().Version);
			}

			var eventsToPublish = new List<ISagaEvent<TAuthenticationToken>>();

			int i = 0;
			int version = saga.Version;
			foreach (ISagaEvent<TAuthenticationToken> @event in uncommittedChanges)
			{
				if (@event.Rsn == Guid.Empty)
					@event.Rsn = saga.Id;
				if (@event.Rsn == Guid.Empty)
					throw new AggregateOrEventMissingIdException(saga.GetType(), @event.GetType());

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
					(saga.GetType(), @event);
				eventsToPublish.Add(@event);
			}

			saga.MarkChangesAsCommitted();
			foreach (ISagaEvent<TAuthenticationToken> @event in eventsToPublish)
			{
#if NET40
				PublishEvent
#else
				await PublishEventAsync
#endif
					(@event);
			}

			if (commandsToPublish.Any())
			{
#if NET40
				PublishCommands
#else
					await PublishCommandsAsync
#endif
					(commandsToPublish);
			}

			if (nonSagaEventsToPublish.Any())
			{
#if NET40
				PublishEvents
#else
					await PublishEventsAsync
#endif
					(nonSagaEventsToPublish);
			}
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
			(ISagaEvent<TAuthenticationToken> @event)
		{
#if NET40
			EventPublisher.Publish
#else
			await EventPublisher.PublishAsync
#endif
				(@event);
		}

		/// <summary>
		/// Publish the <paramref name="commands"/>.
		/// </summary>
		protected virtual
#if NET40
			void PublishCommands
#else
			async Task PublishCommandsAsync
#endif
				(IEnumerable<ICommand<TAuthenticationToken>> commands)
		{
#if NET40
			CommandPublisher.Publish
#else
			await CommandPublisher.PublishAsync
#endif
				(commands);
		}

		/// <summary>
		/// Publish the <paramref name="events"/>.
		/// </summary>
		protected virtual
#if NET40
			void PublishEvents
#else
			async Task PublishEventsAsync
#endif
				(IEnumerable<IEvent<TAuthenticationToken>> events)
		{
#if NET40
			EventPublisher.Publish
#else
			await EventPublisher.PublishAsync
#endif
				(events);
		}

		/// <summary>
		/// Retrieves an <see cref="ISaga{TAuthenticationToken}"/> of type <typeparamref name="TSaga"/>.
		/// </summary>
		/// <typeparam name="TSaga">The <see cref="Type"/> of the <see cref="ISaga{TAuthenticationToken}"/>.</typeparam>
		/// <param name="sagaId">The identifier of the <see cref="ISaga{TAuthenticationToken}"/> to retrieve.</param>
		/// <param name="events">
		/// A collection of <see cref="IEvent{TAuthenticationToken}"/> to replay on the retrieved <see cref="ISaga{TAuthenticationToken}"/>.
		/// If null, the <see cref="IEventStore{TAuthenticationToken}"/> will be used to retrieve a list of <see cref="IEvent{TAuthenticationToken}"/> for you.
		/// </param>
		public virtual
#if NET40
			TSaga Get
#else
			async Task<TSaga> GetAsync
#endif
				 <TSaga>(Guid sagaId, IList<ISagaEvent<TAuthenticationToken>> events = null)
			where TSaga : ISaga<TAuthenticationToken>
		{
			return
#if NET40
				LoadSaga
#else
				await LoadSagaAsync
#endif
					<TSaga>(sagaId, events);
		}

		/// <summary>
		/// Calls <see cref="IAggregateFactory.Create"/> to get a, <typeparamref name="TSaga"/>.
		/// </summary>
		/// <typeparam name="TSaga">The <see cref="Type"/> of <see cref="ISaga{TAuthenticationToken}"/>.</typeparam>
		/// <param name="id">The id of the <typeparamref name="TSaga"/> to create.</param>
		protected virtual TSaga CreateSaga<TSaga>(Guid id)
			where TSaga : ISaga<TAuthenticationToken>
		{
			var saga = SagaFactory.Create<TSaga>(id);

			return saga;
		}

		/// <summary>
		/// Calls <see cref="IAggregateFactory.Create"/> to get a, <typeparamref name="TSaga"/> and then calls LoadSagaHistory.
		/// </summary>
		/// <typeparam name="TSaga">The <see cref="Type"/> of <see cref="ISaga{TAuthenticationToken}"/>.</typeparam>
		/// <param name="id">The id of the <typeparamref name="TSaga"/> to create.</param>
		/// <param name="events">
		/// A collection of <see cref="IEvent{TAuthenticationToken}"/> to replay on the retrieved <see cref="ISaga{TAuthenticationToken}"/>.
		/// If null, the <see cref="IEventStore{TAuthenticationToken}"/> will be used to retrieve a list of <see cref="IEvent{TAuthenticationToken}"/> for you.
		/// </param>
		protected virtual
#if NET40
			TSaga LoadSaga
#else
			async Task<TSaga> LoadSagaAsync
#endif
				 <TSaga>(Guid id, IList<ISagaEvent<TAuthenticationToken>> events = null)
			where TSaga : ISaga<TAuthenticationToken>
		{
			var saga = SagaFactory.Create<TSaga>(id, false);

#if NET40
			LoadSagaHistory
#else
			await LoadSagaHistoryAsync
#endif
				(saga, events);
			return saga;
		}

		/// <summary>
		/// If <paramref name="events"/> is null, loads the events from <see cref="EventStore"/>, checks for duplicates and then
		/// rehydrates the <paramref name="saga"/> with the events.
		/// </summary>
		/// <typeparam name="TSaga">The <see cref="Type"/> of <see cref="ISaga{TAuthenticationToken}"/>.</typeparam>
		/// <param name="saga">The <typeparamref name="TSaga"/> to rehydrate.</param>
		/// <param name="events">
		/// A collection of <see cref="IEvent{TAuthenticationToken}"/> to replay on the retrieved <see cref="ISaga{TAuthenticationToken}"/>.
		/// If null, the <see cref="IEventStore{TAuthenticationToken}"/> will be used to retrieve a list of <see cref="IEvent{TAuthenticationToken}"/> for you.
		/// </param>
		/// <param name="throwExceptionOnNoEvents">If true will throw an instance of <see cref="SagaNotFoundException{TSaga,TAuthenticationToken}"/> if no aggregate events or provided or found in the <see cref="EventStore"/>.</param>
		public virtual
#if NET40
			void LoadSagaHistory
#else
			async Task LoadSagaHistoryAsync
#endif
				<TSaga>(TSaga saga, IList<ISagaEvent<TAuthenticationToken>> events = null, bool throwExceptionOnNoEvents = true)
			where TSaga : ISaga<TAuthenticationToken>
		{
			IList<ISagaEvent<TAuthenticationToken>> theseEvents = events ?? 
			(
#if NET40
				EventStore.Get
#else
				await EventStore.GetAsync
#endif
					<TSaga>(saga.Id)
			).Cast<ISagaEvent<TAuthenticationToken>>().ToList();
			if (!theseEvents.Any())
			{
				if (throwExceptionOnNoEvents)
					throw new SagaNotFoundException<TSaga, TAuthenticationToken>(saga.Id);
				return;
			}

			var duplicatedEvents =
				theseEvents.GroupBy(x => x.Version)
					.Select(x => new { Version = x.Key, Total = x.Count() })
					.FirstOrDefault(x => x.Total > 1);
			if (duplicatedEvents != null)
				throw new DuplicateSagaEventException<TSaga, TAuthenticationToken>(saga.Id, duplicatedEvents.Version);

			saga.LoadFromHistory(theseEvents);
		}
	}
}