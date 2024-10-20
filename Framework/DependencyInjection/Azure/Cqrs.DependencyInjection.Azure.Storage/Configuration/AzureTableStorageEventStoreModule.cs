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
using Cqrs.DependencyInjection.Modules;
using Cqrs.Events;
using Cqrs.Snapshots;
using Microsoft.Extensions.DependencyInjection;

namespace Cqrs.DependencyInjection.Azure.Storage.Configuration
{
	/// <summary>
	/// A <see cref="Module"/> that wires up the prerequisites of <see cref="IEventStore{TAuthenticationToken}"/> with table storage.
	/// </summary>
	/// <typeparam name="TAuthenticationToken">The <see cref="Type"/> of the authentication token.</typeparam>
	public class AzureTableStorageEventStoreModule<TAuthenticationToken> : ResolvableModule
	{
		#region Overrides of ResolvableModule

		/// <summary>
		/// Loads the module into the kernel.
		/// </summary>
		public override void Load(IServiceCollection services)
		{
			RegisterFactories(services);
			RegisterEventSerialisationConfiguration(services);
			RegisterEventStore(services);
		}

		#endregion

		/// <summary>
		/// Register the all factories
		/// </summary>
		public virtual void RegisterFactories(IServiceCollection services)
		{
			services.AddSingleton<ITableStorageStoreConnectionStringFactory, TableStorageEventStoreConnectionStringFactory>();
			services.AddSingleton<ITableStorageSnapshotStoreConnectionStringFactory, TableStorageSnapshotStoreConnectionStringFactory>();
		}

		/// <summary>
		/// Register the all event serialisation configurations
		/// </summary>
		public virtual void RegisterEventSerialisationConfiguration(IServiceCollection services)
		{
			services.AddSingleton<IEventBuilder<TAuthenticationToken>, DefaultEventBuilder<TAuthenticationToken>>();
			services.AddSingleton<IEventDeserialiser<TAuthenticationToken>, EventDeserialiser<TAuthenticationToken>>();
			services.AddSingleton<ISnapshotDeserialiser, SnapshotDeserialiser>();
		}

		/// <summary>
		/// Register the <see cref="IEventStore{TAuthenticationToken}"/>
		/// </summary>
		public virtual void RegisterEventStore(IServiceCollection services)
		{
			services.AddSingleton<IEventStore<TAuthenticationToken>, TableStorageEventStore<TAuthenticationToken>>();
			services.AddSingleton<ISnapshotStore, TableStorageSnapshotStore>();
		}
	}
}