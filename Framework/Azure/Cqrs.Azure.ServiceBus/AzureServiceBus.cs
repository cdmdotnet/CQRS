#region Copyright
// // -----------------------------------------------------------------------
// // <copyright company="Chinchilla Software Limited">
// // 	Copyright Chinchilla Software Limited. All rights reserved.
// // </copyright>
// // -----------------------------------------------------------------------
#endregion

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Chinchilla.Logging;
using Cqrs.Authentication;
using Cqrs.Bus;
using Cqrs.Configuration;
using Cqrs.Exceptions;
using Cqrs.Messages;

#if NETSTANDARD2_0 || NET48_OR_GREATER
using Azure.Messaging.ServiceBus;
using BrokeredMessage = Azure.Messaging.ServiceBus.ServiceBusReceivedMessage;
using IMessageReceiver = Azure.Messaging.ServiceBus.ServiceBusProcessor;
using Manager = Azure.Messaging.ServiceBus.Administration.ServiceBusAdministrationClient;
using TopicClient = Azure.Messaging.ServiceBus.ServiceBusSender;
using TopicDescription = Azure.Messaging.ServiceBus.Administration.CreateTopicOptions;
using Azure.Identity;
using Azure.Messaging.ServiceBus.Administration;
#else
using Microsoft.ServiceBus;
using Microsoft.ServiceBus.Messaging;
using Manager = Microsoft.ServiceBus.NamespaceManager;
using IMessageReceiver = Microsoft.ServiceBus.Messaging.SubscriptionClient;
#endif
#if NET462
using Microsoft.Identity.Client;
#endif

