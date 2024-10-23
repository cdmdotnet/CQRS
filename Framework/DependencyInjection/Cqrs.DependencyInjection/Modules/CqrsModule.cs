#region Copyright
// // -----------------------------------------------------------------------
// // <copyright company="Chinchilla Software Limited">
// // 	Copyright Chinchilla Software Limited. All rights reserved.
// // </copyright>
// // -----------------------------------------------------------------------
#endregion

using System;
using System.Reflection;

#if NET462
using cdmdotnet.AutoMapper;
#endif
using Chinchilla.Logging;
using Chinchilla.Logging.Azure.Configuration;
using Chinchilla.Logging.Configuration;
using Chinchilla.StateManagement;
using Chinchilla.StateManagement.Threaded;
using Chinchilla.StateManagement.Web;
using Cqrs.Authentication;
using Cqrs.Bus;
using Cqrs.Configuration;
using Cqrs.Domain;
using Cqrs.Domain.Factories;
using Cqrs.Events;
using Cqrs.Repositories.Queries;
using Cqrs.Snapshots;
using Microsoft.Extensions.DependencyInjection;

namespace Cqrs.DependencyInjection.Modules
{
	/// <summary>
	/// The main <see cref="Module"/> for use with the CQRS package that wires up many of the prerequisites for running CQRS.NET.
	/// </summary>
	/// <typeparam name="TAuthenticationToken">The <see cref="Type"/> of the authentication token.</typeparam>
	/// <typeparam name="TAuthenticationTokenHelper">The <see cref="Type"/> of the authentication token helper.</typeparam>
	public class CqrsModule<TAuthenticationToken, TAuthenticationTokenHelper> : ResolvableModule
		where TAuthenticationTokenHelper : class, IAuthenticationTokenHelper<TAuthenticationToken>
	{
		/// <summary>
		/// Indicates that web based wire-up is required rather than console, WPF or winforms based wire-up.
		/// </summary>
		protected bool SetupForWeb { get; private set; }

		/// <summary>
		/// Indicates that logging should be configured for SQL Server rather than console.
		/// </summary>
		protected bool SetupForSqlLogging { get; private set; }

		/// <summary>
		/// Indicates that the <see cref="ConfigurationManager"/> should be registered automatically.
		/// </summary>
		protected bool RegisterDefaultConfigurationManager { get; private set; }

		/// <summary>
		/// Indicates that the <see cref="ISnapshotStrategy{TAuthenticationToken}"/> should be registered automatically.
		/// </summary>
		protected bool RegisterDefaultSnapshotStrategy { get; private set; }

		/// <summary>
		/// Indicates that the <see cref="ISnapshotAggregateRepository{TAuthenticationToken}"/> should be registered automatically.
		/// </summary>
		protected bool RegisterDefaultSnapshotAggregateRepository { get; private set; }

		/// <summary>
		/// Indicates that the <see cref="ISnapshotSagaRepository{TAuthenticationToken}"/> should be registered automatically.
		/// </summary>
		protected bool RegisterDefaultSnapshotSagaRepository { get; private set; }

		/// <summary>
		/// Indicates that the <see cref="ISnapshotBuilder"/> should be registered automatically.
		/// </summary>
		protected bool RegisterDefaultSnapshotBuilder { get; private set; }

		/// <summary>
		/// Instantiate a new instance of the <see cref="CqrsModule{TAuthenticationToken,TAuthenticationTokenHelper}"/> that uses the provided <paramref name="configurationManager"/>
		/// to read the following configuration settings:
		/// "Cqrs.SetupForWeb": If set to true the system will be configured for hosting in IIS or some other web-server that provides access to System.Web.HttpContext.Current.
		/// "Cqrs.SetupForSqlLogging": If set to true the <see cref="SqlLogger"/> will be bootstrapped by default, otherwise the <see cref="ConsoleLogger"/> will be bootstrapped by default.
		/// "Cqrs.RegisterDefaultConfigurationManager": If set true the <see cref="ConfigurationManager"/> will be registered. If you want to use the Azure one leave this as false (the default) and register it yourself.
		/// "Cqrs.RegisterDefaultSnapshotStrategy": If set true the <see cref="DefaultSnapshotStrategy{TAuthenticationToken}"/> will be registered.
		/// "Cqrs.RegisterDefaultSnapshotAggregateRepository": If set true the <see cref="SnapshotRepository{TAuthenticationToken}"/> will be registered.
		/// "Cqrs.RegisterDefaultSnapshotSagaRepository": If set true the <see cref="SnapshotSagaRepository{TAuthenticationToken}"/> will be registered.
		/// "Cqrs.RegisterDefaultSnapshotBuilder": If set true the <see cref="DefaultSnapshotBuilder"/> will be registered.
		/// </summary>
		/// <param name="configurationManager">The <see cref="IConfigurationManager"/> to use, if one isn't provided then <see cref="ConfigurationManager"/> is instantiate, used and then disposed.</param>
		public CqrsModule(IConfigurationManager configurationManager = null)
		{
			configurationManager = configurationManager ?? Cqrs.Configuration.DependencyResolver.ConfigurationManager ?? new ConfigurationManager();
			if (configurationManager.TryGetSetting("Cqrs.SetupForWeb", out bool setupForWeb))
				SetupForWeb = setupForWeb;
			if (configurationManager.TryGetSetting("Cqrs.SetupForSqlLogging", out bool setupForSqlLogging))
				SetupForSqlLogging = setupForSqlLogging;
			if (configurationManager.TryGetSetting("Cqrs.RegisterDefaultConfigurationManager", out bool registerDefaultConfigurationManager))
				RegisterDefaultConfigurationManager = registerDefaultConfigurationManager;
			if (configurationManager.TryGetSetting("Cqrs.RegisterDefaultSnapshotAggregateRepository", out bool registerDefaultSnapshotAggregateRepository))
				RegisterDefaultSnapshotAggregateRepository = registerDefaultSnapshotAggregateRepository;
			else
				RegisterDefaultSnapshotAggregateRepository = true;
			if (configurationManager.TryGetSetting("Cqrs.RegisterDefaultSnapshotSagaRepository", out bool registerDefaultSnapshotSagaRepository))
				RegisterDefaultSnapshotSagaRepository = registerDefaultSnapshotSagaRepository;
			else
				RegisterDefaultSnapshotSagaRepository = true;
			if (configurationManager.TryGetSetting("Cqrs.RegisterDefaultSnapshotStrategy", out bool registerDefaultSnapshotStrategy))
				RegisterDefaultSnapshotStrategy = registerDefaultSnapshotStrategy;
			else
				RegisterDefaultSnapshotStrategy = true;
			if (configurationManager.TryGetSetting("Cqrs.RegisterDefaultSnapshotBuilder", out bool registerDefaultSnapshotBuilder))
				RegisterDefaultSnapshotBuilder = registerDefaultSnapshotBuilder;
			else
				RegisterDefaultSnapshotBuilder = true;
		}

		/// <summary>
		/// Instantiate a new instance of the <see cref="CqrsModule{TAuthenticationToken,TAuthenticationTokenHelper}"/>.
		/// </summary>
		/// <param name="setupForWeb">Set this to true if you will host this in IIS or some other web-server that provides access to System.Web.HttpContext.Current.</param>
		/// <param name="setupForSqlLogging">Set this to true to use <see cref="SqlLogger"/> otherwise the <see cref="ConsoleLogger"/> will be bootstrapped by default.</param>
		/// <param name="registerDefaultConfigurationManager">Set this to true to use <see cref="ConfigurationManager"/>. If you want to use the Azure one leave this as false (the default) and register it yourself.</param>
		/// <param name="registerDefaultSnapshotAggregateRepository">If set true the <see cref="SnapshotRepository{TAuthenticationToken}"/> will be registered.</param>
		/// <param name="registerDefaultSnapshotSagaRepository">If set true the <see cref="SnapshotSagaRepository{TAuthenticationToken}"/> will be registered.</param>
		/// <param name="registerDefaultSnapshotStrategy">If set true the <see cref="DefaultSnapshotStrategy{TAuthenticationToken}"/> will be registered.</param>
		/// <param name="registerDefaultSnapshotBuilder">If set true the <see cref="DefaultSnapshotBuilder"/> will be registered.</param>
		public CqrsModule(bool setupForWeb, bool setupForSqlLogging, bool registerDefaultConfigurationManager = false, bool registerDefaultSnapshotAggregateRepository = true, bool registerDefaultSnapshotSagaRepository = true, bool registerDefaultSnapshotStrategy = true, bool registerDefaultSnapshotBuilder = true)
		{
			SetupForWeb = setupForWeb;
			SetupForSqlLogging = setupForSqlLogging;
			RegisterDefaultConfigurationManager = registerDefaultConfigurationManager;
			RegisterDefaultSnapshotAggregateRepository = registerDefaultSnapshotAggregateRepository;
			RegisterDefaultSnapshotSagaRepository = registerDefaultSnapshotSagaRepository;
			RegisterDefaultSnapshotStrategy = registerDefaultSnapshotStrategy;
			RegisterDefaultSnapshotBuilder = registerDefaultSnapshotBuilder;
		}

		#region Overrides of Module

		/// <summary>
		/// Loads the module into the kernel.
		/// </summary>
		public override void Load(IServiceCollection services)
		{
			RegisterFactories(services);
			RegisterRepositories(services);
			RegisterQueryBuilders(services);
			RegisterServices(services);
			RegisterCqrsRequirements(services);
#if NET462
			RegisterAutomapperComponents(services);
#endif
			RegisterLoggerComponents(services);
			RegisterCaching(services);
		}

		#endregion

		/// <summary>
		/// Register the all factories
		/// </summary>
		public virtual void RegisterFactories(IServiceCollection services)
		{
			services.AddSingleton<IQueryFactory, QueryFactory>();
			services.AddSingleton<IHashAlgorithmFactory, BuiltInHashAlgorithmFactory>();
		}

		/// <summary>
		/// Register the all components for the <see cref="ILogger"/>
		/// </summary>
		public virtual void RegisterLoggerComponents(IServiceCollection services)
		{
			bool isCorrelationIdHelperBound = IsRegistered<ICorrelationIdHelper>(services);
			if (!isCorrelationIdHelperBound)
			{
				if (SetupForWeb)
					services.AddSingleton<ICorrelationIdHelper, WebCorrelationIdHelper>();
				else
					services.AddSingleton<ICorrelationIdHelper, CorrelationIdHelper>();
			}

			bool isLoggerBound = IsRegistered<ILogger>(services);
			if (!isLoggerBound)
			{
				if (SetupForSqlLogging)
					services.AddSingleton<ILogger, SqlLogger>();
				else
					services.AddSingleton<ILogger, ConsoleLogger>();
			}

			bool isLoggerSettingsBound = IsRegistered<ILoggerSettings>(services);
			if (!isLoggerSettingsBound)
			{
				services.AddSingleton<ILoggerSettings, AzureLoggerSettingsConfiguration>();
			}

			bool isTelemetryHelperBound = IsRegistered<ITelemetryHelper>(services);
			if (!isTelemetryHelperBound)
			{
				services.AddSingleton<ITelemetryHelper, NullTelemetryHelper>();
			}
		}

#if NET462
		/// <summary>
		/// Register the all <see cref="IAutomapHelper"/>
		/// </summary>
		public virtual void RegisterAutomapperComponents(IServiceCollection services)
		{
			services.AddSingleton<IAutomapHelper, AutomapHelper>();
		}
#endif

		/// <summary>
		/// Register the all repositories
		/// </summary>
		public virtual void RegisterRepositories(IServiceCollection services)
		{
		}

		/// <summary>
		/// Register the all query builders
		/// </summary>
		public virtual void RegisterQueryBuilders(IServiceCollection services)
		{
		}

		/// <summary>
		/// Register the all services
		/// </summary>
		public virtual void RegisterServices(IServiceCollection services)
		{
		}

		/// <summary>
		/// Register the all caching stuffs
		/// </summary>
		public virtual void RegisterCaching(IServiceCollection services)
		{
			if (IsRegistered<IContextItemCollectionFactory>(services))
				Unbind<IContextItemCollectionFactory>(services);
			if (IsRegistered<IContextItemCollection>(services))
				Unbind<IContextItemCollection>(services);
			if (SetupForWeb)
			{
				services.AddSingleton<IContextItemCollectionFactory, WebContextItemCollectionFactory>();
				services.AddSingleton<IContextItemCollection, WebContextItemCollection>();
			}
			else
			{
				services.AddSingleton<IContextItemCollectionFactory, ContextItemCollectionFactory>();
				services.AddSingleton<IContextItemCollection, Chinchilla.StateManagement.Threaded.ContextItemCollection>();
			}
		}

		/// <summary>
		/// Register the all Cqrs requirements
		/// </summary>
		public virtual void RegisterCqrsRequirements(IServiceCollection services)
		{
			services.AddTransient<IUnitOfWork<TAuthenticationToken>, UnitOfWork<TAuthenticationToken>>();
			services.AddTransient<ISagaUnitOfWork<TAuthenticationToken>, SagaUnitOfWork<TAuthenticationToken>>();
			services.AddSingleton<IAggregateRepository<TAuthenticationToken>, AggregateRepository<TAuthenticationToken>>();

			if (RegisterDefaultSnapshotAggregateRepository)
				services.AddSingleton<ISnapshotAggregateRepository<TAuthenticationToken>, SnapshotRepository<TAuthenticationToken>>();

			if (RegisterDefaultSnapshotSagaRepository)
				services.AddSingleton<ISnapshotSagaRepository<TAuthenticationToken>, SnapshotSagaRepository<TAuthenticationToken>>();

			services.AddSingleton<ISagaRepository<TAuthenticationToken>, SagaRepository<TAuthenticationToken>>();
			services.AddSingleton<IAggregateFactory, AggregateFactory>();

			services.AddSingleton<IAuthenticationTokenHelper<TAuthenticationToken>, TAuthenticationTokenHelper>();

			services.AddSingleton<IStoreLastEventProcessed, FileBasedLastEventProcessedStore>();

			services.AddSingleton<IBusHelper, BusHelper>();

			if (RegisterDefaultConfigurationManager && Configuration.DependencyResolver.ConfigurationManager != null)
			{
				services.AddSingleton<IConfigurationManager, ConfigurationManager>();
				Configuration.DependencyResolver.ConfigurationManager = Resolve<IConfigurationManager>(services);
			}
			else if (Configuration.DependencyResolver.ConfigurationManager != null)
			{
				if (!IsRegistered<IConfigurationManager>(services))
				{
					services.AddSingleton(Configuration.DependencyResolver.ConfigurationManager);
				}
				if (!IsRegistered<Microsoft.Extensions.Configuration.IConfiguration>(services))
				{
					Type configurationManagerType = Configuration.DependencyResolver.ConfigurationManager.GetType();
					if (configurationManagerType.FullName == "Cqrs.Azure.ConfigurationManager.CloudConfigurationManager")
					{
						PropertyInfo propertyInfo = configurationManagerType.GetProperty("Configuration", BindingFlags.Instance | BindingFlags.NonPublic);
						if (propertyInfo != null)
						{
							var config = propertyInfo.GetValue(Configuration.DependencyResolver.ConfigurationManager) as Microsoft.Extensions.Configuration.IConfiguration;
							if (config != null)
							{
								services.AddSingleton(config);
							}
						}
					}
				}
			}

			if (RegisterDefaultSnapshotStrategy)
				services.AddSingleton<ISnapshotStrategy<TAuthenticationToken>, DefaultSnapshotStrategy<TAuthenticationToken>>();

			if (RegisterDefaultSnapshotBuilder)
				services.AddSingleton<ISnapshotBuilder, DefaultSnapshotBuilder>();
		}
	}
}