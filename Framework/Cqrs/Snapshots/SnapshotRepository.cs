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
using Cqrs.Domain;
using Cqrs.Domain.Factories;
using Cqrs.Events;
using Cqrs.Infrastructure;

#if NET472
#else
using System.Threading.Tasks;
#endif

namespace Cqrs.Snapshots
{
	/// <summary>
	/// Provides basic repository methods for operations with instances of <see cref="IAggregateRoot{TAuthenticationToken}"/>
	/// utilising <see cref="Snapshot">snapshots</see> for optimised rehydration.
	/// </summary>
	/// <typeparam name="TAuthenticationToken">The <see cref="Type"/> of authentication token.</typeparam>
	public class SnapshotRepository<TAuthenticationToken>
		: ISnapshotAggregateRepository<TAuthenticationToken>
	{
		/// <summary>
		/// Gets or sets the <see cref="ISnapshotStore"/>.
		/// </summary>
		protected ISnapshotStore SnapshotStore { get; private set; }

		/// <summary>
		/// Gets or sets the <see cref="ISnapshotStrategy{TAuthenticationToken}"/>.
		/// </summary>
		protected ISnapshotStrategy<TAuthenticationToken> SnapshotStrategy { get; private set; }

		/// <summary>
		/// Gets or sets the <see cref="IAggregateRepository{TAuthenticationToken}"/>.
		/// </summary>
		protected IAggregateRepository<TAuthenticationToken> Repository { get; private set; }

		/// <summary>
		/// Gets or sets the <see cref="IEventStore{TAuthenticationToken}"/>.
		/// </summary>
		protected IEventStore<TAuthenticationToken> EventStore { get; private set; }

		/// <summary>
		/// Gets or sets the <see cref="IAggregateFactory"/>.
		/// </summary>
		protected IAggregateFactory AggregateFactory { get; private set; }

		/// <summary>
		/// Instantiates a new instance of <see cref="SnapshotRepository{TAuthenticationToken}"/>.
		/// </summary>
		public SnapshotRepository(ISnapshotStore snapshotStore, ISnapshotStrategy<TAuthenticationToken> snapshotStrategy, IAggregateRepository<TAuthenticationToken> repository, IEventStore<TAuthenticationToken> eventStore, IAggregateFactory aggregateFactory)
		{
			SnapshotStore = snapshotStore;
			SnapshotStrategy = snapshotStrategy;
			Repository = repository;
			EventStore = eventStore;
			AggregateFactory = aggregateFactory;
		}

#if NET40
		/// <summary>
		/// Calls <see cref="TryMakeSnapshot"/> then IAggregateRepository{TAuthenticationToken}.Save on <see cref="Repository"/>.
		/// </summary>
		/// <typeparam name="TAggregateRoot">The <see cref="Type"/> of the <see cref="IAggregateRoot{TAuthenticationToken}"/>.</typeparam>
		/// <param name="aggregate">The <see cref="IAggregateRoot{TAuthenticationToken}"/> to save and persist.</param>
		/// <param name="expectedVersion">The version number the <see cref="IAggregateRoot{TAuthenticationToken}"/> is expected to be at.</param>
#else
		/// <summary>
		/// Calls <see cref="TryMakeSnapshotAsync(IAggregateRoot{TAuthenticationToken}, IEnumerable{IEvent{TAuthenticationToken}})"/> then IAggregateRepository{TAuthenticationToken}.Save on <see cref="Repository"/>.
		/// </summary>
		/// <typeparam name="TAggregateRoot">The <see cref="Type"/> of the <see cref="IAggregateRoot{TAuthenticationToken}"/>.</typeparam>
		/// <param name="aggregate">The <see cref="IAggregateRoot{TAuthenticationToken}"/> to save and persist.</param>
		/// <param name="expectedVersion">The version number the <see cref="IAggregateRoot{TAuthenticationToken}"/> is expected to be at.</param>
#endif
		public virtual
#if NET40
			void Save
#else
			async Task SaveAsync
#endif
				<TAggregateRoot>(TAggregateRoot aggregate, int? expectedVersion = null)
			where TAggregateRoot : IAggregateRoot<TAuthenticationToken>
		{
			// We need to grab these first as the changes will have been commited already by the time we go to make the snapshot.
			IEnumerable<IEvent<TAuthenticationToken>> uncommittedChanges = aggregate.GetUncommittedChanges();
			// Save the evets first then snapshot the system.
#if NET40
			Repository.Save
#else
			await Repository.SaveAsync
#endif
				(aggregate, expectedVersion);

#if NET40
			TryMakeSnapshot
#else
			await TryMakeSnapshotAsync
#endif
				(aggregate, uncommittedChanges);
		}

#if NET40
		/// <summary>
		/// Retrieves an <see cref="IAggregateRoot{TAuthenticationToken}"/> of type <typeparamref name="TAggregateRoot"/>,
		/// First using <see cref="TryRestoreAggregateFromSnapshot{TAggregateRoot}"/>, otherwise via IAggregateRepository{TAuthenticationToken}.Get on <see cref="Repository"/>
		/// Then does rehydration.
		/// </summary>
		/// <typeparam name="TAggregateRoot">The <see cref="Type"/> of the <see cref="IAggregateRoot{TAuthenticationToken}"/>.</typeparam>
		/// <param name="aggregateId">The identifier of the <see cref="IAggregateRoot{TAuthenticationToken}"/> to retrieve.</param>
		/// <param name="events">
		/// A collection of <see cref="IEvent{TAuthenticationToken}"/> to replay on the retrieved <see cref="IAggregateRoot{TAuthenticationToken}"/>.
		/// If null, the <see cref="IEventStore{TAuthenticationToken}"/> will be used to retrieve a list of <see cref="IEvent{TAuthenticationToken}"/> for you.
		/// </param>
#else
		/// <summary>
		/// Retrieves an <see cref="IAggregateRoot{TAuthenticationToken}"/> of type <typeparamref name="TAggregateRoot"/>,
		/// First using <see cref="TryRestoreAggregateFromSnapshotAsync{TAggregateRoot}(Guid, TAggregateRoot)"/>, otherwise via IAggregateRepository{TAuthenticationToken}.Get on <see cref="Repository"/>
		/// Then does rehydration.
		/// </summary>
		/// <typeparam name="TAggregateRoot">The <see cref="Type"/> of the <see cref="IAggregateRoot{TAuthenticationToken}"/>.</typeparam>
		/// <param name="aggregateId">The identifier of the <see cref="IAggregateRoot{TAuthenticationToken}"/> to retrieve.</param>
		/// <param name="events">
		/// A collection of <see cref="IEvent{TAuthenticationToken}"/> to replay on the retrieved <see cref="IAggregateRoot{TAuthenticationToken}"/>.
		/// If null, the <see cref="IEventStore{TAuthenticationToken}"/> will be used to retrieve a list of <see cref="IEvent{TAuthenticationToken}"/> for you.
		/// </param>
#endif
		public virtual
#if NET40
			TAggregateRoot Get
#else
			async Task<TAggregateRoot> GetAsync
#endif
				<TAggregateRoot>(Guid aggregateId, IList<IEvent<TAuthenticationToken>> events = null)
			where TAggregateRoot : IAggregateRoot<TAuthenticationToken>
		{
			var aggregate = AggregateFactory.Create<TAggregateRoot>();
			int snapshotVersion =
#if NET40
				TryRestoreAggregateFromSnapshot
#else
				await TryRestoreAggregateFromSnapshotAsync
#endif
					(aggregateId, aggregate);
			if (snapshotVersion == -1)
			{
				return
#if NET40
					Repository.Get
#else
					await Repository.GetAsync
#endif
						<TAggregateRoot>(aggregateId);
			}
			IEnumerable<IEvent<TAuthenticationToken>> theseEvents = events ?? (
#if NET40
				EventStore.Get
#else
				await EventStore.GetAsync
#endif
				<TAggregateRoot>(aggregateId, false, snapshotVersion)
			).Where(desc => desc.Version > snapshotVersion);
			aggregate.LoadFromHistory(theseEvents);

			return aggregate;
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
			Task<TAggregateRoot> GetToVersionAsync
#endif
				 <TAggregateRoot>(Guid aggregateId, int version, IList<IEvent<TAuthenticationToken>> events = null)
			where TAggregateRoot : IAggregateRoot<TAuthenticationToken>
		{
			throw new InvalidOperationException("Verion replay is not appriopriate with snapshots.");
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
			Task<TAggregateRoot> GetToDateAsync
#endif
				<TAggregateRoot>(Guid aggregateId, DateTime versionedDate, IList<IEvent<TAuthenticationToken>> events = null)
			where TAggregateRoot : IAggregateRoot<TAuthenticationToken>
		{
			throw new InvalidOperationException("Verion replay is not appriopriate with snapshots.");
		}

#if NET40
		/// <summary>
		/// Calls <see cref="ISnapshotStrategy{TAuthenticationToken}.IsSnapshotable"/> on <see cref="SnapshotStrategy"/>
		/// If the <typeparamref name="TAggregateRoot"/> is snapshot-able <see cref="ISnapshotStore.Get{TAggregateRoot}"/> is called on <see cref="SnapshotStore"/>.
		/// The Restore method is then called on
		/// </summary>
		/// <typeparam name="TAggregateRoot">The <see cref="Type"/> of the <see cref="IAggregateRoot{TAuthenticationToken}"/>.</typeparam>
		/// <param name="id">The identifier of the <see cref="IAggregateRoot{TAuthenticationToken}"/> to restore, since the <paramref name="aggregate"/> may be completely uninitialised.</param>
		/// <param name="aggregate">The <typeparamref name="TAggregateRoot"/></param>
		/// <returns>-1 if no restoration was made, otherwise version number the <typeparamref name="TAggregateRoot"/> was rehydrated to.</returns>
		/// <remarks>There may be more events after the snapshot that still need to rehydrated into the <typeparamref name="TAggregateRoot"/> after restoration.</remarks>
#else
		/// <summary>
		/// Calls <see cref="ISnapshotStrategy{TAuthenticationToken}.IsSnapshotable"/> on <see cref="SnapshotStrategy"/>
		/// If the <typeparamref name="TAggregateRoot"/> is snapshot-able <see cref="ISnapshotStore.GetAsync{TAggregateRoot}(Guid)"/> is called on <see cref="SnapshotStore"/>.
		/// The Restore method is then called on
		/// </summary>
		/// <typeparam name="TAggregateRoot">The <see cref="Type"/> of the <see cref="IAggregateRoot{TAuthenticationToken}"/>.</typeparam>
		/// <param name="id">The identifier of the <see cref="IAggregateRoot{TAuthenticationToken}"/> to restore, since the <paramref name="aggregate"/> may be completely uninitialised.</param>
		/// <param name="aggregate">The <typeparamref name="TAggregateRoot"/></param>
		/// <returns>-1 if no restoration was made, otherwise version number the <typeparamref name="TAggregateRoot"/> was rehydrated to.</returns>
		/// <remarks>There may be more events after the snapshot that still need to rehydrated into the <typeparamref name="TAggregateRoot"/> after restoration.</remarks>
#endif
		protected virtual
#if NET40
			int TryRestoreAggregateFromSnapshot
#else
			async Task<int> TryRestoreAggregateFromSnapshotAsync
#endif
				<TAggregateRoot>(Guid id, TAggregateRoot aggregate)
		{
			int version = -1;
			if (SnapshotStrategy.IsSnapshotable(typeof(TAggregateRoot)))
			{
				Snapshot snapshot =
#if NET40
					SnapshotStore.Get
#else
					await SnapshotStore.GetAsync
#endif
						<TAggregateRoot>(id);
				if (snapshot != null)
				{
					aggregate.AsDynamic().Restore(snapshot);
					version = snapshot.Version;
				}
			}
			return version;
		}

#if NET40
		/// <summary>
		/// Calls <see cref="ISnapshotStrategy{TAuthenticationToken}.ShouldMakeSnapShot(IAggregateRoot{TAuthenticationToken}, IEnumerable{IEvent{TAuthenticationToken}})"/> on <see cref="SnapshotStrategy"/>
		/// If the <see cref="IAggregateRoot{TAuthenticationToken}"/> is snapshot-able <see cref="SnapshotAggregateRoot{TAuthenticationToken,TSnapshot}.GetSnapshot"/> is called
		/// The <see cref="Snapshot.Version"/> is calculated, finally <see cref="ISnapshotStore.Save"/> is called on <see cref="SnapshotStore"/>.
		/// </summary>
		/// <param name="aggregate">The <see cref="IAggregateRoot{TAuthenticationToken}"/> to try and snapshot.</param>
		/// <param name="uncommittedChanges">A collection of uncommited changes to assess. If null the aggregate will be asked to provide them.</param>
#else
		/// <summary>
		/// Calls <see cref="ISnapshotStrategy{TAuthenticationToken}.ShouldMakeSnapShot(IAggregateRoot{TAuthenticationToken}, IEnumerable{IEvent{TAuthenticationToken}})"/> on <see cref="SnapshotStrategy"/>
		/// If the <see cref="IAggregateRoot{TAuthenticationToken}"/> is snapshot-able <see cref="SnapshotAggregateRoot{TAuthenticationToken,TSnapshot}.GetSnapshot"/> is called
		/// The <see cref="Snapshot.Version"/> is calculated, finally <see cref="ISnapshotStore.SaveAsync(Snapshot)"/> is called on <see cref="SnapshotStore"/>.
		/// </summary>
		/// <param name="aggregate">The <see cref="IAggregateRoot{TAuthenticationToken}"/> to try and snapshot.</param>
		/// <param name="uncommittedChanges">A collection of uncommited changes to assess. If null the aggregate will be asked to provide them.</param>
#endif
		protected virtual
#if NET40
			void TryMakeSnapshot
#else
			async Task TryMakeSnapshotAsync
#endif
				(IAggregateRoot<TAuthenticationToken> aggregate, IEnumerable<IEvent<TAuthenticationToken>> uncommittedChanges)
		{
			if (!SnapshotStrategy.ShouldMakeSnapShot(aggregate, uncommittedChanges))
				return;
			dynamic snapshot = aggregate.AsDynamic().GetSnapshot().RealObject;
			var rSnapshot = snapshot as Snapshot;
			if (rSnapshot != null)
			{
				rSnapshot.Version = aggregate.Version;
#if NET40
				SnapshotStore.Save
#else
				await SnapshotStore.SaveAsync
#endif
					(rSnapshot);
			}
			else
			{
				snapshot.Version = aggregate.Version;
#if NET40
				SnapshotStore.Save
#else
				await SnapshotStore.SaveAsync
#endif
					(snapshot);
			}
		}
	}
}