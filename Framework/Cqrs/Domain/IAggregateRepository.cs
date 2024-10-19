#region Copyright
// // -----------------------------------------------------------------------
// // <copyright company="Chinchilla Software Limited">
// // 	Copyright Chinchilla Software Limited. All rights reserved.
// // </copyright>
// // -----------------------------------------------------------------------
#endregion

using System;
using System.Collections.Generic;
using Cqrs.Events;

#if NET472
#else
using System.Threading.Tasks;
#endif

namespace Cqrs.Domain
{
	/// <summary>
	/// Provides basic repository methods for operations with instances of <see cref="IAggregateRoot{TAuthenticationToken}"/>.
	/// </summary>
	/// <typeparam name="TAuthenticationToken">The <see cref="Type"/> of authentication token.</typeparam>
	public interface IAggregateRepository<TAuthenticationToken>
	{
		/// <summary>
		/// Save and persist the provided <paramref name="aggregate"/>, optionally providing the version number the <see cref="IAggregateRoot{TAuthenticationToken}"/> is expected to be at.
		/// </summary>
		/// <typeparam name="TAggregateRoot">The <see cref="Type"/> of the <see cref="IAggregateRoot{TAuthenticationToken}"/>.</typeparam>
		/// <param name="aggregate">The <see cref="IAggregateRoot{TAuthenticationToken}"/> to save and persist.</param>
		/// <param name="expectedVersion">The version number the <see cref="IAggregateRoot{TAuthenticationToken}"/> is expected to be at.</param>
#if NET40
		void Save
#else
		Task SaveAsync
#endif
			<TAggregateRoot>(TAggregateRoot aggregate, int? expectedVersion = null)
			where TAggregateRoot : IAggregateRoot<TAuthenticationToken>;

		/// <summary>
		/// Retrieves an <see cref="IAggregateRoot{TAuthenticationToken}"/> of type <typeparamref name="TAggregateRoot"/>.
		/// </summary>
		/// <typeparam name="TAggregateRoot">The <see cref="Type"/> of the <see cref="IAggregateRoot{TAuthenticationToken}"/>.</typeparam>
		/// <param name="aggregateId">The identifier of the <see cref="IAggregateRoot{TAuthenticationToken}"/> to retrieve.</param>
		/// <param name="events">
		/// A collection of <see cref="IEvent{TAuthenticationToken}"/> to replay on the retrieved <see cref="IAggregateRoot{TAuthenticationToken}"/>.
		/// If null, the <see cref="IEventStore{TAuthenticationToken}"/> will be used to retrieve a list of <see cref="IEvent{TAuthenticationToken}"/> for you.
		/// </param>
#if NET40
		TAggregateRoot Get
#else
		Task<TAggregateRoot> GetAsync
#endif
			<TAggregateRoot>(Guid aggregateId, IList<IEvent<TAuthenticationToken>> events = null)
			where TAggregateRoot : IAggregateRoot<TAuthenticationToken>;

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
#if NET40
		TAggregateRoot GetToVersion
#else
		Task<TAggregateRoot> GetToVersionAsync
#endif
			<TAggregateRoot>(Guid aggregateId, int version, IList<IEvent<TAuthenticationToken>> events = null)
			where TAggregateRoot : IAggregateRoot<TAuthenticationToken>;

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
#if NET40
		TAggregateRoot GetToDate
#else
		Task<TAggregateRoot> GetToDateAsync
#endif
			<TAggregateRoot>(Guid aggregateId, DateTime versionedDate, IList<IEvent<TAuthenticationToken>> events = null)
			where TAggregateRoot : IAggregateRoot<TAuthenticationToken>;
	}
}