namespace Cqrs.Azure.ServiceBus
{
	/// <summary>
	/// An <see cref="AzureBus{TAuthenticationToken}"/> that uses Azure Service Bus.
	/// </summary>
	/// <typeparam name="TAuthenticationToken">The <see cref="Type"/> of the authentication token.</typeparam>
	/// <remarks>
	/// https://markheath.net/post/migrating-to-new-servicebus-sdk
	/// https://docs.microsoft.com/en-us/azure/service-bus-messaging/service-bus-dotnet-how-to-use-topics-subscriptions#receive-messages-from-the-subscription
	/// https://stackoverflow.com/questions/47427361/azure-service-bus-read-messages-sent-by-net-core-2-with-brokeredmessage-getbo
	/// https://docs.microsoft.com/en-us/azure/service-bus-messaging/service-bus-dotnet-get-started-with-queues
	/// </remarks>
	public abstract class AzureServiceBus<TAuthenticationToken>
		: AzureBus<TAuthenticationToken>
	{
		/// <summary>
		/// Gets the private <see cref="TopicClient"/> publisher.
		/// </summary>
		protected TopicClient PrivateServiceBusPublisher { get; private set; }

		/// <summary>
		/// Gets the public <see cref="TopicClient"/> publisher.
		/// </summary>
		protected TopicClient PublicServiceBusPublisher { get; private set; }

		/// <summary>
		/// Gets the private <see cref="IMessageReceiver"/> receivers.
		/// </summary>
		protected IDictionary<int, IMessageReceiver> PrivateServiceBusReceivers { get; private set; }

		/// <summary>
		/// Gets the public <see cref="IMessageReceiver"/> receivers.
		/// </summary>
		protected IDictionary<int, IMessageReceiver> PublicServiceBusReceivers { get; private set; }

		/// <summary>
		/// The name of the private topic.
		/// </summary>
		protected string PrivateTopicName { get; set; }

		/// <summary>
		/// The name of the public topic.
		/// </summary>
		protected string PublicTopicName { get; set; }

		/// <summary>
		/// The name of the subscription in the private topic.
		/// </summary>
		protected string PrivateTopicSubscriptionName { get; set; }

		/// <summary>
		/// The name of the subscription in the public topic.
		/// </summary>
		protected string PublicTopicSubscriptionName { get; set; }

		/// <summary>
		/// The configuration key for the message bus connection string as used by <see cref="IConfigurationManager"/>.
		/// </summary>
		protected abstract string MessageBusConnectionStringConfigurationKey { get; }

		/// <summary>
		/// The configuration key for the message bus connection endpoint as used by <see cref="IConfigurationManager"/>, when using RBAC.
		/// </summary>
		protected abstract string MessageBusConnectionEndpointConfigurationKey { get; }

		/// <summary>
		/// The configuration key for the message bus connection Application Id as used by <see cref="IConfigurationManager"/>, when using RBAC.
		/// </summary>
		protected abstract string MessageBusConnectionApplicationIdConfigurationKey { get; }

		/// <summary>
		/// The configuration key for the message bus connection Client Key/Secret as used by <see cref="IConfigurationManager"/>, when using RBAC.
		/// </summary>
		protected abstract string MessageBusConnectionClientKeyConfigurationKey { get; }

		/// <summary>
		/// The configuration key for the message bus connection Tenant Id as used by <see cref="IConfigurationManager"/>, when using RBAC.
		/// </summary>
		protected abstract string MessageBusConnectionTenantIdConfigurationKey { get; }

		/// <summary>
		/// The configuration key for the signing token as used by <see cref="IConfigurationManager"/>.
		/// </summary>
		protected abstract string SigningTokenConfigurationKey { get; }

		/// <summary>
		/// The configuration key for the name of the private topic as used by <see cref="IConfigurationManager"/>.
		/// </summary>
		protected abstract string PrivateTopicNameConfigurationKey { get; }

		/// <summary>
		/// The configuration key for the name of the public topic as used by <see cref="IConfigurationManager"/>.
		/// </summary>
		protected abstract string PublicTopicNameConfigurationKey { get; }

		/// <summary>
		/// The default name of the private topic if no <see cref="IConfigurationManager"/> value is set.
		/// </summary>
		protected abstract string DefaultPrivateTopicName { get; }

		/// <summary>
		/// The default name of the public topic if no <see cref="IConfigurationManager"/> value is set.
		/// </summary>
		protected abstract string DefaultPublicTopicName { get; }

		/// <summary>
		/// The configuration key for the name of the subscription in the private topic as used by <see cref="IConfigurationManager"/>.
		/// </summary>
		protected abstract string PrivateTopicSubscriptionNameConfigurationKey { get; }

		/// <summary>
		/// The configuration key for the name of the subscription in the public topic as used by <see cref="IConfigurationManager"/>.
		/// </summary>
		protected abstract string PublicTopicSubscriptionNameConfigurationKey { get; }

		/// <summary>
		/// The configuration key that
		/// specifies if an <see cref="Exception"/> is thrown if the network lock is lost
		/// as used by <see cref="IConfigurationManager"/>.
		/// </summary>
		protected abstract string ThrowExceptionOnReceiverMessageLockLostExceptionDuringCompleteConfigurationKey { get; }

		/// <summary>
		/// Specifies if an <see cref="Exception"/> is thrown if the network lock is lost.
		/// </summary>
		protected bool ThrowExceptionOnReceiverMessageLockLostExceptionDuringComplete { get; set; }

		/// <summary>
		/// The default name of the subscription in the private topic if no <see cref="IConfigurationManager"/> value is set.
		/// </summary>
		protected const string DefaultPrivateTopicSubscriptionName = "Root";

		/// <summary>
		/// The default name of the subscription in the public topic if no <see cref="IConfigurationManager"/> value is set.
		/// </summary>
		protected const string DefaultPublicTopicSubscriptionName = "Root";

#if NETSTANDARD2_0 || NET48_OR_GREATER
		/// <summary>
		/// The <see cref="Func{ProcessMessageEventArgs}">handler</see> used for <see cref="ServiceBusProcessor.OnProcessMessageAsync(ProcessMessageEventArgs)"/> on each receiver.
		/// </summary>
		protected virtual Func<ProcessMessageEventArgs, Task> ReceiverMessageHandler { get; set; }
#else
		/// <summary>
		/// The <see cref="Action{TBrokeredMessage}">handler</see> used for <see cref="IMessageReceiver.OnMessage(System.Action{Microsoft.ServiceBus.Messaging.BrokeredMessage}, OnMessageOptions)"/> on each receiver.
		/// </summary>
		protected Action<IMessageReceiver, BrokeredMessage> ReceiverMessageHandler { get; set; }
#endif

#if NETSTANDARD2_0 || NET48_OR_GREATER
		/// <summary>
		/// The <see cref="ServiceBusProcessorOptions" /> used.
		/// </summary>
		protected virtual ServiceBusProcessorOptions ReceiverMessageHandlerOptions { get; set; }
#else
		/// <summary>
		/// The <see cref="OnMessageOptions" /> used for <see cref="IMessageReceiver.OnMessage(System.Action{Microsoft.ServiceBus.Messaging.BrokeredMessage}, OnMessageOptions)"/> on each receiver.
		/// </summary>
		protected OnMessageOptions ReceiverMessageHandlerOptions { get; set; }
#endif

		/// <summary>
		/// Gets the <see cref="IBusHelper"/>.
		/// </summary>
		protected IBusHelper BusHelper { get; private set; }

		/// <summary>
		/// Gets the <see cref="IAzureBusHelper{TAuthenticationToken}"/>.
		/// </summary>
		protected IAzureBusHelper<TAuthenticationToken> AzureBusHelper { get; private set; }

		/// <summary>
		/// Gets the <see cref="ITelemetryHelper"/>.
		/// </summary>
		protected ITelemetryHelper TelemetryHelper { get; set; }

		/// <summary>
		/// The maximum number of time a retry is tried if a <see cref="System.TimeoutException"/> is thrown while sending messages.
		/// </summary>
		protected short TimeoutOnSendRetryMaximumCount { get; private set; }

		/// <summary>
		/// The <see cref="IHashAlgorithmFactory"/> to use to sign messages.
		/// </summary>
		protected IHashAlgorithmFactory Signer { get; private set; }

		/// <summary>
		/// A list of namespaces to exclude when trying to automatically determine the container.
		/// </summary>
		protected IList<string> ExclusionNamespaces { get; private set; }

		private IList<string> TaskRelatedMethodNames { get; }

		private Regex ContainerNameMatcher { get; }

#if NET462
		/// <summary>
		/// Gets an access token from Active Directory when using RBAC based connections.
		/// </summary>
		protected AzureActiveDirectoryTokenProvider.AuthenticationCallback GetActiveDirectoryToken { get; private set; }
#endif

		/// <summary>
		/// Instantiates a new instance of <see cref="AzureServiceBus{TAuthenticationToken}"/>
		/// </summary>
		protected AzureServiceBus(IConfigurationManager configurationManager, IMessageSerialiser<TAuthenticationToken> messageSerialiser, IAuthenticationTokenHelper<TAuthenticationToken> authenticationTokenHelper, ICorrelationIdHelper correlationIdHelper, ILogger logger, IAzureBusHelper<TAuthenticationToken> azureBusHelper, IBusHelper busHelper, IHashAlgorithmFactory hashAlgorithmFactory, bool isAPublisher)
			: base(configurationManager, messageSerialiser, authenticationTokenHelper, correlationIdHelper, logger, isAPublisher)
		{
			AzureBusHelper = azureBusHelper;
			BusHelper = busHelper;
			Signer = hashAlgorithmFactory;

			TelemetryHelper = new NullTelemetryHelper();
			PrivateServiceBusReceivers = new Dictionary<int, IMessageReceiver>();
			PublicServiceBusReceivers = new Dictionary<int, IMessageReceiver>();
			TimeoutOnSendRetryMaximumCount = 1;

			string timeoutOnSendRetryMaximumCountValue;
			short timeoutOnSendRetryMaximumCount;
			if (ConfigurationManager.TryGetSetting("Cqrs.Azure.Servicebus.TimeoutOnSendRetryMaximumCount", out timeoutOnSendRetryMaximumCountValue) && !string.IsNullOrWhiteSpace(timeoutOnSendRetryMaximumCountValue) && short.TryParse(timeoutOnSendRetryMaximumCountValue, out timeoutOnSendRetryMaximumCount))
				TimeoutOnSendRetryMaximumCount = timeoutOnSendRetryMaximumCount;

			ExclusionNamespaces = new SynchronizedCollection<string> { "Cqrs", "System" };
			TaskRelatedMethodNames = new List<string>
			{
				"MoveNext",
				"Start"
			};
			ContainerNameMatcher = new Regex("^(.)+?>", RegexOptions.IgnoreCase);

#if NET462
			InstantiateActiveDirectoryToken();
#endif
		}

#if NET462
		/// <summary>
		/// Setup <see cref="GetActiveDirectoryToken"/>
		/// </summary>
		protected virtual void InstantiateActiveDirectoryToken()
		{
			GetActiveDirectoryToken = async (audience, authority, state) =>
			{
				string applicationId = ConfigurationManager.GetSetting(MessageBusConnectionApplicationIdConfigurationKey);
				string clientKey = ConfigurationManager.GetSetting(MessageBusConnectionClientKeyConfigurationKey);

				IConfidentialClientApplication app = ConfidentialClientApplicationBuilder.Create(applicationId)
					.WithAuthority(authority)
					.WithClientSecret(clientKey)
					.Build();

				var authResult = await app
					.AcquireTokenForClient(new string[] { "https://servicebus.azure.net/.default" })
					.ExecuteAsync();

				return authResult.AccessToken;
			};
		}
#endif

#if NETSTANDARD2_0 || NET48_OR_GREATER
		/// <summary>
		/// The underlaying <see cref="ServiceBusClient"/>.
		/// Do not use this directly. Use <see cref="GetOrCreateClientAsync"/>
		/// </summary>
		private ServiceBusClient ServiceBusClient { get; set; }

		private static SemaphoreSlim lockObject = new SemaphoreSlim(1, 1);
		/// <summary>
		/// Get the current <see cref="ServiceBusClient"/> or creates and returns one if it has not yet been created
		/// </summary>
		protected virtual async Task<ServiceBusClient> GetOrCreateClientAsync()
		{
			if (ServiceBusClient == null)
			{
				await lockObject.WaitAsync();
				try
				{
					// now recheck as we've been locked
					if (ServiceBusClient == null)
					{
						string connectionString = ConnectionString;
						AzureBusRbacSettings rbacSettings = RbacConnectionSettings;

						if (!string.IsNullOrWhiteSpace(connectionString))
							ServiceBusClient = new ServiceBusClient(connectionString, new ServiceBusClientOptions { Identifier = Logger.LoggerSettings.ModuleName });
						else
						{
							var credentials = new ClientSecretCredential(rbacSettings.TenantId, rbacSettings.ApplicationId, rbacSettings.ClientKey);
							ServiceBusClient = new ServiceBusClient(rbacSettings.Endpoint, credentials);
						}
					}
				}
				finally
				{
					//When the task is ready, release the semaphore. It is vital to ALWAYS release the semaphore when we are ready, or else we will end up with a Semaphore that is forever locked.
					//This is why it is important to do the Release within a try...finally clause; program execution may crash or take a different path, this way you are guaranteed execution
					lockObject.Release();
				}
			}

			return await Task.FromResult(ServiceBusClient);
		}
#endif

		#region Overrides of AzureBus<TAuthenticationToken>

		/// <summary>
		/// Gets the connection string for the bus from <see cref="AzureBus{TAuthenticationToken}.ConfigurationManager"/>
		/// </summary>
		protected override
#if NETSTANDARD2_0 || NET48_OR_GREATER
		async Task<string> GetConnectionStringAsync
#else
		string GetConnectionString
#endif
			()
		{
			if (!ConfigurationManager.TryGetSetting(MessageBusConnectionStringConfigurationKey, out string connectionString))
				connectionString = null;
			if (string.IsNullOrWhiteSpace(connectionString))
			{
				string connectionEndpoint = ConfigurationManager.GetSetting(MessageBusConnectionEndpointConfigurationKey);
				// double check an endpoint isn't provided, if it is, then we're using endpoints, but if not, we'll assume a connection string is prefered as it's easier
				if (string.IsNullOrWhiteSpace(connectionEndpoint))
					throw new MissingApplicationSettingForConnectionStringException(MessageBusConnectionStringConfigurationKey);
			}
#if NETSTANDARD2_0 || NET48_OR_GREATER
			return await Task.FromResult(connectionString);
#else
			return connectionString;
#endif
		}

		/// <summary>
		/// Gets the RBAC connection settings for the bus from <see cref="AzureBus{TAuthenticationToken}.ConfigurationManager"/>
		/// </summary>
		protected override
#if NETSTANDARD2_0 || NET48_OR_GREATER
		async Task<AzureBusRbacSettings> GetRbacConnectionSettingsAsync
#else
		AzureBusRbacSettings GetRbacConnectionSettings
#endif
()
		{
			// double check an endpoint isn't provided, if it is, then we're using endpoints, but if not, we'll assume a connection string is prefered as it's easier
			bool isUsingConnectionString;
			if (!ConfigurationManager.TryGetSetting(MessageBusConnectionStringConfigurationKey, out string connectionString))
				isUsingConnectionString = false;
			else
				isUsingConnectionString = !string.IsNullOrWhiteSpace(connectionString);

			if (!ConfigurationManager.TryGetSetting(MessageBusConnectionEndpointConfigurationKey, out string endpoint))
				endpoint = null;
			if (!isUsingConnectionString && string.IsNullOrWhiteSpace(endpoint))
				throw new MissingApplicationSettingForConnectionStringException(MessageBusConnectionEndpointConfigurationKey);

			if (!ConfigurationManager.TryGetSetting(MessageBusConnectionApplicationIdConfigurationKey, out string applicationId))
				applicationId = null;
			if (!isUsingConnectionString && string.IsNullOrWhiteSpace(applicationId))
				throw new MissingApplicationSettingForConnectionStringException(MessageBusConnectionApplicationIdConfigurationKey);

			if (!ConfigurationManager.TryGetSetting(MessageBusConnectionClientKeyConfigurationKey, out string clientKey))
				clientKey = null;
			if (!isUsingConnectionString && string.IsNullOrWhiteSpace(clientKey))
				throw new MissingApplicationSettingForConnectionStringException(MessageBusConnectionClientKeyConfigurationKey);

			if (!ConfigurationManager.TryGetSetting(MessageBusConnectionTenantIdConfigurationKey, out string tenantId))
				tenantId = null;
			if (!isUsingConnectionString && string.IsNullOrWhiteSpace(tenantId))
				throw new MissingApplicationSettingForConnectionStringException(MessageBusConnectionTenantIdConfigurationKey);

			var result = new AzureBusRbacSettings
			{
				Endpoint = endpoint,
				ApplicationId = applicationId,
				ClientKey = clientKey,
				TenantId = tenantId
			};
#if NETSTANDARD2_0 || NET48_OR_GREATER
			return await Task.FromResult(result);
#else
			return result;
#endif
		}

		#endregion

#if NETSTANDARD2_0 || NET48_OR_GREATER
		/// <summary>
		/// Instantiate publishing on this bus by
		/// calling <see cref="CheckPrivateTopicExistsAsync(Manager, bool)"/> and <see cref="CheckPublicTopicExistsAsync(Manager, bool)"/>
		/// then calling <see cref="AzureBus{TAuthenticationToken}.StartSettingsChecking"/>
		/// </summary>
#else
		/// <summary>
		/// Instantiate publishing on this bus by
		/// calling <see cref="CheckPrivateTopicExists"/> and <see cref="CheckPublicTopicExists"/>
		/// then calling <see cref="AzureBus{TAuthenticationToken}.StartSettingsChecking"/>
		/// </summary>
#endif
		protected override
#if NETSTANDARD2_0 || NET48_OR_GREATER
			async Task InstantiatePublishingAsync
#else
			void InstantiatePublishing
#endif
			()
		{
#if NET462
			if (GetActiveDirectoryToken == null)
				InstantiateActiveDirectoryToken();
#endif

			Manager manager =
#if NETSTANDARD2_0 || NET48_OR_GREATER
				await GetManagerAsync
#else
				GetManager
#endif
				();

#if NETSTANDARD2_0 || NET48_OR_GREATER
			await CheckPrivateTopicExistsAsync(manager, false);
			await CheckPublicTopicExistsAsync(manager, false);
#else
			CheckPrivateTopicExists(manager, false);
			CheckPublicTopicExists(manager, false);
#endif

#if NETSTANDARD2_0 || NET48_OR_GREATER
			ServiceBusClient client = await GetOrCreateClientAsync();
			PrivateServiceBusPublisher = client.CreateSender(PrivateTopicName, new ServiceBusSenderOptions { Identifier = $"{Logger.LoggerSettings.ModuleName} Private Bus" });
			PublicServiceBusPublisher = client.CreateSender(PublicTopicName, new ServiceBusSenderOptions { Identifier = $"{Logger.LoggerSettings.ModuleName} Public Bus" });
#elif NET452
			PrivateServiceBusPublisher = TopicClient.CreateFromConnectionString(ConnectionString, PrivateTopicName);
			PublicServiceBusPublisher = TopicClient.CreateFromConnectionString(ConnectionString, PublicTopicName);
#else
			if (!string.IsNullOrWhiteSpace(ConnectionString))
			{
				PrivateServiceBusPublisher = TopicClient.CreateFromConnectionString(ConnectionString, PrivateTopicName);
				PublicServiceBusPublisher = TopicClient.CreateFromConnectionString(ConnectionString, PublicTopicName);
			}
			else
			{
				PrivateServiceBusPublisher = TopicClient.CreateWithAzureActiveDirectory(new Uri(RbacConnectionSettings.Endpoint), PrivateTopicName, GetActiveDirectoryToken, RbacConnectionSettings.GetDefaultAuthority());
				PublicServiceBusPublisher = TopicClient.CreateWithAzureActiveDirectory(new Uri(RbacConnectionSettings.Endpoint), PublicTopicName, GetActiveDirectoryToken, RbacConnectionSettings.GetDefaultAuthority());
			}
#endif
			StartSettingsChecking();
		}

#if NETSTANDARD2_0 || NET48_OR_GREATER
		/// <summary>
		/// Instantiate receiving on this bus by
		/// calling <see cref="CheckPrivateTopicExistsAsync(Manager, bool)"/> and <see cref="CheckPublicTopicExistsAsync(Manager, bool)"/>
		/// then InstantiateReceiving for private and public topics,
		/// calls <see cref="CleanUpDeadLettersAsync(string, string)"/> for the private and public topics,
		/// then calling <see cref="AzureBus{TAuthenticationToken}.StartSettingsChecking"/>
		/// </summary>
#else
		/// <summary>
		/// Instantiate receiving on this bus by
		/// calling <see cref="CheckPrivateTopicExists"/> and <see cref="CheckPublicTopicExists"/>
		/// then InstantiateReceiving for private and public topics,
		/// calls <see cref="CleanUpDeadLetters"/> for the private and public topics,
		/// then calling <see cref="AzureBus{TAuthenticationToken}.StartSettingsChecking"/>
		/// </summary>
#endif
		protected override
#if NETSTANDARD2_0 || NET48_OR_GREATER
			async Task InstantiateReceivingAsync
#else
			void InstantiateReceiving
#endif
			()
		{

			Manager manager =
#if NETSTANDARD2_0 || NET48_OR_GREATER
				await GetManagerAsync
#else
				GetManager
#endif
				();
			string connectionString = ConnectionString;
			AzureBusRbacSettings rbacSettings = RbacConnectionSettings;

#if NETSTANDARD2_0 || NET48_OR_GREATER
			await CheckPrivateTopicExistsAsync(manager);
			await CheckPublicTopicExistsAsync(manager);
#else
			CheckPrivateTopicExists(manager);
			CheckPublicTopicExists(manager);
#endif

			try
			{
#if NETSTANDARD2_0 || NET48_OR_GREATER
				await InstantiateReceivingAsync
#else
				InstantiateReceiving
#endif
				(manager, PrivateServiceBusReceivers, PrivateTopicName, PrivateTopicSubscriptionName);
			}
			catch (UriFormatException exception)
			{
				throw new InvalidConfigurationException("The connection string for one of the private Service Bus receivers may be invalid.", exception);
			}
			try
			{
#if NETSTANDARD2_0 || NET48_OR_GREATER
				await InstantiateReceivingAsync
#else
				InstantiateReceiving
#endif
				(manager, PublicServiceBusReceivers, PublicTopicName, PublicTopicSubscriptionName);
			}
			catch (UriFormatException exception)
			{
				throw new InvalidConfigurationException("The connection string for one of the public Service Bus receivers may be invalid.", exception);
			}

			bool enableDeadLetterCleanUp;
			string enableDeadLetterCleanUpValue = ConfigurationManager.GetSetting("Cqrs.Azure.Servicebus.EnableDeadLetterCleanUp");
			if (bool.TryParse(enableDeadLetterCleanUpValue, out enableDeadLetterCleanUp) && enableDeadLetterCleanUp)
			{
#if NETSTANDARD2_0 || NET48_OR_GREATER
				await CleanUpDeadLettersAsync(PrivateTopicName, PrivateTopicSubscriptionName);
				await CleanUpDeadLettersAsync(PublicTopicName, PublicTopicSubscriptionName);
#else
				CleanUpDeadLetters(PrivateTopicName, PrivateTopicSubscriptionName);
				CleanUpDeadLetters(PublicTopicName, PublicTopicSubscriptionName);
#endif
			}

			// If this is also a publisher, then it will the check over there and that will handle this
			// we only need to check one of these
			if (PublicServiceBusPublisher != null)
				return;

			StartSettingsChecking();
		}

#if NETSTANDARD2_0 || NET48_OR_GREATER
		/// <summary>
		/// Creates a single <see cref="IMessageReceiver"/>.
		/// If flushing is required, any flushed <see cref="IMessageReceiver"/> has <see cref="IMessageReceiver.CloseAsync(CancellationToken)"/> called on it first.
		/// </summary>
		/// <param name="manager">The <see cref="Manager"/>.</param>
		/// <param name="serviceBusReceivers">The receivers collection to place <see cref="IMessageReceiver"/> instances into.</param>
		/// <param name="topicName">The topic name.</param>
		/// <param name="topicSubscriptionName">The topic subscription name.</param>
#else
		/// <summary>
		/// Creates <see cref="AzureBus{TAuthenticationToken}.NumberOfReceiversCount"/> <see cref="IMessageReceiver"/>.
		/// If flushing is required, any flushed <see cref="IMessageReceiver"/> has <see cref="ClientEntity.Close()"/> called on it first.
		/// </summary>
		/// <param name="manager">The <see cref="Manager"/>.</param>
		/// <param name="serviceBusReceivers">The receivers collection to place <see cref="IMessageReceiver"/> instances into.</param>
		/// <param name="topicName">The topic name.</param>
		/// <param name="topicSubscriptionName">The topic subscription name.</param>
#endif
		protected virtual
#if NETSTANDARD2_0 || NET48_OR_GREATER
			async Task InstantiateReceivingAsync
#else
			void InstantiateReceiving
#endif
			(Manager manager, IDictionary<int, IMessageReceiver> serviceBusReceivers, string topicName, string topicSubscriptionName)
		{
#if NETSTANDARD2_0 || NET48_OR_GREATER
			IMessageReceiver serviceBusReceiver = (await GetOrCreateClientAsync()).CreateProcessor(topicName, topicSubscriptionName, new ServiceBusProcessorOptions { ReceiveMode = ServiceBusReceiveMode.PeekLock, Identifier = $"{Logger.LoggerSettings.ModuleName} Receiver", AutoCompleteMessages = false, MaxConcurrentCalls = MaximumConcurrentReceiverProcessesCount, MaxAutoLockRenewalDuration = new TimeSpan(0, 5, 0) });

			if (serviceBusReceivers.ContainsKey(0))
			{
				await serviceBusReceivers[0].CloseAsync();
				await serviceBusReceivers[0].DisposeAsync();
				serviceBusReceivers[0] = serviceBusReceiver;
			}
			else
				serviceBusReceivers.Add(0, serviceBusReceiver);

			await Task.CompletedTask;
#else
			for (int i = 0; i < NumberOfReceiversCount; i++)
			{
				IMessageReceiver serviceBusReceiver;
				string connectionString = ConnectionString;
				AzureBusRbacSettings rbacSettings = RbacConnectionSettings;
#if NET452
				serviceBusReceiver = SubscriptionClient.CreateFromConnectionString(ConnectionString, topicName, topicSubscriptionName);
#else
				if (!string.IsNullOrWhiteSpace(connectionString))
					serviceBusReceiver = SubscriptionClient.CreateFromConnectionString(ConnectionString, topicName, topicSubscriptionName);
				else
					serviceBusReceiver = SubscriptionClient.CreateWithAzureActiveDirectory(new Uri(rbacSettings.Endpoint), topicName, topicSubscriptionName, GetActiveDirectoryToken, rbacSettings.GetDefaultAuthority());
#endif
				if (serviceBusReceivers.ContainsKey(i))
					serviceBusReceivers[i] = serviceBusReceiver;
				else
					serviceBusReceivers.Add(i, serviceBusReceiver);
			}
			// Remove any if the number has decreased
			for (int i = NumberOfReceiversCount; i < serviceBusReceivers.Count; i++)
			{
				IMessageReceiver serviceBusReceiver;
				if (serviceBusReceivers.TryGetValue(i, out serviceBusReceiver))
				{
					serviceBusReceiver.Close();
				}
				serviceBusReceivers.Remove(i);
			}
#endif
		}

		/// <summary>
		/// Checks if the private topic and subscription name exists as per <see cref="PrivateTopicName"/> and <see cref="PrivateTopicSubscriptionName"/>.
		/// </summary>
		/// <param name="manager">The <see cref="Manager"/>.</param>
		/// <param name="createSubscriptionIfNotExists">Create a subscription if there isn't one</param>
		protected virtual
#if NETSTANDARD2_0 || NET48_OR_GREATER
			async Task CheckPrivateTopicExistsAsync
#else
			void CheckPrivateTopicExists
#endif
			(Manager manager, bool createSubscriptionIfNotExists = true)
		{
#if NETSTANDARD2_0 || NET48_OR_GREATER
			await CheckTopicExistsAsync
#else
			CheckTopicExists
#endif
			(manager, PrivateTopicName = ConfigurationManager.GetSetting(PrivateTopicNameConfigurationKey) ?? DefaultPrivateTopicName, PrivateTopicSubscriptionName = ConfigurationManager.GetSetting(PrivateTopicSubscriptionNameConfigurationKey) ?? DefaultPrivateTopicSubscriptionName, createSubscriptionIfNotExists);
			Logger.LogSensitive($"Private topic settings set.", metaData: new Dictionary<string, object> {
				{ "PrivateTopicName", PrivateTopicName },
				{ "PrivateTopicSubscriptionName", PrivateTopicSubscriptionName }
			});
		}

		/// <summary>
		/// Checks if the public topic and subscription name exists as per <see cref="PublicTopicName"/> and <see cref="PublicTopicSubscriptionName"/>.
		/// </summary>
		/// <param name="manager">The <see cref="Manager"/>.</param>
		/// <param name="createSubscriptionIfNotExists">Create a subscription if there isn't one</param>
		protected virtual
#if NETSTANDARD2_0 || NET48_OR_GREATER
			async Task CheckPublicTopicExistsAsync
#else
			void CheckPublicTopicExists
#endif
			(Manager manager, bool createSubscriptionIfNotExists = true)
		{
#if NETSTANDARD2_0 || NET48_OR_GREATER
			await CheckTopicExistsAsync
#else
			CheckTopicExists
#endif
			(manager, PublicTopicName = ConfigurationManager.GetSetting(PublicTopicNameConfigurationKey) ?? DefaultPublicTopicName, PublicTopicSubscriptionName = ConfigurationManager.GetSetting(PublicTopicSubscriptionNameConfigurationKey) ?? DefaultPublicTopicSubscriptionName, createSubscriptionIfNotExists);
			Logger.LogSensitive($"Public topic settings set.", metaData: new Dictionary<string, object> {
				{ "PublicTopicName", PublicTopicName },
				{ "PublicTopicSubscriptionName", PublicTopicSubscriptionName }
			});
		}

		/// <summary>
		/// Checks if a topic by the provided <paramref name="topicName"/> exists and
		/// Checks if a subscription name by the provided <paramref name="subscriptionName"/> exists.
		/// </summary>
		protected virtual
#if NETSTANDARD2_0 || NET48_OR_GREATER
			async Task CheckTopicExistsAsync
#else
			void CheckTopicExists
#endif
			(Manager manager, string topicName, string subscriptionName, bool createSubscriptionIfNotExists = true)
		{
			// Configure Queue Settings
			var eventTopicDescription = new TopicDescription(topicName)
			{
				MaxSizeInMegabytes = 5120,
				DefaultMessageTimeToLive = new TimeSpan(0, 25, 0),
				EnablePartitioning = true,
				EnableBatchedOperations = true,
				// forcing this requires an ability to set session ids which will need to be built into the framework
				// SupportOrdering = true
			};

#if NETSTANDARD2_0 || NET48_OR_GREATER
			bool topicExists = await manager.TopicExistsAsync(topicName);
			if (!topicExists)
			{
				TopicProperties createdTopic = await manager.CreateTopicAsync(eventTopicDescription);
				Logger.LogInfo($"Created topic '{createdTopic.Name}'");
			}

			if (createSubscriptionIfNotExists)
			{
				bool subscriptionExists = await manager.SubscriptionExistsAsync(topicName, subscriptionName);
				if (!subscriptionExists)
				{
					var subscriptionDescription = new CreateSubscriptionOptions(topicName, subscriptionName)
					{
						DefaultMessageTimeToLive = eventTopicDescription.DefaultMessageTimeToLive,
						EnableBatchedOperations = eventTopicDescription.EnableBatchedOperations,
						DeadLetteringOnMessageExpiration = true,
						LockDuration = new TimeSpan(0, 5, 0)
					};
					SubscriptionProperties createdSubscription = await manager.CreateSubscriptionAsync(subscriptionDescription);
					Logger.LogInfo($"Created subscription '{createdSubscription.SubscriptionName}' on topic '{createdSubscription.TopicName}'");
				}
			}
#else
			// Create the topic if it does not exist already
			if (!manager.TopicExists(eventTopicDescription.Path))
			{
				TopicDescription createdTopic = manager.CreateTopic(eventTopicDescription);
				Logger.LogInfo($"Created topic '{createdTopic.Path}'");
			}

			if (createSubscriptionIfNotExists && !manager.SubscriptionExists(eventTopicDescription.Path, subscriptionName))
			{
				SubscriptionDescription createdSubscription = manager.CreateSubscription
				(
					new SubscriptionDescription(eventTopicDescription.Path, subscriptionName)
					{
						DefaultMessageTimeToLive = new TimeSpan(0, 25, 0),
						EnableBatchedOperations = true,
						EnableDeadLetteringOnFilterEvaluationExceptions = true,
						LockDuration = new TimeSpan(0, 5, 0)
					}
				);

				Logger.LogInfo($"Created subscription '{createdSubscription.Name}' on topic '{createdSubscription.TopicPath}'");
			}
#endif
		}

#if NETSTANDARD2_0 || NET48_OR_GREATER
		/// <summary>
		/// First runs <see cref="AzureBus{TAuthenticationToken}.ValidateSettingsHaveChangedAsync"/> then checks
		/// <see cref="PublicTopicName"/>, <see cref="PublicTopicSubscriptionName"/>,
		/// <see cref="PrivateTopicName"/> or <see cref="PrivateTopicSubscriptionName"/> have changed.
		/// </summary>
#else
		/// <summary>
		/// First runs <see cref="AzureBus{TAuthenticationToken}.ValidateSettingsHaveChanged"/> then checks
		/// <see cref="PublicTopicName"/>, <see cref="PublicTopicSubscriptionName"/>,
		/// <see cref="PrivateTopicName"/> or <see cref="PrivateTopicSubscriptionName"/> have changed.
		/// </summary>
#endif
		protected override
#if NETSTANDARD2_0 || NET48_OR_GREATER
			async Task<bool> ValidateSettingsHaveChangedAsync
#else
			bool ValidateSettingsHaveChanged
#endif
			()
		{
			if (
#if NETSTANDARD2_0 || NET48_OR_GREATER
				await base.ValidateSettingsHaveChangedAsync
#else
				base.ValidateSettingsHaveChanged
#endif
				())
				return true;
			return PublicTopicName != (ConfigurationManager.GetSetting(PublicTopicNameConfigurationKey) ?? DefaultPublicTopicName)
				||
			PublicTopicSubscriptionName != (ConfigurationManager.GetSetting(PublicTopicSubscriptionNameConfigurationKey) ?? DefaultPublicTopicSubscriptionName)
				||
			PrivateTopicName != (ConfigurationManager.GetSetting(PrivateTopicNameConfigurationKey) ?? DefaultPrivateTopicName)
				||
			PrivateTopicSubscriptionName != (ConfigurationManager.GetSetting(PrivateTopicSubscriptionNameConfigurationKey) ?? DefaultPrivateTopicSubscriptionName);
		}

#if NETSTANDARD2_0 || NET48_OR_GREATER
		/// <summary>
		/// Triggers settings checking on both public and private publishers and receivers,
		/// then calls <see cref="InstantiatePublishingAsync"/> if <see cref="PublicServiceBusPublisher"/> is not null.
		/// </summary>
#else
		/// <summary>
		/// Triggers settings checking on both public and private publishers and receivers,
		/// then calls <see cref="InstantiatePublishing"/> if <see cref="PublicServiceBusPublisher"/> is not null.
		/// </summary>
#endif
		protected override
#if NETSTANDARD2_0 || NET48_OR_GREATER
			async Task TriggerSettingsCheckingAsync
#else
			void TriggerSettingsChecking
#endif
			()
		{
			// First refresh the EventBlackListProcessing property
			bool throwExceptionOnReceiverMessageLockLostExceptionDuringComplete;
			if (!ConfigurationManager.TryGetSetting(ThrowExceptionOnReceiverMessageLockLostExceptionDuringCompleteConfigurationKey, out throwExceptionOnReceiverMessageLockLostExceptionDuringComplete))
				throwExceptionOnReceiverMessageLockLostExceptionDuringComplete = true;
			ThrowExceptionOnReceiverMessageLockLostExceptionDuringComplete = throwExceptionOnReceiverMessageLockLostExceptionDuringComplete;

#if NETSTANDARD2_0 || NET48_OR_GREATER
			await TriggerSettingsCheckingAsync(PrivateServiceBusPublisher, PrivateServiceBusReceivers);
			await TriggerSettingsCheckingAsync(PublicServiceBusPublisher, PublicServiceBusReceivers);
#else
			TriggerSettingsChecking(PrivateServiceBusPublisher, PrivateServiceBusReceivers);
			TriggerSettingsChecking(PublicServiceBusPublisher, PublicServiceBusReceivers);
#endif

			// Restart configuration, we order this intentionally with the publisher second as if this triggers the cancellation there's nothing else to process here
			// we also only need to check one of the publishers
			if (PublicServiceBusPublisher != null)
			{
				Logger.LogDebug("Recursively calling into InstantiatePublishing.");
#if NETSTANDARD2_0 || NET48_OR_GREATER
				await InstantiatePublishingAsync
#else
				InstantiatePublishing
#endif
					();
			}
		}

#if NETSTANDARD2_0 || NET48_OR_GREATER
		/// <summary>
		/// Triggers settings checking on the provided <paramref name="serviceBusPublisher"/> and <paramref name="serviceBusReceivers"/>,
		/// then calls <see cref="InstantiateReceivingAsync()"/>.
		/// </summary>
#else
		/// <summary>
		/// Triggers settings checking on the provided <paramref name="serviceBusPublisher"/> and <paramref name="serviceBusReceivers"/>,
		/// then calls <see cref="InstantiateReceiving()"/>.
		/// </summary>
#endif
		protected virtual
#if NETSTANDARD2_0 || NET48_OR_GREATER
			async Task TriggerSettingsCheckingAsync
#else
			void TriggerSettingsChecking
#endif
			(TopicClient serviceBusPublisher, IDictionary<int, IMessageReceiver> serviceBusReceivers)
		{
			// Let's wrap up using this message bus and start the switch
			if (serviceBusPublisher != null)
			{
#if NETSTANDARD2_0 || NET48_OR_GREATER
				await serviceBusPublisher.CloseAsync();
#else
				serviceBusPublisher.Close();
#endif
				Logger.LogDebug("Publishing service bus closed.");
			}
			foreach (IMessageReceiver serviceBusReceiver in serviceBusReceivers.Values)
			{
				// Let's wrap up using this message bus and start the switch
				if (serviceBusReceiver != null)
				{
#if NETSTANDARD2_0 || NET48_OR_GREATER
					await serviceBusReceiver.CloseAsync();
					await serviceBusReceiver.DisposeAsync();
#else
					serviceBusReceiver.Close();
#endif
					Logger.LogDebug("Receiving service bus closed.");
				}
				// Restart configuration, we order this intentionally with the receiver first as if this triggers the cancellation we know this isn't a publisher as well
				if (serviceBusReceiver != null)
				{
					Logger.LogDebug("Recursively calling into InstantiateReceiving.");
#if NETSTANDARD2_0 || NET48_OR_GREATER
					await InstantiateReceivingAsync();
#else
					InstantiateReceiving();
#endif

					// This will be the case of a connection setting change re-connection
					if (ReceiverMessageHandler != null && ReceiverMessageHandlerOptions != null)
					{
						// Callback to handle received messages
						Logger.LogDebug("Re-registering onMessage handler.");
						ApplyReceiverMessageHandler();
					}
					else
						Logger.LogWarning("No onMessage handler was found to re-bind.");
				}
			}
		}

		/// <summary>
		/// Registers the provided <paramref name="receiverMessageHandler"/> with the provided <paramref name="receiverMessageHandlerOptions"/>.
		/// </summary>
#if NETSTANDARD2_0 || NET48_OR_GREATER
		protected async virtual Task RegisterReceiverMessageHandlerAsync(Func<ProcessMessageEventArgs, Task> receiverMessageHandler, ServiceBusProcessorOptions receiverMessageHandlerOptions)
#else
		protected virtual void RegisterReceiverMessageHandler(Action<IMessageReceiver, BrokeredMessage> receiverMessageHandler, OnMessageOptions receiverMessageHandlerOptions)
#endif
		{
#if NETSTANDARD2_0 || NET48_OR_GREATER
			await StoreReceiverMessageHandlerAsync
#else
			StoreReceiverMessageHandler
#endif
			(receiverMessageHandler, receiverMessageHandlerOptions);

			ApplyReceiverMessageHandler();

#if NETSTANDARD2_0 || NET48_OR_GREATER
			await Task.CompletedTask;
#endif
		}

		/// <summary>
		/// Stores the provided <paramref name="receiverMessageHandler"/> and <paramref name="receiverMessageHandlerOptions"/>.
		/// </summary>
#if NETSTANDARD2_0 || NET48_OR_GREATER
		protected virtual async Task StoreReceiverMessageHandlerAsync(Func<ProcessMessageEventArgs, Task> receiverMessageHandler, ServiceBusProcessorOptions receiverMessageHandlerOptions)
#else
		protected virtual void StoreReceiverMessageHandler(Action<IMessageReceiver, BrokeredMessage> receiverMessageHandler, OnMessageOptions receiverMessageHandlerOptions)
#endif
		{
			ReceiverMessageHandler = receiverMessageHandler;
			ReceiverMessageHandlerOptions = receiverMessageHandlerOptions;

#if NETSTANDARD2_0 || NET48_OR_GREATER
			await Task.CompletedTask;
#endif
		}

		/// <summary>
		/// Applies the stored ReceiverMessageHandler and ReceiverMessageHandlerOptions to all receivers in
		/// <see cref="PrivateServiceBusReceivers"/> and <see cref="PublicServiceBusReceivers"/>.
		/// </summary>
		protected override void ApplyReceiverMessageHandler()
		{
			foreach (IMessageReceiver serviceBusReceiver in PrivateServiceBusReceivers.Values)
			{
#if NETSTANDARD2_0 || NET48_OR_GREATER
				serviceBusReceiver.ProcessMessageAsync += async args =>
				{
					BusHelper.SetWasPrivateBusUsed(true);
					await ReceiverMessageHandler(args);
				};
#else
				serviceBusReceiver
					.OnMessage
					(
						message =>
						{
							BusHelper.SetWasPrivateBusUsed(true);
							ReceiverMessageHandler(serviceBusReceiver, message);
						},
						ReceiverMessageHandlerOptions
					);
#endif
				}
			foreach (IMessageReceiver serviceBusReceiver in PublicServiceBusReceivers.Values)
			{
#if NETSTANDARD2_0 || NET48_OR_GREATER
				serviceBusReceiver.ProcessMessageAsync += async args => {
					BusHelper.SetWasPrivateBusUsed(false);
					await ReceiverMessageHandler(args);
				};
#else
				serviceBusReceiver
					.OnMessage
						(
							message =>
							{
								BusHelper.SetWasPrivateBusUsed(false);
								ReceiverMessageHandler(serviceBusReceiver, message);
							},
							ReceiverMessageHandlerOptions
						);
#endif
			}
		}

		/// <summary>
		/// Using a <see cref="Task"/>, clears all dead letters from the topic and subscription of the 
		/// provided <paramref name="topicName"/> and <paramref name="topicSubscriptionName"/>.
		/// </summary>
		/// <param name="topicName">The name of the topic.</param>
		/// <param name="topicSubscriptionName">The name of the subscription.</param>
		/// <returns></returns>
		protected virtual
#if NETSTANDARD2_0 || NET48_OR_GREATER
			async Task<CancellationTokenSource> CleanUpDeadLettersAsync
#else
			CancellationTokenSource CleanUpDeadLetters
#endif
			(string topicName, string topicSubscriptionName)
		{
			var brokeredMessageRenewCancellationTokenSource = new CancellationTokenSource();
			IDictionary<string, string> telemetryProperties = new Dictionary<string, string> { { "Type", "Azure/Servicebus" } };
			int lockIssues = 0;

#if NETSTANDARD2_0 || NET48_OR_GREATER
			Func<ServiceBusReceiver, ServiceBusReceivedMessage, IMessage, Task> leaveDeadlLetterInQueue = async (receiver, deadLetterBrokeredMessage, deadLetterMessage) =>
#else
			Action<BrokeredMessage, IMessage> leaveDeadlLetterInQueue = (deadLetterBrokeredMessage, deadLetterMessage) =>
#endif
			{
				// Remove message from queue
				try
				{
#if NETSTANDARD2_0 || NET48_OR_GREATER
					await receiver.AbandonMessageAsync(deadLetterBrokeredMessage);
#else
					deadLetterBrokeredMessage.Abandon();
#endif
					lockIssues = 0;
				}
				catch
				(
#if NETSTANDARD2_0 || NET48_OR_GREATER
					ServiceBusException
#else
					MessageLockLostException
#endif
					ex
				)
				{
					lockIssues++;
					Logger.LogWarning($"The lock supplied for abandon for the skipped dead-letter message '{deadLetterBrokeredMessage.MessageId}' is invalid.", "Cqrs.Azure.ServiceBus.CleanUpDeadLetters.LeaveDeadlLetterInQueue", exception: ex);
				}
				Logger.LogDebug($"A dead-letter message of type {deadLetterMessage.GetType().FullName} arrived with the id '{deadLetterBrokeredMessage.MessageId}' but left in the queue due to settings.", "Cqrs.Azure.ServiceBus.CleanUpDeadLetters.LeaveDeadlLetterInQueue");

#if NETSTANDARD2_0 || NET48_OR_GREATER
				await Task.CompletedTask;
#endif
			};
#if NETSTANDARD2_0 || NET48_OR_GREATER
			Func<ServiceBusReceiver, ServiceBusReceivedMessage, Task> removeDeadlLetterFromQueue = async (receiver, deadLetterBrokeredMessage) =>
#else
			Action<BrokeredMessage> removeDeadlLetterFromQueue = (deadLetterBrokeredMessage) =>
#endif
			{
				// Remove message from queue
				try
				{
#if NETSTANDARD2_0 || NET48_OR_GREATER
					await receiver.CompleteMessageAsync(deadLetterBrokeredMessage);
#else
					deadLetterBrokeredMessage.Complete();
#endif
					lockIssues = 0;
				}
				catch
				(
#if NETSTANDARD2_0 || NET48_OR_GREATER
					ServiceBusException
#else
					MessageLockLostException
#endif
					ex
				)
				{
					lockIssues++;
					Logger.LogWarning($"The lock supplied for complete for the skipped dead-letter message '{deadLetterBrokeredMessage.MessageId}' is invalid.", "Cqrs.Azure.ServiceBus.CleanUpDeadLetters(Async).RemoveDeadlLetterFromQueue", exception: ex);
				}
				Logger.LogDebug($"A dead-letter message arrived with the id '{deadLetterBrokeredMessage.MessageId}' but was removed as processing was skipped due to settings.", "Cqrs.Azure.ServiceBus.CleanUpDeadLetters(Async).RemoveDeadlLetterFromQueue");

#if NETSTANDARD2_0 || NET48_OR_GREATER
				await Task.CompletedTask;
#endif
			};

#if NETSTANDARD2_0 || NET48_OR_GREATER
#else
			Task.Factory.StartNewSafely(() =>
#endif
			{
				int loop = 0;
				while (!brokeredMessageRenewCancellationTokenSource.Token.IsCancellationRequested)
				{
					lockIssues = 0;
					IEnumerable<BrokeredMessage> brokeredMessages;

#if NETSTANDARD2_0 || NET48_OR_GREATER
					ServiceBusReceiver deadLetterReceiver = (await GetOrCreateClientAsync()).CreateReceiver(topicName, topicSubscriptionName, new ServiceBusReceiverOptions
					{
						Identifier = $"{Logger.LoggerSettings.ModuleName} DeadLetter Cleaner",
						ReceiveMode = ServiceBusReceiveMode.PeekLock,
						SubQueue = SubQueue.DeadLetter
					});
					brokeredMessages = await deadLetterReceiver.ReceiveMessagesAsync(1000);
#else
					MessagingFactory factory;
#if NET452
					factory = MessagingFactory.CreateFromConnectionString(ConnectionString);
#else
					if (!string.IsNullOrWhiteSpace(ConnectionString))
						factory = MessagingFactory.CreateFromConnectionString(ConnectionString);
					else
						factory = MessagingFactory.Create(new Uri(RbacConnectionSettings.Endpoint), TokenProvider.CreateAzureActiveDirectoryTokenProvider(GetActiveDirectoryToken, null, RbacConnectionSettings.GetDefaultAuthority()));
#endif

					string deadLetterPath = SubscriptionClient.FormatDeadLetterPath(topicName, topicSubscriptionName);
					MessageReceiver client = factory.CreateMessageReceiver(deadLetterPath, ReceiveMode.PeekLock);
					brokeredMessages = client.ReceiveBatch(1000);
#endif

					foreach (BrokeredMessage brokeredMessage in brokeredMessages)
					{
						if (lockIssues > 10)
							break;
						try
						{
							Logger.LogDebug($"A dead-letter message arrived with the id '{brokeredMessage.MessageId}'.");
#if NET462
							string messageBody = brokeredMessage.GetBody<string>();
#else
							string messageBody = brokeredMessage.GetBodyAsString();
#endif

							// Closure protection
							BrokeredMessage message = brokeredMessage;
							try
							{
#if NETSTANDARD2_0 || NET48_OR_GREATER
								await AzureBusHelper.ReceiveEventAsync(deadLetterReceiver, 
#else
								AzureBusHelper.ReceiveEvent(null, 
#endif
									messageBody,
#if NETSTANDARD2_0 || NET48_OR_GREATER
									async @event =>
#else
									@event =>
#endif
									{
										bool isRequired = BusHelper.IsEventRequired(@event.GetType());
										if (!isRequired)
										{
#if NETSTANDARD2_0 || NET48_OR_GREATER
											await removeDeadlLetterFromQueue(deadLetterReceiver, message);
#else
											removeDeadlLetterFromQueue(message);
#endif
										}
										else
										{
#if NETSTANDARD2_0 || NET48_OR_GREATER
											await leaveDeadlLetterInQueue(deadLetterReceiver, message, @event);
#else
											leaveDeadlLetterInQueue(message, @event);
#endif
										}
#if NETSTANDARD2_0 || NET48_OR_GREATER
										return await Task.FromResult<bool?>(true);
#else
										return true;
#endif
									},
									$"id '{brokeredMessage.MessageId}'",
									ExtractSignature(message),
									SigningTokenConfigurationKey,
#if NETSTANDARD2_0 || NET48_OR_GREATER
									async
#endif
									() =>
									{
#if NETSTANDARD2_0 || NET48_OR_GREATER
										await
#endif
										removeDeadlLetterFromQueue(
#if NETSTANDARD2_0 || NET48_OR_GREATER
										deadLetterReceiver,
#endif
										message);
									},
#if NETSTANDARD2_0 || NET48_OR_GREATER
									async
#endif
									() =>
									{
#if NETSTANDARD2_0 || NET48_OR_GREATER
										await Task.CompletedTask;
#endif
									}
								);
							}
							catch
							{
#if NETSTANDARD2_0 || NET48_OR_GREATER
								await AzureBusHelper.ReceiveEventAsync(deadLetterReceiver,
#else
								AzureBusHelper.ReceiveEvent(null, 
#endif
									messageBody,
#if NETSTANDARD2_0 || NET48_OR_GREATER
									async command =>
#else
									command =>
#endif
									{
										bool isRequired = BusHelper.IsEventRequired(command.GetType());
										if (!isRequired)
										{
#if NETSTANDARD2_0 || NET48_OR_GREATER
											await removeDeadlLetterFromQueue(deadLetterReceiver, message);
#else
											removeDeadlLetterFromQueue(message);
#endif
										}
										else
										{
#if NETSTANDARD2_0 || NET48_OR_GREATER
											await leaveDeadlLetterInQueue(deadLetterReceiver, message, command);
#else
											leaveDeadlLetterInQueue(message, command);
#endif
										}
#if NETSTANDARD2_0 || NET48_OR_GREATER
										return await Task.FromResult<bool?>(true);
#else
										return true;
#endif
									},
									$"id '{brokeredMessage.MessageId}'",
									ExtractSignature(message),
									SigningTokenConfigurationKey,
#if NETSTANDARD2_0 || NET48_OR_GREATER
									async
#endif
									() =>
									{
#if NETSTANDARD2_0 || NET48_OR_GREATER
										await
#endif
										removeDeadlLetterFromQueue(
#if NETSTANDARD2_0 || NET48_OR_GREATER
										deadLetterReceiver,
#endif
										message);
									},
#if NETSTANDARD2_0 || NET48_OR_GREATER
									async
#endif
									() =>
									{
#if NETSTANDARD2_0 || NET48_OR_GREATER
										await Task.CompletedTask;
#endif
									}
								);
							}
						}
						catch (Exception exception)
						{
							TelemetryHelper.TrackException(exception, null, telemetryProperties);
							// Indicates a problem, unlock message in queue
							Logger.LogError($"A dead-letter message arrived with the id '{brokeredMessage.MessageId}' but failed to be process.", exception: exception);
							try
							{
#if NETSTANDARD2_0 || NET48_OR_GREATER
								await deadLetterReceiver.AbandonMessageAsync(brokeredMessage);
#else
								brokeredMessage.Abandon();
#endif
							}
							catch
							(
#if NETSTANDARD2_0 || NET48_OR_GREATER
								ServiceBusException
#else
								MessageLockLostException
#endif
								ex
							)
							{
								lockIssues++;
								Logger.LogWarning($"The lock supplied for abandon for the skipped dead-letter message '{brokeredMessage.MessageId}' is invalid.", exception: ex);
							}
						}
					}
#if NETSTANDARD2_0 || NET48_OR_GREATER
#else
					client.Close();
#endif

					if (loop++ % 5 == 0)
					{
						loop = 0;
						Thread.Yield();
					}
					else
						Thread.Sleep(500);
				}
				try
				{
					brokeredMessageRenewCancellationTokenSource.Dispose();
				}
				catch (ObjectDisposedException) { }
			}
#if NETSTANDARD2_0 || NET48_OR_GREATER
#else
			, brokeredMessageRenewCancellationTokenSource.Token);
#endif

#if NETSTANDARD2_0 || NET48_OR_GREATER
			return await Task.FromResult(brokeredMessageRenewCancellationTokenSource);
#else
			return brokeredMessageRenewCancellationTokenSource;
#endif
		}

#if NETSTANDARD2_0 || NET48_OR_GREATER
		DataContractBinarySerializer brokeredMessageSerialiser = new DataContractBinarySerializer(typeof(string));
#endif
		/// <summary>
		/// Create a <see cref="BrokeredMessage"/> with additional properties to aid routing and tracing
		/// </summary>
		protected virtual
#if NETSTANDARD2_0 || NET48_OR_GREATER
			async Task<ServiceBusMessage> CreateBrokeredMessageAsync
#else
			BrokeredMessage CreateBrokeredMessage
#endif
			<TMessage>(Func<TMessage, string> serialiserFunction, Type messageType, TMessage message
#if NETSTANDARD2_0 || NET48_OR_GREATER
				, TimeSpan? delay = null
#endif
			)
		{
			string messageBody = serialiserFunction(message);
#if NETSTANDARD2_0 || NET48_OR_GREATER
			byte[] messageBodyData;
			using (var stream = new MemoryStream())
			{
				brokeredMessageSerialiser.WriteObject(stream, messageBody);
				stream.Flush();
				stream.Position = 0;
				messageBodyData = stream.ToArray();
			}

			var brokeredMessage = new ServiceBusMessage(messageBodyData)
#else
			var brokeredMessage = new BrokeredMessage(messageBody)
#endif
			{
				CorrelationId = CorrelationIdHelper.GetCorrelationId().ToString("N")
			};
			if (message is IMessageWithOrderingKey messageWithOrderingKey)
			{
				brokeredMessage.SessionId = messageWithOrderingKey.OrderKey;
			}
			brokeredMessage.AddUserProperty("CorrelationId", brokeredMessage.CorrelationId);
			Type sagaType = typeof(Cqrs.Events.SagaEvent<int>);
			string typeName = null;
			if (messageType.Assembly == sagaType.Assembly && messageType.Name == sagaType.Name && messageType.Namespace == sagaType.Namespace)
			{
				PropertyInfo eventProperty = messageType.GetProperty("Event");
				if (eventProperty != null)
				{
					object @event = eventProperty.GetValue(message);
					if (@event != null)
					{
						typeName = $"{messageType.Namespace}.{messageType.Name}{{{@event.GetType().FullName}}}";
					}
				}
			}
			if (string.IsNullOrWhiteSpace(typeName))
				typeName = messageType.FullName;
			brokeredMessage.AddUserProperty("Type", typeName);
			brokeredMessage.AddUserProperty("Source", $"{Logger.LoggerSettings.ModuleName}/{Logger.LoggerSettings.Instance}/{Logger.LoggerSettings.Environment}/{Logger.LoggerSettings.EnvironmentInstance}");
			brokeredMessage.AddUserProperty("Framework",
				// this compiler directive is intentionally .NET Core and not 4.8
#if NETSTANDARD2_0 || NET48_OR_GREATER
				".NET Core"
#else
				".NET Framework"
#endif
			);

			// see https://github.com/Chinchilla-Software-Com/CQRS/wiki/Inter-process-function-security
			string configurationKey = $"{messageType.FullName}.SigningToken";
			string signingToken;
			HashAlgorithm signer = Signer.Create();
			if (!ConfigurationManager.TryGetSetting(configurationKey, out signingToken) || string.IsNullOrWhiteSpace(signingToken))
				if (!ConfigurationManager.TryGetSetting(SigningTokenConfigurationKey, out signingToken) || string.IsNullOrWhiteSpace(signingToken))
					signingToken = Guid.Empty.ToString("N");
			if (!string.IsNullOrWhiteSpace(signingToken))
				using (var hashStream = new MemoryStream(Encoding.UTF8.GetBytes($"{signingToken}{messageBody}")))
					brokeredMessage.AddUserProperty("Signature", Convert.ToBase64String(signer.ComputeHash(hashStream)));

			try
			{
				var stackTrace = new StackTrace();
				StackFrame[] stackFrames = stackTrace.GetFrames();
				if (stackFrames != null)
				{
					foreach (StackFrame frame in stackFrames)
					{
						MethodBase method = frame.GetMethod();
						if (method.ReflectedType == null || string.IsNullOrWhiteSpace(method.ReflectedType.FullName))
							continue;

						try
						{
							if (ExclusionNamespaces.All(@namespace => !method.ReflectedType.FullName.StartsWith(@namespace)))
							{
								string container;
								Match match;
								if (method.DeclaringType.IsSealed && method.DeclaringType.IsNestedPrivate && method.DeclaringType.IsAutoLayout && TaskRelatedMethodNames.Contains(method.Name) && (match = ContainerNameMatcher.Match(method.ReflectedType.FullName)).Success)
								{
									container = match.Value.Substring(0, match.Value.Length - 1).Replace("+<", "\\");
								}
								else
								{
									container = $"{method.ReflectedType.FullName}\\{method.Name}";
								}

								brokeredMessage.AddUserProperty("Source-Method", container);
								break;
							}
						}
						catch
						{
							// Just move on
						}
					}
				}
			}
			catch
			{
				// Just move on
			}

#if NETSTANDARD2_0 || NET48_OR_GREATER
			if (brokeredMessage != null && delay != null)
			{
				brokeredMessage.ScheduledEnqueueTime = DateTimeOffset.Now.Add(delay.Value);
			}
#endif

#if NETSTANDARD2_0 || NET48_OR_GREATER
			return await Task.FromResult(brokeredMessage);
#else
			return brokeredMessage;
#endif
		}

		/// <summary>
		/// Extract any telemetry properties from the provided <paramref name="message"/>.
		/// </summary>
		protected virtual IDictionary<string, string> ExtractTelemetryProperties(BrokeredMessage message, string baseCommunicationType)
		{
			IDictionary<string, string> telemetryProperties = new Dictionary<string, string> { { "Type", baseCommunicationType } };
			object value;
			if (message.TryGetUserPropertyValue("Type", out value))
				telemetryProperties.Add("MessageType", value.ToString());
			if (message.TryGetUserPropertyValue("Source", out value))
				telemetryProperties.Add("MessageSource", value.ToString());
			if (message.TryGetUserPropertyValue("Source-Method", out value))
				telemetryProperties.Add("MessageSourceMethod", value.ToString());
			if (message.TryGetUserPropertyValue("CorrelationId", out value) && !telemetryProperties.ContainsKey("CorrelationId"))
				telemetryProperties.Add("CorrelationId", value.ToString());

			return telemetryProperties;
		}

		/// <summary>
		/// Extract the signature from the provided <paramref name="message"/>.
		/// </summary>
		protected virtual string ExtractSignature(BrokeredMessage message)
		{
			object value;
			if (message.TryGetUserPropertyValue("Signature", out value))
				return value.ToString();
			return null;
		}
	}
}