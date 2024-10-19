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
	/// Provides basic repository methods for operations with instances of <see cref="ISaga{TAuthenticationToken}"/>
	/// utilising <see cref="Snapshot">snapshots</see> for optimised rehydration.
	/// </summary>
	/// <typeparam name="TAuthenticationToken">The <see cref="Type"/> of authentication token.</typeparam>
	public class SnapshotSagaRepository<TAuthenticationToken>
		: ISnapshotSagaRepository<TAuthenticationToken>
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
		/// Gets or sets the <see cref="SagaRepository{TAuthenticationToken}"/>.
		/// </summary>
		protected ISagaRepository<TAuthenticationToken> Repository { get; private set; }

		/// <summary>
		/// Gets or sets the <see cref="IEventStore{TAuthenticationToken}"/>.
		/// </summary>
		protected IEventStore<TAuthenticationToken> EventStore { get; private set; }

		/// <summary>
		/// Gets or sets the <see cref="IAggregateFactory"/>.
		/// </summary>
		protected IAggregateFactory SagaFactory { get; private set; }

		/// <summary>
		/// Instantiates a new instance of <see cref="SnapshotRepository{TAuthenticationToken}"/>.
		/// </summary>
		public SnapshotSagaRepository(ISnapshotStore snapshotStore, ISnapshotStrategy<TAuthenticationToken> snapshotStrategy, ISagaRepository<TAuthenticationToken> repository, IEventStore<TAuthenticationToken> eventStore, IAggregateFactory sagaFactory)
		{
			SnapshotStore = snapshotStore;
			SnapshotStrategy = snapshotStrategy;
			Repository = repository;
			EventStore = eventStore;
			SagaFactory = sagaFactory;
		}

#if NET40
		/// <summary>
		/// Calls <see cref="TryMakeSnapshot"/> then ISagaRepository{TAuthenticationToken}.Save on <see cref="Repository"/>.
		/// </summary>
		/// <typeparam name="TSaga">The <see cref="Type"/> of the <see cref="ISaga{TAuthenticationToken}"/>.</typeparam>
		/// <param name="saga">The <see cref="ISaga{TAuthenticationToken}"/> to save and persist.</param>
		/// <param name="expectedVersion">The version number the <see cref="ISaga{TAuthenticationToken}"/> is expected to be at.</param>
#else
		/// <summary>
		/// Calls <see cref="TryMakeSnapshotAsync(ISaga{TAuthenticationToken}, IEnumerable{ISagaEvent{TAuthenticationToken}})"/> then ISagaRepository{TAuthenticationToken}.Save on <see cref="Repository"/>.
		/// </summary>
		/// <typeparam name="TSaga">The <see cref="Type"/> of the <see cref="ISaga{TAuthenticationToken}"/>.</typeparam>
		/// <param name="saga">The <see cref="ISaga{TAuthenticationToken}"/> to save and persist.</param>
		/// <param name="expectedVersion">The version number the <see cref="ISaga{TAuthenticationToken}"/> is expected to be at.</param>
#endif
		public virtual
#if NET40
			void Save
#else
			async Task SaveAsync
#endif
				<TSaga>(TSaga saga, int? expectedVersion = null)
			where TSaga : ISaga<TAuthenticationToken>
		{
			// We need to grab these first as the changes will have been commited already by the time we go to make the snapshot.
			IEnumerable<ISagaEvent<TAuthenticationToken>> uncommittedChanges = saga.GetUncommittedChanges();
			// Save the evets first then snapshot the system.
#if NET40
			Repository.Save
#else
			await Repository.SaveAsync
#endif
				(saga, expectedVersion);

#if NET40
			TryMakeSnapshot
#else
			await TryMakeSnapshotAsync
#endif
				(saga, uncommittedChanges);
		}

