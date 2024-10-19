#region Copyright
// // -----------------------------------------------------------------------
// // <copyright company="Chinchilla Software Limited">
// // 	Copyright Chinchilla Software Limited. All rights reserved.
// // </copyright>
// // -----------------------------------------------------------------------
#endregion

using Chinchilla.Logging;
using Cqrs.Configuration;
using Cqrs.Domain;
using System;

namespace Cqrs.Snapshots
{
	/// <summary>
	/// An <see cref="ISaga{TAuthenticationToken}"/> that supports <see cref="Snapshot">snapshots</see> for optimised rehydration.
	/// </summary>
	public abstract class SnapshotSaga<TAuthenticationToken, TSnapshot>
		: Saga<TAuthenticationToken>
		where TSnapshot : Snapshot
	{
		/// <summary>
		/// A constructor for the <see cref="Cqrs.Domain.Factories.IAggregateFactory"/>
		/// </summary>
		protected SnapshotSaga()
			:base()
		{
		}

		/// <summary>
		/// A constructor for the <see cref="Cqrs.Domain.Factories.IAggregateFactory"/>
		/// </summary>
		protected SnapshotSaga(IDependencyResolver dependencyResolver, ILogger logger)
			: base(dependencyResolver, logger)
		{
		}

		/// <summary>
		/// A constructor for the <see cref="Cqrs.Domain.Factories.IAggregateFactory"/>
		/// </summary>
		protected SnapshotSaga(IDependencyResolver dependencyResolver, ILogger logger, Guid rsn)
			: base(dependencyResolver, logger, rsn)
		{
		}

		/// <summary>
		/// Calls <see cref="CreateSnapshot"/> and applies the <see cref="ISaga{TAuthenticationToken}.Id"/> of this instance to the <typeparamref name="TSnapshot"/> result.
		/// </summary>
		public virtual TSnapshot GetSnapshot()
		{
			TSnapshot snapshot = CreateSnapshot();
			snapshot.Id = Id;
			// Version is handled by the repository
			return snapshot;
		}

		/// <summary>
		/// Sets the <see cref="ISaga{TAuthenticationToken}.Id"/> of this instance from <see cref="Snapshot.Id"/> the provided <paramref name="snapshot"/>,
		/// sets the <see cref="ISaga{TAuthenticationToken}.Version"/> of this instance from  <see cref="Snapshot.Version"/> the provided <paramref name="snapshot"/>,
		/// then calls <see cref="RestoreFromSnapshot"/>
		/// </summary>
		/// <param name="snapshot">The <typeparamref name="TSnapshot"/> to rehydrate this instance from.</param>
		public virtual void Restore(TSnapshot snapshot)
		{
			Id = snapshot.Id;
			Version = snapshot.Version;
			RestoreFromSnapshot(snapshot);
		}

		/// <summary>
		/// Create a <typeparamref name="TSnapshot"/> of the current state of this instance.
		/// </summary>
		protected abstract TSnapshot CreateSnapshot();

		/// <summary>
		/// Rehydrate this instance from the provided <paramref name="snapshot"/>.
		/// </summary>
		/// <param name="snapshot">The <typeparamref name="TSnapshot"/> to rehydrate this instance from.</param>
		protected abstract void RestoreFromSnapshot(TSnapshot snapshot);
	}
}