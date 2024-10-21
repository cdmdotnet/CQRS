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
	public class UnitOfWork<TAuthenticationToken>
		: IUnitOfWork<TAuthenticationToken>
	{
		private IAggregateRepository<TAuthenticationToken> Repository { get; set; }

		private ISnapshotAggregateRepository<TAuthenticationToken> SnapshotRepository { get; set; }

		private Dictionary<Guid, IAggregateDescriptor<TAuthenticationToken>> TrackedAggregates { get; set; }

		/// <summary>
		/// Instantiates a new instance of <see cref="UnitOfWork{TAuthenticationToken}"/>
		/// </summary>
		public UnitOfWork(ISnapshotAggregateRepository<TAuthenticationToken> snapshotRepository, IAggregateRepository<TAuthenticationToken> repository)
			: this(repository)
		{
			if (snapshotRepository == null)
				throw new ArgumentNullException("snapshotRepository");

			SnapshotRepository = snapshotRepository;
		}

		/// <summary>
		/// Instantiates a new instance of <see cref="UnitOfWork{TAuthenticationToken}"/>
		/// </summary>
		public UnitOfWork(IAggregateRepository<TAuthenticationToken> repository)
		{
			if(repository == null)
				throw new ArgumentNullException("repository");

			Repository = repository;
			TrackedAggregates = new Dictionary<Guid, IAggregateDescriptor<TAuthenticationToken>>();
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
				<TAggregateRoot>(TAggregateRoot aggregate, bool useSnapshots = false)
			where TAggregateRoot : IAggregateRoot<TAuthenticationToken>
		{
			if (!IsTracked(aggregate.Id))
			{
				var aggregateDescriptor = new AggregateDescriptor<TAggregateRoot, TAuthenticationToken>
				{
					Aggregate = aggregate,
					Version = aggregate.Version,
					UseSnapshots = useSnapshots
				};
				TrackedAggregates.Add(aggregate.Id, aggregateDescriptor);
			}
			else if (TrackedAggregates[aggregate.Id].Aggregate != (IAggregateRoot<TAuthenticationToken>)aggregate)
				throw new ConcurrencyException(aggregate.Id);
#if NET40
#else
			await Task.CompletedTask;
#endif
		}

		/// <summary>
		/// Get an item from the <see cref="IUnitOfWork{TAuthenticationToken}"/> if it has already been loaded or get it from the <see cref="IAggregateRepository{TAuthenticationToken}"/>.
		/// </summary>
		public virtual
#if NET40
			TAggregateRoot Get
#else
			async Task<TAggregateRoot> GetAsync
#endif
				<TAggregateRoot>(Guid id, int? expectedVersion = null, bool useSnapshots = false)
			where TAggregateRoot : IAggregateRoot<TAuthenticationToken>
		{
			if(IsTracked(id))
			{
				var trackedAggregate = (TAggregateRoot)TrackedAggregates[id].Aggregate;
				if (expectedVersion != null && trackedAggregate.Version != expectedVersion)
					throw new ConcurrencyException(trackedAggregate.Id, expectedVersion.Value, trackedAggregate.Version);
				return trackedAggregate;
			}

			var aggregate =
#if NET40
				(useSnapshots ? SnapshotRepository : Repository).Get
#else
				await (useSnapshots ? SnapshotRepository : Repository).GetAsync
#endif
					<TAggregateRoot>(id);
			if (expectedVersion != null && aggregate.Version != expectedVersion)
				throw new ConcurrencyException(id, expectedVersion.Value, aggregate.Version);
#if NET40
			Add
#else
			await AddAsync
#endif
				(aggregate, useSnapshots);

			return aggregate;
		}

		/// <summary>
		/// Get an item from the <see cref="IUnitOfWork{TAuthenticationToken}"/> up to and including the provided <paramref name="version"/>.
		/// </summary>
		/// <typeparam name="TAggregateRoot">The <see cref="Type"/> of the <see cref="IAggregateRoot{TAuthenticationToken}"/> the <see cref="IEvent{TAuthenticationToken}"/> was raised in.</typeparam>
		/// <param name="id">The <see cref="IAggregateRoot{TAuthenticationToken}.Id"/> of the <see cref="IAggregateRoot{TAuthenticationToken}"/>.</param>
		/// <param name="version">Load events up-to and including from this version</param>
		public virtual
#if NET40
			TAggregateRoot GetToVersion
#else
			async Task<TAggregateRoot> GetToVersionAsync
#endif
				<TAggregateRoot>(Guid id, int version)
			where TAggregateRoot : IAggregateRoot<TAuthenticationToken>
		{
			var aggregate =
#if NET40
				Repository.GetToVersion
#else
				await Repository.GetToVersionAsync
#endif
					<TAggregateRoot>(id, version);

			return aggregate;
		}

		/// <summary>
		/// Get an item from the <see cref="IUnitOfWork{TAuthenticationToken}"/> up to and including the provided <paramref name="versionedDate"/>.
		/// </summary>
		/// <typeparam name="TAggregateRoot">The <see cref="Type"/> of the <see cref="IAggregateRoot{TAuthenticationToken}"/> the <see cref="IEvent{TAuthenticationToken}"/> was raised in.</typeparam>
		/// <param name="id">The <see cref="IAggregateRoot{TAuthenticationToken}.Id"/> of the <see cref="IAggregateRoot{TAuthenticationToken}"/>.</param>
		/// <param name="versionedDate">Load events up-to and including from this <see cref="DateTime"/></param>
		public virtual
#if NET40
			TAggregateRoot GetToDate
#else
			async Task<TAggregateRoot> GetToDateAsync
#endif
				<TAggregateRoot>(Guid id, DateTime versionedDate)
			where TAggregateRoot : IAggregateRoot<TAuthenticationToken>
		{
			var aggregate =
#if NET40
				Repository.GetToDate
#else
				await Repository.GetToDateAsync
#endif
					<TAggregateRoot>(id, versionedDate);

			return aggregate;
		}

		private bool IsTracked(Guid id)
		{
			return TrackedAggregates.ContainsKey(id);
		}

		/// <summary>
		/// Commit any changed <see cref="AggregateRoot{TAuthenticationToken}"/> added to this <see cref="IUnitOfWork{TAuthenticationToken}"/> via Add
		/// into the <see cref="IAggregateRepository{TAuthenticationToken}"/>
		/// </summary>
		public virtual

#if NET40
			void Commit
#else
			async Task CommitAsync
#endif
				()
		{
			foreach (IAggregateDescriptor<TAuthenticationToken> descriptor in TrackedAggregates.Values)
			{
#if NET40
				(descriptor.UseSnapshots ? SnapshotRepository : Repository).Save
#else
				await (descriptor.UseSnapshots ? SnapshotRepository : Repository).SaveAsync
#endif
					(descriptor.Aggregate, descriptor.Version);
			}
			TrackedAggregates.Clear();
		}
	}
}
