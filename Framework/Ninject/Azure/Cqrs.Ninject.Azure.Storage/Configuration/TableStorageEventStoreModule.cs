#region Copyright
// // -----------------------------------------------------------------------
// // <copyright company="Chinchilla Software Limited">
// // 	Copyright Chinchilla Software Limited. All rights reserved.
// // </copyright>
// // -----------------------------------------------------------------------
#endregion

using System;
using Cqrs.Azure.Storage;
using Cqrs.Azure.Storage.Events;
using Cqrs.Events;
using Cqrs.Snapshots;
using Ninject.Modules;

namespace Cqrs.Ninject.Azure.Storage.Configuration
{
	/// <summary>
	/// A <see cref="INinjectModule"/> that wires up the prerequisites of <see cref="IEventStore{TAuthenticationToken}"/> with table storage.
	/// </summary>
	/// <typeparam name="TAuthenticationToken">The <see cref="Type"/> of the authentication token.</typeparam>
	public class TableStorageEventStoreModule<TAuthenticationToken> : NinjectModule
	{
		#region Overrides of NinjectModule

		/// <summary>
		/// Loads the module into the kernel.
		/// </summary>
		public override void Load()
		{
			RegisterFactories();
			RegisterEventSerialisationConfiguration();
			RegisterEventStore();
		}

		#endregion

		/// <summary>
		/// Register the all factories
		/// </summary>
		public virtual void RegisterFactories()
		{
			Bind<ITableStorageStoreConnectionStringFactory>()
				.To<TableStorageEventStoreConnectionStringFactory>()
				.InSingletonScope();
			Bind<ITableStorageSnapshotStoreConnectionStringFactory>()
				.To<TableStorageSnapshotStoreConnectionStringFactory>()
				.InSingletonScope();
		}

		/// <summary>
		/// Register the all event serialisation configurations
		/// </summary>
		public virtual void RegisterEventSerialisationConfiguration()
		{
			Bind<IEventBuilder<TAuthenticationToken>>()
				.To<DefaultEventBuilder<TAuthenticationToken>>()
				.InSingletonScope();
			Bind<IEventDeserialiser<TAuthenticationToken>>()
				.To<EventDeserialiser<TAuthenticationToken>>()
				.InSingletonScope();
			Bind<ISnapshotDeserialiser>()
				.To<SnapshotDeserialiser>()
				.InSingletonScope();
		}

		/// <summary>
		/// Register the <see cref="IEventStore{TAuthenticationToken}"/>
		/// </summary>
		public virtual void RegisterEventStore()
		{
			Bind<IEventStore<TAuthenticationToken>>()
				.To<TableStorageEventStore<TAuthenticationToken>>()
				.InSingletonScope();
			Bind<ISnapshotStore>()
				.To<TableStorageSnapshotStore>()
				.InSingletonScope();
		}
	}
}