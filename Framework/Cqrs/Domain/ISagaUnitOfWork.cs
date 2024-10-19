#region Copyright
// // -----------------------------------------------------------------------
// // <copyright company="Chinchilla Software Limited">
// // 	Copyright Chinchilla Software Limited. All rights reserved.
// // </copyright>
// // -----------------------------------------------------------------------
#endregion

using System;

#if NET472
#else
using System.Threading.Tasks;
#endif

namespace Cqrs.Domain
{
	/// <summary>
	/// This is a Unit of Work for sagas
	/// </summary>
	public interface ISagaUnitOfWork<TAuthenticationToken>
	{
		/// <summary>
		/// Add an item into the <see cref="ISagaUnitOfWork{TAuthenticationToken}"/> ready to be committed.
		/// </summary>
#if NET40
		void Add
#else
		Task AddAsync
#endif
			<TSaga>(TSaga saga, bool useSnapshots = false)
			where TSaga : ISaga<TAuthenticationToken>;

		/// <summary>
		/// Get an item from the <see cref="ISagaUnitOfWork{TAuthenticationToken}"/> if it has already been loaded.
		/// </summary>
#if NET40
		TSaga Get
#else
		Task<TSaga> GetAsync
#endif
			<TSaga>(Guid id, int? expectedVersion = null, bool useSnapshots = false)
			where TSaga : ISaga<TAuthenticationToken>;

		/// <summary>
		/// Commit any changed <see cref="Saga{TAuthenticationToken}"/> added to this <see cref="ISagaUnitOfWork{TAuthenticationToken}"/> via Add
		/// </summary>

#if NET40
		void Commit
#else
		Task CommitAsync
#endif
			();
	}
}