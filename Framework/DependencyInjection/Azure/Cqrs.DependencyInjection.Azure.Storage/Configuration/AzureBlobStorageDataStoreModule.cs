#region Copyright
// // -----------------------------------------------------------------------
// // <copyright company="Chinchilla Software Limited">
// // 	Copyright Chinchilla Software Limited. All rights reserved.
// // </copyright>
// // -----------------------------------------------------------------------
#endregion

using Cqrs.Azure.Storage.DataStores;
using Cqrs.DependencyInjection.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace Cqrs.DependencyInjection.Azure.Storage.Configuration
{
	/// <summary>
	/// A <see cref="Module"/> that wires up <see cref="BlobStorageDataStoreConnectionStringFactory"/> as the <see cref="IBlobStorageDataStoreConnectionStringFactory"/>.
	/// </summary>
	public class AzureBlobStorageDataStoreModule : ResolvableModule
	{
		#region Overrides of ResolvableModule

		/// <summary>
		/// Loads the module into the kernel.
		/// </summary>
		public override void Load(IServiceCollection services)
		{
			RegisterFactories(services);
		}

		#endregion

		/// <summary>
		/// Register all the factories
		/// </summary>
		public virtual void RegisterFactories(IServiceCollection services)
		{
			services.AddSingleton<IBlobStorageDataStoreConnectionStringFactory, BlobStorageDataStoreConnectionStringFactory>();
		}
	}
}