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
using System.Linq;
using Chinchilla.Logging;
using Cqrs.Authentication;
using Cqrs.Bus;
using Cqrs.Configuration;
using Cqrs.Events;
using Cqrs.Messages;

#if NETSTANDARD2_0 || NET48_OR_GREATER
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using BrokeredMessage = Azure.Messaging.ServiceBus.ServiceBusMessage;
#else
using Microsoft.ServiceBus.Messaging;
#endif

namespace Cqrs.Azure.ServiceBus
{
	/// <summary>
	/// An <see cref="IEventPublisher{TAuthenticationToken}"/> that resolves handlers and executes the handler.
	/// </summary>
	/// <typeparam name="TAuthenticationToken">The <see cref="Type"/> of the authentication token.</typeparam>
	// The “,nq” suffix here just asks the expression evaluator to remove the quotes when displaying the final value (nq = no quotes).
	[DebuggerDisplay("{DebuggerDisplay,nq}")]
	public class AzureEventBusPublisher<TAuthenticationToken>
		: AzureEventBus<TAuthenticationToken>
#if NETSTANDARD2_0 || NET48_OR_GREATER
		, IAsyncEventPublisher<TAuthenticationToken>
#else
		, IEventPublisher<TAuthenticationToken>
#endif
	{
		/// <summary>
		/// Instantiates a new instance of <see cref="AzureEventBusPublisher{TAuthenticationToken}"/>.
		/// </summary>
		public AzureEventBusPublisher(IConfigurationManager configurationManager, IMessageSerialiser<TAuthenticationToken> messageSerialiser, IAuthenticationTokenHelper<TAuthenticationToken> authenticationTokenHelper, ICorrelationIdHelper correlationIdHelper, ILogger logger, IAzureBusHelper<TAuthenticationToken> azureBusHelper, IBusHelper busHelper, IHashAlgorithmFactory hashAlgorithmFactory)
			: base(configurationManager, messageSerialiser, authenticationTokenHelper, correlationIdHelper, logger, azureBusHelper, busHelper, hashAlgorithmFactory, true)
		{
			TelemetryHelper = configurationManager.CreateTelemetryHelper("Cqrs.Azure.EventBus.Publisher.UseApplicationInsightTelemetryHelper", correlationIdHelper);
		}

		/// <summary>
		/// The debugger variable window value.
		/// </summary>
		internal string DebuggerDisplay
		{
			get
			{
				string connectionString = $"ConnectionString : {MessageBusConnectionStringConfigurationKey}";
				try
				{
					string _value =
#if NETSTANDARD2_0 || NET48_OR_GREATER
						GetConnectionStringAsync().Result;
#else
						GetConnectionString();
#endif
					if (!string.IsNullOrWhiteSpace(_value))
						connectionString = string.Concat(connectionString, "=", _value);
					else
					{
						connectionString = $"ConnectionRBACSettings : ";
						connectionString = string.Concat(connectionString, "=",
#if NETSTANDARD2_0 || NET48_OR_GREATER
							GetRbacConnectionSettingsAsync().Result
#else
							GetRbacConnectionSettings()
#endif
						);
					}
				}
				catch { /* */ }
				return $"{connectionString}, PrivateTopicName : {PrivateTopicName}, PrivateTopicSubscriptionName : {PrivateTopicSubscriptionName}, PublicTopicName : {PublicTopicName}, PublicTopicSubscriptionName : {PublicTopicSubscriptionName}";
			}
		}

		#region Implementation of IEventPublisher<TAuthenticationToken>

		/// <summary>
		/// Publishes the provided <paramref name="event"/> on the event bus.
		/// </summary>
		public virtual
#if NETSTANDARD2_0 || NET48_OR_GREATER
			async Task PublishAsync
#else
			void Publish
#endif
			<TEvent>(TEvent @event)
			where TEvent : IEvent<TAuthenticationToken>
		{
			if (@event == null)
			{
				Logger.LogDebug("No event to publish.");
				return;
			}
			Type eventType = @event.GetType();
			DateTimeOffset startedAt = DateTimeOffset.UtcNow;
			Stopwatch mainStopWatch = Stopwatch.StartNew();
			string responseCode = null;
			bool mainWasSuccessfull = false;
			bool telemeterOverall = false;

			IDictionary<string, string> telemetryProperties = new Dictionary<string, string> { { "Type", "Azure/Servicebus" } };
			string telemetryName = $"{eventType.FullName}/{@event.GetIdentity()}/{@event.Id}";
			var telemeteredEvent = @event as ITelemeteredMessage;
			if (telemeteredEvent != null)
				telemetryName = telemeteredEvent.TelemetryName;
			else
				telemetryName = $"Event/{telemetryName}";

			try
			{
				if (!
#if NETSTANDARD2_0 || NET48_OR_GREATER
					await AzureBusHelper.PrepareAndValidateEventAsync
#else
					AzureBusHelper.PrepareAndValidateEvent
#endif
					(@event, "Azure-ServiceBus"))
					return;

				bool? isPublicBusRequired = BusHelper.IsPublicBusRequired(eventType);
				bool? isPrivateBusRequired = BusHelper.IsPrivateBusRequired(eventType);

				// We only add telemetry for overall operations if two occurred
				telemeterOverall = isPublicBusRequired != null && isPublicBusRequired.Value && isPrivateBusRequired != null && isPrivateBusRequired.Value;

				// Backwards compatibility and simplicity
				bool wasSuccessfull;
				Stopwatch stopWatch = Stopwatch.StartNew();
				if ((isPublicBusRequired == null || !isPublicBusRequired.Value) && (isPrivateBusRequired == null || !isPrivateBusRequired.Value))
				{
					stopWatch.Restart();
					responseCode = "200";
					wasSuccessfull = false;
					try
					{
						var brokeredMessage =
#if NETSTANDARD2_0 || NET48_OR_GREATER
							await CreateBrokeredMessageAsync
#else
							CreateBrokeredMessage
#endif
							(MessageSerialiser.SerialiseEvent, eventType, @event);
						int count = 1;
						do
						{
							try
							{
#if NETSTANDARD2_0 || NET48_OR_GREATER
								await PublicServiceBusPublisher.SendMessageAsync(brokeredMessage).ConfigureAwait(true);
#else
								PublicServiceBusPublisher.Send(brokeredMessage);
#endif
								break;
							}
							catch (TimeoutException)
							{
								if (count >= TimeoutOnSendRetryMaximumCount)
									throw;
							}
							count++;
						} while (true);
						wasSuccessfull = true;
					}
					catch
					(
#if NETSTANDARD2_0 || NET48_OR_GREATER
						ServiceBusException
#else
						QuotaExceededException
#endif
						exception
					)
					{
						responseCode = "429";
						Logger.LogError("The size of the event being sent was too large or the topic has reached it's limit.", exception: exception, metaData: new Dictionary<string, object> { { "Event", @event } });
						throw;
					}
					catch (Exception exception)
					{
						responseCode = "500";
						Logger.LogError("An issue occurred while trying to publish an event.", exception: exception, metaData: new Dictionary<string, object> { { "Event", @event } });
						throw;
					}
					finally
					{
						TelemetryHelper.TrackDependency("Azure/Servicebus/EventBus", "Event", telemetryName, "Default Bus", startedAt, stopWatch.Elapsed, responseCode, wasSuccessfull, telemetryProperties);
					}
					Logger.LogDebug($"An event was published on the public bus with the id '{@event.Id}' was of type {eventType.FullName}.");
				}
				if ((isPublicBusRequired != null && isPublicBusRequired.Value))
				{
					stopWatch.Restart();
					responseCode = "200";
					wasSuccessfull = false;
					try
					{
						var brokeredMessage =
#if NETSTANDARD2_0 || NET48_OR_GREATER
							await CreateBrokeredMessageAsync
#else
							CreateBrokeredMessage
#endif
							(MessageSerialiser.SerialiseEvent, eventType, @event);
						int count = 1;
						do
						{
							try
							{
#if NETSTANDARD2_0 || NET48_OR_GREATER
								await PublicServiceBusPublisher.SendMessageAsync(brokeredMessage);
#else
								PublicServiceBusPublisher.Send(brokeredMessage);
#endif
								break;
							}
							catch (TimeoutException)
							{
								if (count >= TimeoutOnSendRetryMaximumCount)
									throw;
							}
							count++;
						} while (true);
						wasSuccessfull = true;
					}
					catch
					(
#if NETSTANDARD2_0 || NET48_OR_GREATER
						ServiceBusException
#else
						QuotaExceededException
#endif
						exception
					)
					{
						responseCode = "429";
						Logger.LogError("The size of the event being sent was too large.", exception: exception, metaData: new Dictionary<string, object> { { "Event", @event } });
						throw;
					}
					catch (Exception exception)
					{
						responseCode = "500";
						Logger.LogError("An issue occurred while trying to publish an event.", exception: exception, metaData: new Dictionary<string, object> { { "Event", @event } });
						throw;
					}
					finally
					{
						TelemetryHelper.TrackDependency("Azure/Servicebus/EventBus", "Event", telemetryName, "Public Bus", startedAt, stopWatch.Elapsed, responseCode, wasSuccessfull, telemetryProperties);
					}
					Logger.LogDebug($"An event was published on the public bus with the id '{@event.Id}' was of type {eventType.FullName}.");
				}
				if (isPrivateBusRequired != null && isPrivateBusRequired.Value)
				{
					stopWatch.Restart();
					responseCode = "200";
					wasSuccessfull = false;
					try
					{
						BrokeredMessage brokeredMessage =
#if NETSTANDARD2_0 || NET48_OR_GREATER
							await CreateBrokeredMessageAsync
#else
							CreateBrokeredMessage
#endif
							(MessageSerialiser.SerialiseEvent, eventType, @event);
						int count = 1;
						do
						{
							try
							{
#if NETSTANDARD2_0 || NET48_OR_GREATER
								await PrivateServiceBusPublisher.SendMessageAsync(brokeredMessage);
#else
								PrivateServiceBusPublisher.Send(brokeredMessage);
#endif
								break;
							}
							catch (TimeoutException)
							{
								if (count >= TimeoutOnSendRetryMaximumCount)
									throw;
							}
							count++;
						} while (true);
						wasSuccessfull = true;
					}
					catch
					(
#if NETSTANDARD2_0 || NET48_OR_GREATER
						ServiceBusException
#else
						QuotaExceededException
#endif
						exception
					)
					{
						responseCode = "429";
						Logger.LogError("The size of the event being sent was too large.", exception: exception, metaData: new Dictionary<string, object> { { "Event", @event } });
						throw;
					}
					catch (Exception exception)
					{
						responseCode = "500";
						Logger.LogError("An issue occurred while trying to publish an event.", exception: exception, metaData: new Dictionary<string, object> { { "Event", @event } });
						throw;
					}
					finally
					{
						TelemetryHelper.TrackDependency("Azure/Servicebus/EventBus", "Event", telemetryName, "Private Bus", startedAt, stopWatch.Elapsed, responseCode, wasSuccessfull, telemetryProperties);
					}

					Logger.LogDebug($"An event was published on the private bus with the id '{@event.Id}' was of type {eventType.FullName}.");
				}
				mainWasSuccessfull = true;
			}
			finally
			{
				mainStopWatch.Stop();
				if (telemeterOverall)
					TelemetryHelper.TrackDependency("Azure/Servicebus/EventBus", "Event", telemetryName, null, startedAt, mainStopWatch.Elapsed, responseCode, mainWasSuccessfull, telemetryProperties);
			}
		}

		/// <summary>
		/// Publishes the provided <paramref name="events"/> on the event bus.
		/// </summary>
		public virtual
#if NETSTANDARD2_0 || NET48_OR_GREATER
			async Task PublishAsync
#else
			void Publish
#endif
			<TEvent>(IEnumerable<TEvent> events)
			where TEvent : IEvent<TAuthenticationToken>
		{
			if (events == null)
			{
				Logger.LogDebug("No events to publish.");
				return;
			}
			IList<TEvent> sourceEvents = events.ToList();
			if (!sourceEvents.Any())
			{
				Logger.LogDebug("An empty collection of events to publish.");
				return;
			}

			DateTimeOffset startedAt = DateTimeOffset.UtcNow;
			Stopwatch mainStopWatch = Stopwatch.StartNew();
			string responseCode = null;
			bool mainWasSuccessfull = false;

			IDictionary<string, string> telemetryProperties = new Dictionary<string, string> { { "Type", "Azure/Servicebus" } };
			string telemetryName = "Events";
			string telemetryNames = string.Empty;
			foreach (TEvent @event in sourceEvents)
			{
				Type eventType = @event.GetType();
				string subTelemetryName = $"{eventType.FullName}/{@event.GetIdentity()}/{@event.Id}";
				var telemeteredEvent = @event as ITelemeteredMessage;
				if (telemeteredEvent != null)
					subTelemetryName = telemeteredEvent.TelemetryName;
				telemetryNames = $"{telemetryNames}{subTelemetryName},";
			}
			if (telemetryNames.Length > 0)
				telemetryNames = telemetryNames.Substring(0, telemetryNames.Length - 1);
			telemetryProperties.Add("Events", telemetryNames);

			try
			{
				IList<string> sourceEventMessages = new List<string>();
				IList<BrokeredMessage> privateBrokeredMessages = new List<BrokeredMessage>(sourceEvents.Count);
				IList<BrokeredMessage> publicBrokeredMessages = new List<BrokeredMessage>(sourceEvents.Count);
				foreach (TEvent @event in sourceEvents)
				{
					if (!
#if NETSTANDARD2_0 || NET48_OR_GREATER
						await AzureBusHelper.PrepareAndValidateEventAsync
#else
						AzureBusHelper.PrepareAndValidateEvent
#endif
						(@event, "Azure-ServiceBus"))
						continue;

					Type eventType = @event.GetType();

					BrokeredMessage brokeredMessage =
#if NETSTANDARD2_0 || NET48_OR_GREATER
						await CreateBrokeredMessageAsync
#else
						CreateBrokeredMessage
#endif
						(MessageSerialiser.SerialiseEvent, eventType, @event);

					bool? isPublicBusRequired = BusHelper.IsPublicBusRequired(eventType);
					bool? isPrivateBusRequired = BusHelper.IsPrivateBusRequired(eventType);

					// Backwards compatibility and simplicity
					if ((isPublicBusRequired == null || !isPublicBusRequired.Value) && (isPrivateBusRequired == null || !isPrivateBusRequired.Value))
					{
						publicBrokeredMessages.Add(brokeredMessage);
						sourceEventMessages.Add($"A event was published on the public bus with the id '{@event.Id}' was of type {eventType.FullName}.");
					}
					if ((isPublicBusRequired != null && isPublicBusRequired.Value))
					{
						publicBrokeredMessages.Add(brokeredMessage);
						sourceEventMessages.Add($"A event was published on the public bus with the id '{@event.Id}' was of type {eventType.FullName}.");
					}
					if (isPrivateBusRequired != null && isPrivateBusRequired.Value)
					{
						privateBrokeredMessages.Add(brokeredMessage);
						sourceEventMessages.Add($"An event was published on the private bus with the id '{@event.Id}' was of type {eventType.FullName}.");
					}
				}

				bool wasSuccessfull;
				Stopwatch stopWatch = Stopwatch.StartNew();

				// Backwards compatibility and simplicity
				stopWatch.Restart();
				responseCode = "200";
				wasSuccessfull = false;
				try
				{
					int count = 1;
					bool batchSuccess = true;
					do
					{
						try
						{
							if (publicBrokeredMessages.Any())
							{
#if NETSTANDARD2_0 || NET48_OR_GREATER
								await PublicServiceBusPublisher.SendMessagesAsync(publicBrokeredMessages);
#else
								PublicServiceBusPublisher.SendBatch(publicBrokeredMessages);
#endif
							}
							else
								Logger.LogDebug("An empty collection of public events to publish post validation.");
							break;
						}
#if NETSTANDARD2_0 || NET48_OR_GREATER
						catch (ServiceBusException exception)
						{
							if (exception.Reason == ServiceBusFailureReason.MessageSizeExceeded)
							{
								Logger.LogDebug("The size of the event being sent was too large. Will try sending individually", exception: exception, metaData: new Dictionary<string, object> { { "Command", publicBrokeredMessages } });
								batchSuccess = false;
							}
							else
								throw;
						}
#else
						catch (QuotaExceededException exception)
						{
							Logger.LogDebug("The size of the event being sent was too large. Will try sending individually", exception: exception, metaData: new Dictionary<string, object> { { "Command", publicBrokeredMessages } });
							batchSuccess = false;
						}
#endif
						catch (TimeoutException)
						{
							if (count >= TimeoutOnSendRetryMaximumCount)
								throw;
						}
						if (!batchSuccess)
						{
							foreach (var publicBrokeredMessage in publicBrokeredMessages)
							{
#if NETSTANDARD2_0 || NET48_OR_GREATER
								await PublicServiceBusPublisher.SendMessageAsync(publicBrokeredMessage);
#else
								PublicServiceBusPublisher.Send(publicBrokeredMessage);
#endif
							}
							break;
						}
						count++;
					} while (true);
					wasSuccessfull = true;
				}
				catch
				(
#if NETSTANDARD2_0 || NET48_OR_GREATER
					ServiceBusException
#else
					QuotaExceededException
#endif
					exception
				)
				{
					responseCode = "429";
					Logger.LogError("The size of the event being sent was too large.", exception: exception, metaData: new Dictionary<string, object> { { "Events", publicBrokeredMessages } });
					throw;
				}
				catch (Exception exception)
				{
					responseCode = "500";
					Logger.LogError("An issue occurred while trying to publish an event.", exception: exception, metaData: new Dictionary<string, object> { { "Events", publicBrokeredMessages } });
					throw;
				}
				finally
				{
					TelemetryHelper.TrackDependency("Azure/Servicebus/EventBus", "Event", telemetryName, "Public Bus", startedAt, stopWatch.Elapsed, responseCode, wasSuccessfull, telemetryProperties);
				}

				stopWatch.Restart();
				responseCode = "200";
				wasSuccessfull = false;
				try
				{
					int count = 1;
					bool batchSuccess = true;
					do
					{
						try
						{
							if (privateBrokeredMessages.Any())
							{
#if NETSTANDARD2_0 || NET48_OR_GREATER
								await PrivateServiceBusPublisher.SendMessagesAsync(privateBrokeredMessages);
#else
								PrivateServiceBusPublisher.SendBatch(privateBrokeredMessages);
#endif
							}
							else
								Logger.LogDebug("An empty collection of private events to publish post validation.");
							break;
						}
#if NETSTANDARD2_0 || NET48_OR_GREATER
						catch (ServiceBusException exception)
						{
							if (exception.Reason == ServiceBusFailureReason.MessageSizeExceeded)
							{
								Logger.LogDebug("The size of the event being sent was too large. Will try sending individually", exception: exception, metaData: new Dictionary<string, object> { { "Command", publicBrokeredMessages } });
								batchSuccess = false;
							}
							else
								throw;
						}
#else
						catch (QuotaExceededException exception)
						{
							Logger.LogDebug("The size of the event being sent was too large. Will try sending individually", exception: exception, metaData: new Dictionary<string, object> { { "Command", publicBrokeredMessages } });
							batchSuccess = false;
						}
#endif
						catch (TimeoutException)
						{
							if (count >= TimeoutOnSendRetryMaximumCount)
								throw;
						}
						if (!batchSuccess)
						{
							foreach (var publicBrokeredMessage in publicBrokeredMessages)
							{
#if NETSTANDARD2_0 || NET48_OR_GREATER
								await PublicServiceBusPublisher.SendMessageAsync(publicBrokeredMessage);
#else
								PublicServiceBusPublisher.Send(publicBrokeredMessage);
#endif
							}
							break;
						}
						count++;
					} while (true);
					wasSuccessfull = true;
				}
				catch
				(
#if NETSTANDARD2_0 || NET48_OR_GREATER
					ServiceBusException
#else
					QuotaExceededException
#endif
					exception
				)
				{
					responseCode = "429";
					Logger.LogError("The size of the event being sent was too large.", exception: exception, metaData: new Dictionary<string, object> { { "Events", privateBrokeredMessages } });
					throw;
				}
				catch (Exception exception)
				{
					responseCode = "500";
					Logger.LogError("An issue occurred while trying to publish an event.", exception: exception, metaData: new Dictionary<string, object> { { "Events", privateBrokeredMessages } });
					throw;
				}
				finally
				{
					TelemetryHelper.TrackDependency("Azure/Servicebus/EventBus", "Event", telemetryName, "Private Bus", startedAt, stopWatch.Elapsed, responseCode, wasSuccessfull, telemetryProperties);
				}

				foreach (string message in sourceEventMessages)
					Logger.LogInfo(message);

				mainWasSuccessfull = true;
			}
			finally
			{
				mainStopWatch.Stop();
				TelemetryHelper.TrackDependency("Azure/Servicebus/EventBus", "Event", telemetryName, null, startedAt, mainStopWatch.Elapsed, responseCode, mainWasSuccessfull, telemetryProperties);
			}
		}

		#endregion
	}
}