#if NET40
		/// <summary>
		/// Retrieves an <see cref="ISaga{TAuthenticationToken}"/> of type <typeparamref name="TSaga"/>,
		/// First using <see cref="TryRestoreSagaFromSnapshot{TSaga}"/>, otherwise via ISagaRepository{TAuthenticationToken}.Get on <see cref="Repository"/>
		/// Then does rehydration.
		/// </summary>
		/// <typeparam name="TSaga">The <see cref="Type"/> of the <see cref="ISaga{TAuthenticationToken}"/>.</typeparam>
		/// <param name="sagaId">The identifier of the <see cref="ISaga{TAuthenticationToken}"/> to retrieve.</param>
		/// <param name="events">
		/// A collection of <see cref="IEvent{TAuthenticationToken}"/> to replay on the retrieved <see cref="ISaga{TAuthenticationToken}"/>.
		/// If null, the <see cref="IEventStore{TAuthenticationToken}"/> will be used to retrieve a list of <see cref="IEvent{TAuthenticationToken}"/> for you.
		/// </param>
#else
		/// <summary>
		/// Retrieves an <see cref="ISaga{TAuthenticationToken}"/> of type <typeparamref name="TSaga"/>,
		/// First using <see cref="TryRestoreSagaFromSnapshotAsync{TSaga}(Guid, TSaga)"/>, otherwise via ISagaRepository{TAuthenticationToken}.Get on <see cref="Repository"/>
		/// Then does rehydration.
		/// </summary>
		/// <typeparam name="TSaga">The <see cref="Type"/> of the <see cref="ISaga{TAuthenticationToken}"/>.</typeparam>
		/// <param name="sagaId">The identifier of the <see cref="ISaga{TAuthenticationToken}"/> to retrieve.</param>
		/// <param name="events">
		/// A collection of <see cref="IEvent{TAuthenticationToken}"/> to replay on the retrieved <see cref="ISaga{TAuthenticationToken}"/>.
		/// If null, the <see cref="IEventStore{TAuthenticationToken}"/> will be used to retrieve a list of <see cref="IEvent{TAuthenticationToken}"/> for you.
		/// </param>
#endif
		public virtual
#if NET40
			TSaga Get
#else
			async Task<TSaga> GetAsync
#endif
				<TSaga>(Guid sagaId, IList<ISagaEvent<TAuthenticationToken>> events = null)
			where TSaga : ISaga<TAuthenticationToken>
		{
			var saga = SagaFactory.Create<TSaga>();
			int snapshotVersion =
#if NET40
				TryRestoreSagaFromSnapshot
#else
				await TryRestoreSagaFromSnapshotAsync
#endif
					(sagaId, saga);
			if (snapshotVersion == -1)
			{
				return
#if NET40
					Repository.Get
#else
					await Repository.GetAsync
#endif
						<TSaga>(sagaId);
			}
			IEnumerable<ISagaEvent<TAuthenticationToken>> theseEvents = events ?? (
#if NET40
				EventStore.Get
#else
				await EventStore.GetAsync
#endif
				<TSaga>(sagaId, false, snapshotVersion)
			).Where(desc => desc.Version > snapshotVersion)
			.Cast<ISagaEvent<TAuthenticationToken>>().ToList();
			saga.LoadFromHistory(theseEvents);

			return saga;
		}

#if NET40
		/// <summary>
		/// Calls <see cref="ISnapshotStrategy{TAuthenticationToken}.IsSnapshotable"/> on <see cref="SnapshotStrategy"/>
		/// If the <typeparamref name="TSaga"/> is snapshot-able <see cref="ISnapshotStore.Get{TSaga}"/> is called on <see cref="SnapshotStore"/>.
		/// The Restore method is then called on
		/// </summary>
		/// <typeparam name="TSaga">The <see cref="Type"/> of the <see cref="ISaga{TAuthenticationToken}"/>.</typeparam>
		/// <param name="id">The identifier of the <see cref="ISaga{TAuthenticationToken}"/> to restore, since the <paramref name="saga"/> may be completely uninitialised.</param>
		/// <param name="saga">The <typeparamref name="TSaga"/></param>
		/// <returns>-1 if no restoration was made, otherwise version number the <typeparamref name="TSaga"/> was rehydrated to.</returns>
		/// <remarks>There may be more events after the snapshot that still need to rehydrated into the <typeparamref name="TSaga"/> after restoration.</remarks>
