#region Copyright
// // -----------------------------------------------------------------------
// // <copyright company="Chinchilla Software Limited">
// // 	Copyright Chinchilla Software Limited. All rights reserved.
// // </copyright>
// // -----------------------------------------------------------------------
#endregion

using System;
using System.Collections.Generic;
using Cqrs.Domain;
using Cqrs.Events;

namespace Cqrs.Snapshots
{
	/// <summary>
	/// Provides information about the ability to make and get <see cref="Snapshot">snapshots</see>.
	/// </summary>
	/// <typeparam name="TAuthenticationToken">The <see cref="Type"/> of the authentication token.</typeparam>
	public interface ISnapshotStrategy<TAuthenticationToken>
	{
		/// <summary>
		/// Indicates if the provided <paramref name="aggregate"/> should have a <see cref="Snapshot"/> made.
		/// This does NOT indicate if the provided <paramref name="aggregate"/> can have a <see cref="Snapshot"/> made or not.
		/// </summary>
		/// <param name="aggregate">The <see cref="IAggregateRoot{TAuthenticationToken}"/> to check.</param>
		/// <param name="uncommittedChanges">A collection of uncommited changes to assess. If null the aggregate will be asked to provide them.</param>
		bool ShouldMakeSnapShot(IAggregateRoot<TAuthenticationToken> aggregate, IEnumerable<IEvent<TAuthenticationToken>> uncommittedChanges = null);

		/// <summary>
		/// Indicates if the provided <paramref name="saga"/> should have a <see cref="Snapshot"/> made.
		/// This does NOT indicate if the provided <paramref name="saga"/> can have a <see cref="Snapshot"/> made or not.
		/// </summary>
		/// <param name="saga">The <see cref="IAggregateRoot{TAuthenticationToken}"/> to check.</param>
		/// <param name="uncommittedChanges">A collection of uncommited changes to assess. If null the aggregate will be asked to provide them.</param>
		bool ShouldMakeSnapShot(ISaga<TAuthenticationToken> saga, IEnumerable<ISagaEvent<TAuthenticationToken>> uncommittedChanges = null);

		/// <summary>
		/// Indicates if the provided <paramref name="aggregateType"/> can have a <see cref="Snapshot"/> made or not.
		/// </summary>
		/// <param name="aggregateType">The <see cref="Type"/> of <see cref="IAggregateRoot{TAuthenticationToken}"/> to check.</param>
		bool IsSnapshotable(Type aggregateType);
	}
}