#region Copyright
// // -----------------------------------------------------------------------
// // <copyright company="Chinchilla Software Limited">
// // 	Copyright Chinchilla Software Limited. All rights reserved.
// // </copyright>
// // -----------------------------------------------------------------------
#endregion

using System;
using System.Collections.Generic;
using Cqrs.Authentication;
using Cqrs.Configuration;
using Cqrs.Ninject.Azure.ServiceBus.CommandBus.Configuration;
using Cqrs.Ninject.Azure.WebJobs;
using Cqrs.Ninject.Azure.WebJobs.Configuration;
using Cqrs.Ninject.Configuration;

#if NET472
#else
using Microsoft.Extensions.Configuration;
#endif

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ninject;
using Ninject.Modules;

namespace Cqrs.Azure.ConfigurationManager.Tests.Unit
{
	[TestClass]
	public class AzureCommandBusReceiverModuleTest
	{
		[TestMethod]
		public void Load_DefaultBaseModules_NoErrors()
		{
			// Arrange
			var module = new AzureCommandBusReceiverModule<Guid>();
			IList<INinjectModule> modulesToLoad = new List<INinjectModule>
			{
				new CqrsModule<Guid, DefaultAuthenticationTokenHelper>(),
				new WebJobHostModule(),
				module
			};
			NinjectDependencyResolver.ModulesToLoad = modulesToLoad;

			// Act
			NinjectDependencyResolver.Start();
		}

		static IConfigurationManager ConfigurationManager { get; set; }

#if NET472
#else
		static IConfigurationRoot configuration;
#endif

		[TestInitialize]
		public void Setup()
		{
#if NET472
			ConfigurationManager = new CloudConfigurationManager();
#else
			configuration = new ConfigurationBuilder()
//				.SetBasePath(context.FunctionAppDirectory)
				.AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
				.AddEnvironmentVariables()
			.Build();

			CqrsNinjectJobHost<Guid, DefaultAuthenticationTokenHelper>.SetConfigurationManager(configuration);
			ConfigurationManager = new CloudConfigurationManager(configuration);
#endif
		}
	}
}