#else
		/// <summary>
		/// Calls <see cref="ISnapshotStrategy{TAuthenticationToken}.IsSnapshotable"/> on <see cref="SnapshotStrategy"/>
		/// If the <typeparamref name="TSaga"/> is snapshot-able <see cref="ISnapshotStore.GetAsync{TSaga}(Guid)"/> is called on <see cref="SnapshotStore"/>.
		/// The Restore method is then called on
		/// </summary>
		/// <typeparam name="TSaga">The <see cref="Type"/> of the <see cref="ISaga{TAuthenticationToken}"/>.</typeparam>
		/// <param name="id">The identifier of the <see cref="ISaga{TAuthenticationToken}"/> to restore, since the <paramref name="saga"/> may be completely uninitialised.</param>
		/// <param name="saga">The <typeparamref name="TSaga"/></param>
		/// <returns>-1 if no restoration was made, otherwise version number the <typeparamref name="TSaga"/> was rehydrated to.</returns>
		/// <remarks>There may be more events after the snapshot that still need to rehydrated into the <typeparamref name="TSaga"/> after restoration.</remarks>
#endif
		protected virtual
#if NET40
			int TryRestoreSagaFromSnapshot
#else
			async Task<int> TryRestoreSagaFromSnapshotAsync
#endif
				<TSaga>(Guid id, TSaga saga)
		{
			int version = -1;
			if (SnapshotStrategy.IsSnapshotable(typeof(TSaga)))
			{
				Snapshot snapshot =
#if NET40
					SnapshotStore.Get
#else
					await SnapshotStore.GetAsync
#endif
						<TSaga>(id);
				if (snapshot != null)
				{
					saga.AsDynamic().Restore(snapshot);
					version = snapshot.Version;
				}
			}
			return version;
		}

#if NET40
		/// <summary>
		/// Calls <see cref="ISnapshotStrategy{TAuthenticationToken}.ShouldMakeSnapShot(ISaga{TAuthenticationToken}, IEnumerable{ISagaEvent{TAuthenticationToken}})"/> on <see cref="SnapshotStrategy"/>
		/// If the <see cref="ISaga{TAuthenticationToken}"/> is snapshot-able <see cref="SnapshotSaga{TAuthenticationToken,TSnapshot}.GetSnapshot"/> is called
		/// The <see cref="Snapshot.Version"/> is calculated, finally <see cref="ISnapshotStore.Save"/> is called on <see cref="SnapshotStore"/>.
		/// </summary>
		/// <param name="saga">The <see cref="ISaga{TAuthenticationToken}"/> to try and snapshot.</param>
		/// <param name="uncommittedChanges">A collection of uncommited changes to assess. If null the saga will be asked to provide them.</param>
#else
		/// <summary>
		/// Calls <see cref="ISnapshotStrategy{TAuthenticationToken}.ShouldMakeSnapShot(ISaga{TAuthenticationToken}, IEnumerable{ISagaEvent{TAuthenticationToken}})"/> on <see cref="SnapshotStrategy"/>
		/// If the <see cref="ISaga{TAuthenticationToken}"/> is snapshot-able <see cref="SnapshotSaga{TAuthenticationToken,TSnapshot}.GetSnapshot"/> is called
		/// The <see cref="Snapshot.Version"/> is calculated, finally <see cref="ISnapshotStore.SaveAsync(Snapshot)"/> is called on <see cref="SnapshotStore"/>.
		/// </summary>
		/// <param name="saga">The <see cref="ISaga{TAuthenticationToken}"/> to try and snapshot.</param>
		/// <param name="uncommittedChanges">A collection of uncommited changes to assess. If null the saga will be asked to provide them.</param>
#endif
		protected virtual
#if NET40
			void TryMakeSnapshot
#else
			async Task TryMakeSnapshotAsync
#endif
				(ISaga<TAuthenticationToken> saga, IEnumerable<ISagaEvent<TAuthenticationToken>> uncommittedChanges)
		{
			if (!SnapshotStrategy.ShouldMakeSnapShot(saga, uncommittedChanges))
				return;
			dynamic snapshot = saga.AsDynamic().GetSnapshot().RealObject;
			var rSnapshot = snapshot as Snapshot;
			if (rSnapshot != null)
			{
				rSnapshot.Version = saga.Version;
#if NET40
				SnapshotStore.Save
#else
				await SnapshotStore.SaveAsync
#endif
					(rSnapshot);
			}
			else
			{
				snapshot.Version = saga.Version;
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