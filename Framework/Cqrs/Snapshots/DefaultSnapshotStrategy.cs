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
using Cqrs.Events;

namespace Cqrs.Snapshots
{
	/// <summary>
	/// An <see cref="ISnapshotStrategy{TAuthenticationToken}"/> that takes a snapshot every 15 versions.
	/// </summary>
	/// <typeparam name="TAuthenticationToken">The <see cref="Type"/> of the authentication token.</typeparam>
	public class DefaultSnapshotStrategy<TAuthenticationToken>
		: ISnapshotStrategy<TAuthenticationToken>
	{
		private const int SnapshotInterval = 15;

		/// <summary>
		/// Indicates if the <paramref name="aggregateType"/> is able to be snapshotted by checking if the <paramref name="aggregateType"/>
		/// directly inherits <see cref="SnapshotAggregateRoot{TAuthenticationToken,TSnapshot}"/>
		/// </summary>
		/// <param name="aggregateType">The <see cref="Type"/> of <see cref="IAggregateRoot{TAuthenticationToken}"/> to check.</param>
		public virtual bool IsSnapshotable(Type aggregateType)
		{
			if (aggregateType.BaseType == null)
				return false;
			if (aggregateType.BaseType.IsGenericType && new [] { typeof(SnapshotAggregateRoot<,>), typeof(SnapshotSaga<,>) }.Contains(aggregateType.BaseType.GetGenericTypeDefinition()))
				return true;
			return IsSnapshotable(aggregateType.BaseType);
		}

		/// <summary>
		/// Checks <see cref="IsSnapshotable"/> and if it is, also checks if the calculated version number would be exactly dividable by <see cref="GetSnapshotInterval"/>.
		/// </summary>
		/// <param name="aggregate">The <see cref="IAggregateRoot{TAuthenticationToken}"/> to check.</param>
		/// <param name="uncommittedChanges">A collection of uncommited changes to assess. If null the aggregate will be asked to provide them.</param>
		public virtual bool ShouldMakeSnapShot(IAggregateRoot<TAuthenticationToken> aggregate, IEnumerable<IEvent<TAuthenticationToken>> uncommittedChanges = null)
		{
			if (!IsSnapshotable(aggregate.GetType()))
				return false;

			// The reason this isn't as simple as `(aggregate.Version + aggregate.GetUncommittedChanges().Count()) % SnapshotInterval` is
			// because if there are enough uncommited events that this would result in a snapshot, plus some left over, the final state is what will be generated
			// when the snapshot is taken, thus this is a faster and more accurate assessment.
			int limit = (uncommittedChanges ?? aggregate.GetUncommittedChanges()).Count();
			int i = aggregate.Version - limit;
			int snapshotInterval = GetSnapshotInterval();
			
			for (int j = 0; j < limit; j++)
				if (++i % snapshotInterval == 0 && i != 0)
					return true;
			return false;
		}

		/// <summary>
		/// Checks <see cref="IsSnapshotable"/> and if it is, also checks if the calculated version number would be exactly dividable by <see cref="GetSnapshotInterval"/>.
		/// </summary>
		/// <param name="saga">The <see cref="ISaga{TAuthenticationToken}"/> to check.</param>
		/// <param name="uncommittedChanges">A collection of uncommited changes to assess. If null the aggregate will be asked to provide them.</param>
		public bool ShouldMakeSnapShot(ISaga<TAuthenticationToken> saga, IEnumerable<ISagaEvent<TAuthenticationToken>> uncommittedChanges = null)
		{
			if (!IsSnapshotable(saga.GetType()))
				return false;

			// The reason this isn't as simple as `(aggregate.Version + aggregate.GetUncommittedChanges().Count()) % SnapshotInterval` is
			// because if there are enough uncommited events that this would result in a snapshot, plus some left over, the final state is what will be generated
			// when the snapshot is taken, thus this is a faster and more accurate assessment.
			int limit = (uncommittedChanges ?? saga.GetUncommittedChanges()).Count();
			int i = saga.Version - limit;
			int snapshotInterval = GetSnapshotInterval();

			for (int j = 0; j < limit; j++)
				if (++i % snapshotInterval == 0 && i != 0)
					return true;
			return false;
		}

		/// <summary>
		/// Returns the value of <see cref="SnapshotInterval"/>.
		/// </summary>
		protected virtual int GetSnapshotInterval()
		{
			return SnapshotInterval;
		}
	}
}