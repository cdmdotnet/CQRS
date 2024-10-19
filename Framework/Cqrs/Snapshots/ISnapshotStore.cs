#region Copyright
// // -----------------------------------------------------------------------
// // <copyright company="Chinchilla Software Limited">
// // 	Copyright Chinchilla Software Limited. All rights reserved.
// // </copyright>
// // -----------------------------------------------------------------------
#endregion

using System;
using Cqrs.Domain;

#if NET472
#else
using System.Threading.Tasks;
#endif

namespace Cqrs.Snapshots
{
	/// <summary>
	/// Stores the most recent <see cref="Snapshot">snapshots</see> for replay and <see cref="IAggregateRoot{TAuthenticationToken}"/> rehydration on a <see cref="SnapshotAggregateRoot{TAuthenticationToken,TSnapshot}"/>.
	/// </summary>
	public interface ISnapshotStore
	{
		/// <summary>
		/// Get the latest <see cref="Snapshot"/> from storage.
		/// </summary>
		/// <typeparam name="TAggregateRoot">The <see cref="Type"/> of <see cref="IAggregateRoot{TAuthenticationToken}"/> to find a snapshot for.</typeparam>
		/// <param name="id">The identifier of the <see cref="IAggregateRoot{TAuthenticationToken}"/> to get the most recent <see cref="Snapshot"/> of.</param>
		/// <returns>The most recent <see cref="Snapshot"/> of</returns>
#if NET40
		Snapshot Get
#else
		Task<Snapshot> GetAsync
#endif
			<TAggregateRoot>(Guid id);

		/// <summary>
		/// Get the latest <see cref="Snapshot"/> from storage.
		/// </summary>
		/// <param name="aggregateRootType">The <see cref="Type"/> of <see cref="IAggregateRoot{TAuthenticationToken}"/> to find a snapshot for.</param>
		/// <param name="id">The identifier of the <see cref="IAggregateRoot{TAuthenticationToken}"/> to get the most recent <see cref="Snapshot"/> of.</param>
		/// <returns>The most recent <see cref="Snapshot"/> of</returns>
#if NET40
		Snapshot Get
#else
		Task<Snapshot> GetAsync
#endif
			(Type aggregateRootType, Guid id);

		/// <summary>
		/// Saves the provided <paramref name="snapshot"/> into storage.
		/// </summary>
		/// <param name="snapshot">the <see cref="Snapshot"/> to save and store.</param>
#if NET40
		void Save
#else
		Task SaveAsync
#endif
			(Snapshot snapshot);
	}
}