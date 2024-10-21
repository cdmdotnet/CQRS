#region Copyright
// // -----------------------------------------------------------------------
// // <copyright company="Chinchilla Software Limited">
// // 	Copyright Chinchilla Software Limited. All rights reserved.
// // </copyright>
// // -----------------------------------------------------------------------
#endregion

using System;
using System.Collections.Generic;
using Cqrs.Domain.Exceptions;
using Cqrs.Events;

#if NET40
#else
using System.Threading.Tasks;
#endif

namespace Cqrs.Domain
{
	/// <summary>
	/// Provides a basic container to control when <see cref="IEvent{TAuthenticationToken}">events</see> are store in an <see cref="IEventStore{TAuthenticationToken}"/> and then published on an <see cref="IEventPublisher{TAuthenticationToken}"/>.
	/// </summary>
	/// <remarks>
	/// This shouldn't normally be used as a singleton.
	/// </remarks>
	public class SagaUnitOfWork<TAuthenticationToken> : ISagaUnitOfWork<TAuthenticationToken>
	{
		private ISagaRepository<TAuthenticationToken> Repository { get; set; }

		private ISnapshotSagaRepository<TAuthenticationToken> SnapshotRepository { get; set; }

		private Dictionary<Guid, ISagaDescriptor<TAuthenticationToken>> TrackedSagas { get; set; }

		/// <summary>
		/// Instantiates a new instance of <see cref="SagaUnitOfWork{TAuthenticationToken}"/>
		/// </summary>
		public SagaUnitOfWork(ISnapshotSagaRepository<TAuthenticationToken> snapshotRepository, ISagaRepository<TAuthenticationToken> repository)
			: this(repository)
		{
			if (snapshotRepository == null)
				throw new ArgumentNullException("snapshotRepository");

			SnapshotRepository = snapshotRepository;
		}

		/// <summary>
		/// Instantiates a new instance of <see cref="SagaUnitOfWork{TAuthenticationToken}"/>
		/// </summary>
		public SagaUnitOfWork(ISagaRepository<TAuthenticationToken> repository)
		{
			if(repository == null)
				throw new ArgumentNullException("repository");

			Repository = repository;
			TrackedSagas = new Dictionary<Guid, ISagaDescriptor<TAuthenticationToken>>();
		}

		/// <summary>
		/// Add an item into the <see cref="IUnitOfWork{TAuthenticationToken}"/> ready to be committed.
		/// </summary>
		public virtual
#if NET40
			void Add
#else
			async Task AddAsync
#endif
				<TSaga>(TSaga saga, bool useSnapshots = false)
			where TSaga : ISaga<TAuthenticationToken>
		{
			if (!IsTracked(saga.Id))
			{
				var sagaDescriptor = new SagaDescriptor<TSaga, TAuthenticationToken>
				{
					Saga = saga,
					Version = saga.Version,
					UseSnapshots = useSnapshots
				};
				TrackedSagas.Add(saga.Id, sagaDescriptor);
			}
			else if (TrackedSagas[saga.Id].Saga != (ISaga<TAuthenticationToken>)saga)
				throw new ConcurrencyException(saga.Id);
#if NET40
#else
			await Task.CompletedTask;
#endif
		}

		/// <summary>
		/// Get an item from the <see cref="IUnitOfWork{TAuthenticationToken}"/> if it has already been loaded or get it from the <see cref="ISagaRepository{TAuthenticationToken}"/>.
		/// </summary>
		public virtual
#if NET40
			TSaga Get
#else
			async Task<TSaga> GetAsync
#endif
				<TSaga>(Guid id, int? expectedVersion = null, bool useSnapshots = false)
			where TSaga : ISaga<TAuthenticationToken>
		{
			if(IsTracked(id))
			{
				var trackedSaga = (TSaga)TrackedSagas[id].Saga;
				if (expectedVersion != null && trackedSaga.Version != expectedVersion)
					throw new ConcurrencyException(trackedSaga.Id, expectedVersion.Value, trackedSaga.Version);
				return trackedSaga;
			}

			var saga =
#if NET40
				(useSnapshots ? SnapshotRepository : Repository).Get
#else
				await (useSnapshots ? SnapshotRepository : Repository).GetAsync
#endif
					<TSaga>(id);
			if (expectedVersion != null && saga.Version != expectedVersion)
				throw new ConcurrencyException(id, expectedVersion.Value, saga.Version);
#if NET40
			Add
#else
			await AddAsync
#endif
				(saga, useSnapshots);

			return saga;
		}

		private bool IsTracked(Guid id)
		{
			return TrackedSagas.ContainsKey(id);
		}

		/// <summary>
		/// Commit any changed <see cref="Saga{TAuthenticationToken}"/> added to this <see cref="IUnitOfWork{TAuthenticationToken}"/> via Add
		/// into the <see cref="ISagaRepository{TAuthenticationToken}"/>
		/// </summary>
		public virtual

#if NET40
			void Commit
#else
			async Task CommitAsync
#endif
				()
		{
			foreach (ISagaDescriptor<TAuthenticationToken> descriptor in TrackedSagas.Values)
			{
#if NET40
				(descriptor.UseSnapshots ? SnapshotRepository : Repository).Save
#else
				await (descriptor.UseSnapshots ? SnapshotRepository : Repository).SaveAsync
#endif
					(descriptor.Saga, descriptor.Version);
			}
			TrackedSagas.Clear();
		}
	}
}