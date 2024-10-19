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
	/// Provides basic repository methods for operations with instances of <see cref="ISaga{TAuthenticationToken}"/>.
	/// </summary>
	/// <typeparam name="TAuthenticationToken">The <see cref="Type"/> of authentication token.</typeparam>
	public interface ISagaRepository<TAuthenticationToken>
	{
		/// <summary>
		/// Save and persist the provided <paramref name="saga"/>, optionally providing the version number the <see cref="ISaga{TAuthenticationToken}"/> is expected to be at.
		/// </summary>
		/// <typeparam name="TSaga">The <see cref="Type"/> of the <see cref="ISaga{TAuthenticationToken}"/>.</typeparam>
		/// <param name="saga">The <see cref="ISaga{TAuthenticationToken}"/> to save and persist.</param>
		/// <param name="expectedVersion">The version number the <see cref="ISaga{TAuthenticationToken}"/> is expected to be at.</param>
#if NET40
		void Save
#else
		Task SaveAsync
#endif
			<TSaga>(TSaga saga, int? expectedVersion = null)
			where TSaga : ISaga<TAuthenticationToken>;

		/// <summary>
		/// Retrieves an <see cref="ISaga{TAuthenticationToken}"/> of type <typeparamref name="TSaga"/>.
		/// </summary>
		/// <typeparam name="TSaga">The <see cref="Type"/> of the <see cref="ISaga{TAuthenticationToken}"/>.</typeparam>
		/// <param name="sagaId">The identifier of the <see cref="ISaga{TAuthenticationToken}"/> to retrieve.</param>
		/// <param name="events">
		/// A collection of <see cref="IEvent{TAuthenticationToken}"/> to replay on the retrieved <see cref="ISaga{TAuthenticationToken}"/>.
		/// If null, the <see cref="IEventStore{TAuthenticationToken}"/> will be used to retrieve a list of <see cref="IEvent{TAuthenticationToken}"/> for you.
		/// </param>
#if NET40
		TSaga Get
#else
		Task<TSaga> GetAsync
#endif
			<TSaga>(Guid sagaId, IList<ISagaEvent<TAuthenticationToken>> events = null)
			where TSaga : ISaga<TAuthenticationToken>;
	}
}