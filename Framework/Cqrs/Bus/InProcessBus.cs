﻿#region Copyright
// // -----------------------------------------------------------------------
// // <copyright company="Chinchilla Software Limited">
// // 	Copyright Chinchilla Software Limited. All rights reserved.
// // </copyright>
// // -----------------------------------------------------------------------
#endregion

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Policy;
using System.Threading.Tasks;
using Chinchilla.Logging;
using Cqrs.Authentication;
using Cqrs.Commands;
using Cqrs.Configuration;
using Cqrs.Events;
using Cqrs.Messages;
using SpinWait = Cqrs.Infrastructure.SpinWait;

namespace Cqrs.Bus
{
	/// <summary>
	/// An in process command bus 
	/// (<see cref="ICommandPublisher{TAuthenticationToken}"/> and <see cref="ICommandReceiver{TAuthenticationToken}"/>)
	/// event bus
	/// (<see cref="IEventPublisher{TAuthenticationToken}"/> and <see cref="IEventHandler{TAuthenticationToken,TTarget,TEvent}"/>)
	/// as well as a <see cref="IEventHandlerRegistrar"/> and <see cref="ICommandHandlerRegistrar"/> that requires no networking.
	/// </summary>
	/// <typeparam name="TAuthenticationToken">The <see cref="Type"/> of the authentication token.</typeparam>
	public class InProcessBus<TAuthenticationToken>
#if NET40
		: IPublishAndWaitCommandPublisher<TAuthenticationToken>
		, IEventPublisher<TAuthenticationToken>
		, IEventHandlerRegistrar
		, ICommandHandlerRegistrar
		, ICommandReceiver<TAuthenticationToken>
		, IEventReceiver<TAuthenticationToken>
#else
		: IAsyncPublishAndWaitCommandPublisher<TAuthenticationToken>
		, IAsyncEventPublisher<TAuthenticationToken>
		, IAsyncEventHandlerRegistrar
		, IAsyncCommandHandlerRegistrar
		, IAsyncCommandReceiver<TAuthenticationToken>
		, IAsyncEventReceiver<TAuthenticationToken>
#endif
	{
		/// <summary>
		/// Gets or sets the Route Manager
		/// </summary>
		private static RouteManager Routes { get; set; }

		/// <summary>
		/// Gets or sets the Authentication Token Helper
		/// </summary>
		protected IAuthenticationTokenHelper<TAuthenticationToken> AuthenticationTokenHelper { get; private set; }

		/// <summary>
		/// Gets or sets the CorrelationId Helper
		/// </summary>
		protected ICorrelationIdHelper CorrelationIdHelper { get; private set; }

		/// <summary>
		/// Gets or sets the Dependency Resolver
		/// </summary>
		protected IDependencyResolver DependencyResolver { get; private set; }

		/// <summary>
		/// Gets or sets the Logger
		/// </summary>
		protected ILogger Logger { get; private set; }

		/// <summary>
		/// Gets or sets the Configuration Manager
		/// </summary>
		protected IConfigurationManager ConfigurationManager { get; private set; }

		/// <summary>
		/// Gets or sets the Bus Helper
		/// </summary>
		protected IBusHelper BusHelper { get; private set; }

#if NET40
		/// <summary>
		/// Gets or sets the current list of events waiting to be evaluated for <see cref="PublishAndWait{TCommand,TEvent}(TCommand,Cqrs.Events.IEventReceiver{TAuthenticationToken})"/>
		/// </summary>
#else
		/// <summary>
		/// Gets or sets the current list of events waiting to be evaluated for <see cref="PublishAndWaitAsync{TCommand,TEvent}(TCommand,Cqrs.Events.IEventReceiver{TAuthenticationToken})"/>
		/// </summary>
#endif
		protected IDictionary<Guid, IList<IEvent<TAuthenticationToken>>> EventWaits { get; private set; }

		/// <summary>
		/// Gets or sets the Telemetry Helper
		/// </summary>
		protected ITelemetryHelper TelemetryHelper { get; set; }

		static InProcessBus()
		{
			Routes = new RouteManager();
		}

		/// <summary>
		/// Instantiates a new instance of the <see cref="InProcessBus{TAuthenticationToken}"/> class.
		/// </summary>
		public InProcessBus(IAuthenticationTokenHelper<TAuthenticationToken> authenticationTokenHelper, ICorrelationIdHelper correlationIdHelper, IDependencyResolver dependencyResolver, ILogger logger, IConfigurationManager configurationManager, IBusHelper busHelper)
		{
			AuthenticationTokenHelper = authenticationTokenHelper;
			CorrelationIdHelper = correlationIdHelper;
			DependencyResolver = dependencyResolver;
			Logger = logger;
			ConfigurationManager = configurationManager;
			BusHelper = busHelper;
			EventWaits = new ConcurrentDictionary<Guid, IList<IEvent<TAuthenticationToken>>>();
			TelemetryHelper = configurationManager.CreateTelemetryHelper("Cqrs.InProcessBus.UseApplicationInsightTelemetryHelper", correlationIdHelper);

			Logger.LogDebug("Instantiating InProcessBus");
		}

		/// <summary>
		/// Sets the
		/// <see cref="IMessageWithAuthenticationToken{TAuthenticationToken}.AuthenticationToken"/>,
		/// <see cref="IMessage.CorrelationId"/>,
		/// <see cref="IMessage.OriginatingFramework"/> to "Built-In" and
		/// adds a value of "Built-In" to the <see cref="IMessage.Frameworks"/>
		/// if not already done so
		/// </summary>
		protected virtual void PrepareCommand<TCommand>(TCommand command)
			where TCommand : ICommand<TAuthenticationToken>
		{
			if (command.AuthenticationToken == null || command.AuthenticationToken.Equals(default(TAuthenticationToken)))
				command.AuthenticationToken = AuthenticationTokenHelper.GetAuthenticationToken();
			command.CorrelationId = CorrelationIdHelper.GetCorrelationId();

			if (string.IsNullOrWhiteSpace(command.OriginatingFramework))
				command.OriginatingFramework = "Built-In";

			var frameworks = new List<string>();
			if (command.Frameworks != null)
				frameworks.AddRange(command.Frameworks);
			frameworks.Add("Built-In");
			command.Frameworks = frameworks;
		}

		/// <summary>
		/// Locates a suitable <see cref="ICommandValidator{TAuthenticationToken,TCommand}"/> to validate the provided <paramref name="command"/> and validates the provided <paramref name="command"/> if one is located
		/// Calls <see cref="PrepareCommand{TCommand}"/>
		/// Checks if the provided <paramref name="command"/> is required to be processed
		/// Locates a single <see cref="RouteHandlerDelegate">command handler</see> for the provided <paramref name="command"/>
		/// </summary>
		/// <returns>
		/// False if a suitable <see cref="ICommandValidator{TAuthenticationToken,TCommand}"/> is located and the provided <paramref name="command"/> fails validation,
		/// False if no <see cref="RouteHandlerDelegate">command handler</see> is found but the command isn't required to be handled,
		/// True otherwise.
		/// </returns>
		protected virtual bool PrepareAndValidateCommand<TCommand>(TCommand command, out RouteHandlerDelegate commandHandler)
			where TCommand : ICommand<TAuthenticationToken>
		{
			Type commandType = command.GetType();

			if (command.Frameworks != null && command.Frameworks.Contains("Built-In"))
			{
				Logger.LogInfo("The provided command has already been processed by the Built-In bus.", string.Format("{0}\\PrepareAndValidateEvent({1})", GetType().FullName, commandType.FullName));
				commandHandler = null;
				return false;
			}

			ICommandValidator<TAuthenticationToken, TCommand> commandValidator = null;
			try
			{
				commandValidator = DependencyResolver.Resolve<ICommandValidator<TAuthenticationToken, TCommand>>();
			}
			catch (Exception exception)
			{
				Logger.LogDebug("Locating an ICommandValidator failed.", string.Format("{0}\\Handle({1})", GetType().FullName, commandType.FullName), exception);
			}

			if (commandValidator != null && !commandValidator.IsCommandValid(command))
			{
				Logger.LogInfo("The provided command is not valid.", string.Format("{0}\\Handle({1})", GetType().FullName, commandType.FullName));
				commandHandler = null;
				return false;
			}

			PrepareCommand(command);

			bool isRequired = BusHelper.IsEventRequired(commandType);

			commandHandler = Routes.GetSingleHandler(command, isRequired);
			// This check doesn't require an isRequired check as there will be an exception raised above and handled below.
			if (commandHandler == null)
			{
				Logger.LogDebug(string.Format("The command handler for '{0}' is not required.", commandType.FullName));
				return false;
			}

			return true;
		}

		#region Implementation of ICommandPublisher<TAuthenticationToken>

		/// <summary>
		/// Publishes the provided <paramref name="command"/> on the command bus with a delay
		/// </summary>
#if NET40
		void ICommandPublisher<TAuthenticationToken>.Publish
#else
		async Task IAsyncCommandPublisher<TAuthenticationToken>.PublishAsync
#endif
				<TCommand>(TCommand command, TimeSpan delay)
		{
#if NET40
			Send
#else
			await SendAsync
#endif
				(command, delay);
		}


		/// <summary>
		/// Publishes the provided <paramref name="commands"/> on the command bus.with a delay
		/// </summary>
#if NET40
		void ICommandPublisher<TAuthenticationToken>.Publish
#else
		async Task IAsyncCommandPublisher<TAuthenticationToken>.PublishAsync
#endif
			<TCommand>(IEnumerable<TCommand> commands, TimeSpan delay)
		{
#if NET40
			Send
#else
			await SendAsync
#endif
				(commands, delay);
		}

		/// <summary>
		/// Publishes the provided <paramref name="command"/> on the command bus.
		/// </summary>
#if NET40
		void ICommandPublisher<TAuthenticationToken>.Publish
#else
		async Task IAsyncCommandPublisher<TAuthenticationToken>.PublishAsync
#endif
				<TCommand>(TCommand command)
		{
#if NET40
			Send
#else
			await SendAsync
#endif
				(command);
		}

		/// <summary>
		/// Publishes the provided <paramref name="command"/> on the command bus.
		/// </summary>
		public virtual
#if NET40
			void Send
#else
			async Task SendAsync
#endif
				<TCommand>(TCommand command, TimeSpan? delay = null)
			where TCommand : ICommand<TAuthenticationToken>
		{
			if (delay != null)
			{
				DateTime ready = DateTime.UtcNow.Add(delay.Value);
				SpinWait.SpinUntil(() => { return DateTime.UtcNow > ready; } , 1000);
			}
			DateTimeOffset startedAt = DateTimeOffset.UtcNow;
			Stopwatch mainStopWatch = Stopwatch.StartNew();
			string responseCode = "200";
			bool wasSuccessfull = false;

			IDictionary<string, string> telemetryProperties = new Dictionary<string, string> { { "Type", "InProcessBus" } };
			string telemetryName = $"{command.GetType().FullName}/{command.Id}";
			var telemeteredCommand = command as ITelemeteredMessage;
			if (telemeteredCommand != null)
				telemetryName = telemeteredCommand.TelemetryName;
			telemetryName = $"Command/{telemetryName}";

			try
			{
				RouteHandlerDelegate commandHandler;
				if (!PrepareAndValidateCommand(command, out commandHandler))
					return;

				try
				{
#if NET40
					Action<IMessage> handler = commandHandler.Delegate;
					handler(command);
#else
					Func<IMessage, Task> handler = commandHandler.Delegate;
					await handler(command);
#endif
				}
				catch (Exception exception)
				{
					responseCode = "500";
					Logger.LogError("An issue occurred while trying to publish a command.", exception: exception, metaData: new Dictionary<string, object> {{"Command", command}});
					throw;
				}

				Logger.LogInfo($"A command was sent of type {command.GetType().FullName}.");
				wasSuccessfull = true;
			}
			finally
			{
				mainStopWatch.Stop();
				TelemetryHelper.TrackDependency("InProcessBus/CommandBus", "Command", telemetryName, null, startedAt, mainStopWatch.Elapsed, responseCode, wasSuccessfull, telemetryProperties);
			}
		}

		/// <summary>
		/// Publishes the provided <paramref name="commands"/> on the command bus.
		/// </summary>
#if NET40
		void ICommandPublisher<TAuthenticationToken>.Publish
#else
		async Task IAsyncCommandPublisher<TAuthenticationToken>.PublishAsync
#endif
			<TCommand>(IEnumerable<TCommand> commands)
		{
#if NET40
			Send
#else
			await SendAsync
#endif
				(commands);
		}

		/// <summary>
		/// Publishes the provided <paramref name="commands"/> on the command bus.
		/// </summary>
		public virtual
#if NET40
			void Send
#else
			async Task SendAsync
#endif
				<TCommand>(IEnumerable<TCommand> commands, TimeSpan? delay = null)
			where TCommand : ICommand<TAuthenticationToken>
		{
			if (delay != null)
			{
				DateTime ready = DateTime.UtcNow.Add(delay.Value);
				SpinWait.SpinUntil(() => { return DateTime.UtcNow > ready; }, 1000);
			}

			IEnumerable<TCommand> sourceCommands = commands.ToList();

			DateTimeOffset startedAt = DateTimeOffset.UtcNow;
			Stopwatch mainStopWatch = Stopwatch.StartNew();
			string responseCode = "500";
			bool wasSuccessfull = false;

			IDictionary<string, string> telemetryProperties = new Dictionary<string, string> { { "Type", "InProcessBus" } };
			string telemetryName = "Commands";
			string telemetryNames = string.Empty;
			foreach (TCommand command in sourceCommands)
			{
				string subTelemetryName = $"{command.GetType().FullName}/{command.Id}";
				var telemeteredCommand = command as ITelemeteredMessage;
				if (telemeteredCommand != null)
					subTelemetryName = telemeteredCommand.TelemetryName;
				telemetryNames = $"{telemetryNames}{subTelemetryName},";
			}
			if (telemetryNames.Length > 0)
				telemetryNames = telemetryNames.Substring(0, telemetryNames.Length - 1);
			telemetryProperties.Add("Commands", telemetryNames);

			try
			{
				foreach (TCommand command in sourceCommands)
#if NET40
					Send
#else
					await SendAsync
#endif
						(command);

				responseCode = "200";
				wasSuccessfull = true;
			}
			finally
			{
				mainStopWatch.Stop();
				TelemetryHelper.TrackDependency("InProcessBus/CommandBus", "Command", telemetryName, null, startedAt, mainStopWatch.Elapsed, responseCode, wasSuccessfull, telemetryProperties);
			}
		}

		#endregion

		#region Implementation of ISendAndWaitCommandSender<TAuthenticationToken>

		/// <summary>
		/// Sends the provided <paramref name="command"></paramref> and waits for an event of <typeparamref name="TEvent"/>
		/// </summary>
		/// <param name="command">The <typeparamref name="TCommand"/> to send.</param>
		/// <param name="eventReceiver">If provided, is the <see cref="IEventReceiver{TAuthenticationToken}" /> that the event is expected to be returned on.</param>
		public virtual
#if NET40
			TEvent SendAndWait
#else
			async Task<TEvent> SendAndWaitAsync
#endif
				<TCommand, TEvent>(TCommand command, IEventReceiver<TAuthenticationToken> eventReceiver = null)
			where TCommand : ICommand<TAuthenticationToken>
		{
			return
#if NET40
				SendAndWait
#else
				await SendAndWaitAsync
#endif
				<TCommand, TEvent>(command, -1, eventReceiver);
		}

		/// <summary>
		/// Sends the provided <paramref name="command"></paramref> and waits for an event of <typeparamref name="TEvent"/> or exits if the specified timeout is expired.
		/// </summary>
		/// <param name="command">The <typeparamref name="TCommand"/> to send.</param>
		/// <param name="millisecondsTimeout">The number of milliseconds to wait, or <see cref="F:System.Threading.Timeout.Infinite"/> (-1) to wait indefinitely.</param>
		/// <param name="eventReceiver">If provided, is the <see cref="IEventReceiver{TAuthenticationToken}" /> that the event is expected to be returned on.</param>
		public virtual
#if NET40
			TEvent SendAndWait
#else
			async Task<TEvent> SendAndWaitAsync
#endif
				<TCommand, TEvent>(TCommand command, int millisecondsTimeout, IEventReceiver<TAuthenticationToken> eventReceiver = null)
			where TCommand : ICommand<TAuthenticationToken>
		{
			return
#if NET40
				SendAndWait
#else
				await SendAndWaitAsync
#endif
					(command, events => (TEvent)events.SingleOrDefault(@event => @event is TEvent), millisecondsTimeout, eventReceiver);
		}

		/// <summary>
		/// Sends the provided <paramref name="command"></paramref> and waits for an event of <typeparamref name="TEvent"/> or exits if the specified timeout is expired.
		/// </summary>
		/// <param name="command">The <typeparamref name="TCommand"/> to send.</param>
		/// <param name="timeout">A <see cref="T:System.TimeSpan"/> that represents the number of milliseconds to wait, or a TimeSpan that represents -1 milliseconds to wait indefinitely.</param>
		/// <param name="eventReceiver">If provided, is the <see cref="IEventReceiver{TAuthenticationToken}" /> that the event is expected to be returned on.</param>
		public virtual
#if NET40
			TEvent SendAndWait
#else
			async Task<TEvent> SendAndWaitAsync
#endif
				<TCommand, TEvent>(TCommand command, TimeSpan timeout, IEventReceiver<TAuthenticationToken> eventReceiver = null)
			where TCommand : ICommand<TAuthenticationToken>
		{
			long num = (long)timeout.TotalMilliseconds;
			if (num < -1L || num > int.MaxValue)
				throw new ArgumentOutOfRangeException("timeout", timeout, "SpinWait_SpinUntil_TimeoutWrong");
			return
#if NET40
				SendAndWait
#else
				await SendAndWaitAsync
#endif
					<TCommand, TEvent>(command, (int)timeout.TotalMilliseconds, eventReceiver);
		}

		/// <summary>
		/// Sends the provided <paramref name="command"></paramref> and waits until the specified condition is satisfied an event of <typeparamref name="TEvent"/>
		/// </summary>
		/// <param name="command">The <typeparamref name="TCommand"/> to send.</param>
		/// <param name="condition">A delegate to be executed over and over until it returns the <typeparamref name="TEvent"/> that is desired, return null to keep trying.</param>
		/// <param name="eventReceiver">If provided, is the <see cref="IEventReceiver{TAuthenticationToken}" /> that the event is expected to be returned on.</param>
		public virtual
#if NET40
			TEvent SendAndWait
#else
			async Task<TEvent> SendAndWaitAsync
#endif
				<TCommand, TEvent>(TCommand command, Func<IEnumerable<IEvent<TAuthenticationToken>>, TEvent> condition, IEventReceiver<TAuthenticationToken> eventReceiver = null)
			where TCommand : ICommand<TAuthenticationToken>
		{
			return
#if NET40
				PublishAndWait
#else
				await PublishAndWaitAsync
#endif
					(command, condition, eventReceiver);
		}

		/// <summary>
		/// Sends the provided <paramref name="command"></paramref> and waits for an event of <typeparamref name="TEvent"/> or exits if the specified timeout is expired.
		/// </summary>
		/// <param name="command">The <typeparamref name="TCommand"/> to send.</param>
		/// <param name="condition">A delegate to be executed over and over until it returns the <typeparamref name="TEvent"/> that is desired, return null to keep trying.</param>
		/// <param name="millisecondsTimeout">The number of milliseconds to wait, or <see cref="F:System.Threading.Timeout.Infinite"/> (-1) to wait indefinitely.</param>
		/// <param name="eventReceiver">If provided, is the <see cref="IEventReceiver{TAuthenticationToken}" /> that the event is expected to be returned on.</param>
		public virtual
#if NET40
			TEvent SendAndWait
#else
			async Task<TEvent> SendAndWaitAsync
#endif
				<TCommand, TEvent>(TCommand command, Func<IEnumerable<IEvent<TAuthenticationToken>>, TEvent> condition, int millisecondsTimeout, IEventReceiver<TAuthenticationToken> eventReceiver = null)
			where TCommand : ICommand<TAuthenticationToken>
		{
			return
#if NET40
				PublishAndWait
#else
				await PublishAndWaitAsync
#endif
					(command, condition, millisecondsTimeout, eventReceiver);
		}

		/// <summary>
		/// Sends the provided <paramref name="command"></paramref> and waits for an event of <typeparamref name="TEvent"/> or exits if the specified timeout is expired.
		/// </summary>
		/// <param name="command">The <typeparamref name="TCommand"/> to send.</param>
		/// <param name="condition">A delegate to be executed over and over until it returns the <typeparamref name="TEvent"/> that is desired, return null to keep trying.</param>
		/// <param name="timeout">A <see cref="T:System.TimeSpan"/> that represents the number of milliseconds to wait, or a TimeSpan that represents -1 milliseconds to wait indefinitely.</param>
		/// <param name="eventReceiver">If provided, is the <see cref="IEventReceiver{TAuthenticationToken}" /> that the event is expected to be returned on.</param>
		public virtual
#if NET40
			TEvent SendAndWait
#else
			async Task<TEvent> SendAndWaitAsync
#endif
				<TCommand, TEvent>(TCommand command, Func<IEnumerable<IEvent<TAuthenticationToken>>, TEvent> condition, TimeSpan timeout, IEventReceiver<TAuthenticationToken> eventReceiver = null)
			where TCommand : ICommand<TAuthenticationToken>
		{
			return
#if NET40
				PublishAndWait
#else
				await PublishAndWaitAsync
#endif
					(command, condition, timeout, eventReceiver);
		}

		#endregion

		#region Implementation of IEventPublisher<TAuthenticationToken>

		/// <summary>
		/// Publishes the provided <paramref name="event"/> on the event bus.
		/// </summary>
		public virtual
#if NET40
			void Publish
#else
			async Task PublishAsync
#endif
				<TEvent>(TEvent @event)
			where TEvent : IEvent<TAuthenticationToken>
		{
			Type eventType = @event.GetType();
			string eventName = eventType.FullName;
			ISagaEvent<TAuthenticationToken> sagaEvent = @event as ISagaEvent<TAuthenticationToken>;
			if (sagaEvent != null)
				eventName = $"Cqrs.Events.SagaEvent[{sagaEvent.Event.GetType().FullName}]";

			DateTimeOffset startedAt = DateTimeOffset.UtcNow;
			Stopwatch mainStopWatch = Stopwatch.StartNew();
			string responseCode = "200";
			bool wasSuccessfull = false;

			IDictionary<string, string> telemetryProperties = new Dictionary<string, string> { { "Type", "InProcessBus" } };
			string telemetryName = $"{eventName}/{@event.GetIdentity()}/{@event.Id}";
			var telemeteredEvent = @event as ITelemeteredMessage;
			if (telemeteredEvent != null)
				telemetryName = telemeteredEvent.TelemetryName;
			telemetryName = $"Event/{telemetryName}";

			try
			{
				if (@event.Frameworks != null && @event.Frameworks.Contains("Built-In"))
				{
					Logger.LogInfo("The provided event has already been processed by the Built-In bus.", $"{GetType().FullName}\\PrepareAndValidateEvent({eventType.FullName})");
					return;
				}

				if (@event.AuthenticationToken == null || @event.AuthenticationToken.Equals(default(TAuthenticationToken)))
					@event.AuthenticationToken = AuthenticationTokenHelper.GetAuthenticationToken();
				@event.CorrelationId = CorrelationIdHelper.GetCorrelationId();

				if (string.IsNullOrWhiteSpace(@event.OriginatingFramework))
				{
					@event.TimeStamp = DateTimeOffset.UtcNow;
					@event.OriginatingFramework = "Built-In";
				}

				var frameworks = new List<string>();
				if (@event.Frameworks != null)
					frameworks.AddRange(@event.Frameworks);
				frameworks.Add("Built-In");
				@event.Frameworks = frameworks;

				bool isRequired;
				if (!ConfigurationManager.TryGetSetting($"{eventName}.IsRequired", out isRequired))
					isRequired = true;

#if NET40
				IEnumerable<Action<IMessage>>
#else
				IEnumerable<Func<IMessage, Task>>
#endif
					handlers = Routes.GetHandlers(@event, isRequired).Select(x => x.Delegate).ToList();
				// This check doesn't require an isRequired check as there will be an exception raised above and handled below.
				if (!handlers.Any())
					Logger.LogDebug($"An event handler for '{eventName}' is not required.");

				foreach (var handler in handlers)
				{
					IList<IEvent<TAuthenticationToken>> events;
					if (EventWaits.TryGetValue(@event.CorrelationId, out events))
						events.Add(@event);
#if NET40
					handler
#else
					await handler
#endif
						(@event);
				}

				Logger.LogInfo($"An event was sent of type {eventName}.");
				wasSuccessfull = true;
			}
			catch (Exception exception)
			{
				responseCode = "500";
				Logger.LogError("An issue occurred while trying to publish an event.", exception: exception, metaData: new Dictionary<string, object> { { "Event", @event } });
				throw;
			}
			finally
			{
				mainStopWatch.Stop();
				TelemetryHelper.TrackDependency("InProcessBus/EventBus", "Event", telemetryName, null, startedAt, mainStopWatch.Elapsed, responseCode, wasSuccessfull, telemetryProperties);
			}
		}

		/// <summary>
		/// Publishes the provided <paramref name="events"/> on the event bus.
		/// </summary>
		public virtual
#if NET40
			void Publish
#else
			async Task PublishAsync
#endif
				<TEvent>(IEnumerable<TEvent> events)
			where TEvent : IEvent<TAuthenticationToken>
		{
			IEnumerable<TEvent> sourceEvents = events.ToList();

			DateTimeOffset startedAt = DateTimeOffset.UtcNow;
			Stopwatch mainStopWatch = Stopwatch.StartNew();
			string responseCode = "500";
			bool wasSuccessfull = false;

			IDictionary<string, string> telemetryProperties = new Dictionary<string, string> { { "Type", "InProcessBus" } };
			string telemetryName = "Events";
			string telemetryNames = string.Empty;
			foreach (TEvent @event in sourceEvents)
			{
				string subTelemetryName = $"{@event.GetType().FullName}/{@event.GetIdentity()}/{@event.Id}";
				var telemeteredCommand = @event as ITelemeteredMessage;
				if (telemeteredCommand != null)
					subTelemetryName = telemeteredCommand.TelemetryName;
				telemetryNames = $"{telemetryNames}{subTelemetryName},";
			}
			if (telemetryNames.Length > 0)
				telemetryNames = telemetryNames.Substring(0, telemetryNames.Length - 1);
			telemetryProperties.Add("Events", telemetryNames);

			try
			{
				foreach (TEvent @event in sourceEvents)
#if NET40
					Publish
#else
					await PublishAsync
#endif
						(@event);

				responseCode = "200";
				wasSuccessfull = true;
			}
			finally
			{
				mainStopWatch.Stop();
				TelemetryHelper.TrackDependency("InProcessBus/EventBus", "Event", telemetryName, null, startedAt, mainStopWatch.Elapsed, responseCode, wasSuccessfull, telemetryProperties);
			}
		}

		#endregion

		#region Implementation of IPublishAndWaitCommandPublisher<TAuthenticationToken>

		/// <summary>
		/// Publishes the provided <paramref name="command"></paramref> and waits for an event of <typeparamref name="TEvent"/>
		/// </summary>
		/// <param name="command">The <typeparamref name="TCommand"/> to publish.</param>
		/// <param name="eventReceiver">If provided, is the <see cref="IEventReceiver{TAuthenticationToken}" /> that the event is expected to be returned on.</param>
		public virtual
#if NET40
			TEvent PublishAndWait
#else
			async Task<TEvent> PublishAndWaitAsync
#endif
			<TCommand, TEvent>(TCommand command, IEventReceiver<TAuthenticationToken> eventReceiver = null) where TCommand : ICommand<TAuthenticationToken>
		{
			return
#if NET40
				PublishAndWait
#else
				await PublishAndWaitAsync
#endif
				<TCommand, TEvent>(command, -1, eventReceiver);
		}

		/// <summary>
		/// Publishes the provided <paramref name="command"></paramref> and waits for an event of <typeparamref name="TEvent"/> or exits if the specified timeout is expired.
		/// </summary>
		/// <param name="command">The <typeparamref name="TCommand"/> to publish.</param>
		/// <param name="millisecondsTimeout">The number of milliseconds to wait, or <see cref="F:System.Threading.Timeout.Infinite"/> (-1) to wait indefinitely.</param>
		/// <param name="eventReceiver">If provided, is the <see cref="IEventReceiver{TAuthenticationToken}" /> that the event is expected to be returned on.</param>
		public virtual
#if NET40
			TEvent PublishAndWait
#else
			async Task<TEvent> PublishAndWaitAsync
#endif
			<TCommand, TEvent>(TCommand command, int millisecondsTimeout, IEventReceiver<TAuthenticationToken> eventReceiver = null) where TCommand : ICommand<TAuthenticationToken>
		{
			return
#if NET40
				PublishAndWait
#else
				await PublishAndWaitAsync
#endif
					(command, events => (TEvent)events.SingleOrDefault(@event => @event is TEvent), millisecondsTimeout, eventReceiver);
		}

		/// <summary>
		/// Publishes the provided <paramref name="command"></paramref> and waits for an event of <typeparamref name="TEvent"/> or exits if the specified timeout is expired.
		/// </summary>
		/// <param name="command">The <typeparamref name="TCommand"/> to publish.</param>
		/// <param name="timeout">A <see cref="T:System.TimeSpan"/> that represents the number of milliseconds to wait, or a TimeSpan that represents -1 milliseconds to wait indefinitely.</param>
		/// <param name="eventReceiver">If provided, is the <see cref="IEventReceiver{TAuthenticationToken}" /> that the event is expected to be returned on.</param>
		public virtual
#if NET40
			TEvent PublishAndWait
#else
			async Task<TEvent> PublishAndWaitAsync
#endif
			<TCommand, TEvent>(TCommand command, TimeSpan timeout, IEventReceiver<TAuthenticationToken> eventReceiver = null) where TCommand : ICommand<TAuthenticationToken>
		{
			long num = (long)timeout.TotalMilliseconds;
			if (num < -1L || num > int.MaxValue)
				throw new ArgumentOutOfRangeException("timeout", timeout, "SpinWait_SpinUntil_TimeoutWrong");
			return
#if NET40
				PublishAndWait
#else
				await PublishAndWaitAsync
#endif
					<TCommand, TEvent>(command, (int)timeout.TotalMilliseconds, eventReceiver);
		}

		/// <summary>
		/// Publishes the provided <paramref name="command"></paramref> and waits until the specified condition is satisfied an event of <typeparamref name="TEvent"/>
		/// </summary>
		/// <param name="command">The <typeparamref name="TCommand"/> to publish.</param>
		/// <param name="condition">A delegate to be executed over and over until it returns the <typeparamref name="TEvent"/> that is desired, return null to keep trying.</param>
		/// <param name="eventReceiver">If provided, is the <see cref="IEventReceiver{TAuthenticationToken}" /> that the event is expected to be returned on.</param>
		public virtual
#if NET40
			TEvent PublishAndWait
#else
			async Task<TEvent> PublishAndWaitAsync
#endif
			<TCommand, TEvent>(TCommand command, Func<IEnumerable<IEvent<TAuthenticationToken>>, TEvent> condition, IEventReceiver<TAuthenticationToken> eventReceiver = null) where TCommand : ICommand<TAuthenticationToken>
		{
			return
#if NET40
				PublishAndWait
#else
				await PublishAndWaitAsync
#endif
					(command, condition, -1, eventReceiver);
		}

		/// <summary>
		/// Publishes the provided <paramref name="command"></paramref> and waits for an event of <typeparamref name="TEvent"/> or exits if the specified timeout is expired.
		/// </summary>
		/// <param name="command">The <typeparamref name="TCommand"/> to publish.</param>
		/// <param name="condition">A delegate to be executed over and over until it returns the <typeparamref name="TEvent"/> that is desired, return null to keep trying.</param>
		/// <param name="millisecondsTimeout">The number of milliseconds to wait, or <see cref="F:System.Threading.Timeout.Infinite"/> (-1) to wait indefinitely.</param>
		/// <param name="eventReceiver">If provided, is the <see cref="IEventReceiver{TAuthenticationToken}" /> that the event is expected to be returned on.</param>
		public virtual
#if NET40
			TEvent PublishAndWait
#else
			async Task<TEvent> PublishAndWaitAsync
#endif
			<TCommand, TEvent>(TCommand command, Func<IEnumerable<IEvent<TAuthenticationToken>>, TEvent> condition, int millisecondsTimeout, IEventReceiver<TAuthenticationToken> eventReceiver = null)
			where TCommand : ICommand<TAuthenticationToken>
		{
			DateTimeOffset startedAt = DateTimeOffset.UtcNow;
			Stopwatch mainStopWatch = Stopwatch.StartNew();
			string responseCode = "200";
			bool wasSuccessfull = false;

			IDictionary<string, string> telemetryProperties = new Dictionary<string, string> { { "Type", "InProcessBus" } };
			string telemetryName = $"{command.GetType().FullName}/{command.Id}";
			var telemeteredCommand = command as ITelemeteredMessage;
			if (telemeteredCommand != null)
				telemetryName = telemeteredCommand.TelemetryName;
			telemetryName = $"Command/{telemetryName}";

			TEvent result;

			try
			{
				if (eventReceiver != null)
					throw new NotSupportedException("Specifying a different event receiver is not yet supported.");
				RouteHandlerDelegate commandHandler;
				if (!PrepareAndValidateCommand(command, out commandHandler))
					return (TEvent)(object)null;

				result = (TEvent)(object)null;
				EventWaits.Add(command.CorrelationId, new List<IEvent<TAuthenticationToken>>());

#if NET40
				Action<IMessage>
#else
				Func<IMessage, Task>
#endif
					handler = commandHandler.Delegate;
#if NET40
				handler
#else
				await handler
#endif
					(command);
				Logger.LogInfo($"A command was sent of type {command.GetType().FullName}.");
				wasSuccessfull = true;
			}
			finally
			{
				mainStopWatch.Stop();
				TelemetryHelper.TrackDependency("InProcessBus/CommandBus", "Command", telemetryName, null, startedAt, mainStopWatch.Elapsed, responseCode, wasSuccessfull, telemetryProperties);
			}

			SpinWait.SpinUntil(() =>
			{
				IList<IEvent<TAuthenticationToken>> events = EventWaits[command.CorrelationId];

				result = condition(events);

				return result != null;
			}, millisecondsTimeout, SpinWait.DefaultSleepInMilliseconds);

			TelemetryHelper.TrackDependency("InProcessBus/CommandBus", "Command/AndWait", $"Command/AndWait{telemetryName.Substring(7)}", null, startedAt, mainStopWatch.Elapsed, responseCode, wasSuccessfull, telemetryProperties);
			return result;
		}

		/// <summary>
		/// Publishes the provided <paramref name="command"></paramref> and waits for an event of <typeparamref name="TEvent"/> or exits if the specified timeout is expired.
		/// </summary>
		/// <param name="command">The <typeparamref name="TCommand"/> to publish.</param>
		/// <param name="condition">A delegate to be executed over and over until it returns the <typeparamref name="TEvent"/> that is desired, return null to keep trying.</param>
		/// <param name="timeout">A <see cref="T:System.TimeSpan"/> that represents the number of milliseconds to wait, or a TimeSpan that represents -1 milliseconds to wait indefinitely.</param>
		/// <param name="eventReceiver">If provided, is the <see cref="IEventReceiver{TAuthenticationToken}" /> that the event is expected to be returned on.</param>
		public virtual
#if NET40
			TEvent PublishAndWait
#else
			async Task<TEvent> PublishAndWaitAsync
#endif
			<TCommand, TEvent>(TCommand command, Func<IEnumerable<IEvent<TAuthenticationToken>>, TEvent> condition, TimeSpan timeout, IEventReceiver<TAuthenticationToken> eventReceiver = null)
			where TCommand : ICommand<TAuthenticationToken>
		{
			long num = (long)timeout.TotalMilliseconds;
			if (num < -1L || num > int.MaxValue)
				throw new ArgumentOutOfRangeException("timeout", timeout, "SpinWait_SpinUntil_TimeoutWrong");
			return
#if NET40
				PublishAndWait
#else
				await PublishAndWaitAsync
#endif
					(command, condition, (int)timeout.TotalMilliseconds, eventReceiver);
		}

		#endregion

		#region Implementation of IHandlerRegistrar

		/// <summary>
		/// Register an event or command handler that will listen and respond to events or commands.
		/// </summary>
		public virtual
#if NET40
			void RegisterHandler
#else
			async Task RegisterHandlerAsync
#endif
			<TMessage>(
#if NET40
			Action<TMessage>
#else
			Func<TMessage, Task>
#endif
				handler, Type targetedType, bool holdMessageLock = true)
			where TMessage : IMessage
		{
#if NET40
			Action<TMessage>
#else
			Func<TMessage, Task>
#endif
				registerableHandler = BusHelper.BuildTelemeteredActionHandler<TMessage, TAuthenticationToken>(TelemetryHelper, handler, holdMessageLock, "In-Process/Bus");

			Routes.RegisterHandler(registerableHandler, targetedType);

			TelemetryHelper.TrackEvent(string.Format("Cqrs/RegisterHandler/{0}", typeof(TMessage).FullName), new Dictionary<string, string> { { "Type", "In-Process/Bus" } });
			TelemetryHelper.Flush();

#if NET40
#else
			await Task.CompletedTask;
#endif
		}

		/// <summary>
		/// Register an event or command handler that will listen and respond to events or commands.
		/// </summary>
		public virtual
#if NET40
			void RegisterHandler
#else
			async Task RegisterHandlerAsync
#endif
			<TMessage>(
#if NET40
			Action<TMessage>
#else
			Func<TMessage, Task>
#endif
				handler, bool holdMessageLock = true)
			where TMessage : IMessage
		{
#if NET40
			RegisterHandler
#else
			await RegisterHandlerAsync
#endif
				(handler, null, holdMessageLock);
		}

		/// <summary>
		/// Register an event handler that will listen and respond to all events.
		/// </summary>
		public virtual
#if NET40
			void RegisterGlobalEventHandler
#else
			async Task RegisterGlobalEventHandlerAsync
#endif
			<TMessage>(
#if NET40
			Action<TMessage>
#else
			Func<TMessage, Task>
#endif
			handler, bool holdMessageLock = true) where TMessage : IMessage
		{
#if NET40
			Action<TMessage>
#else
			Func<TMessage, Task>
#endif
			registerableHandler = BusHelper.BuildTelemeteredActionHandler<TMessage, TAuthenticationToken>(TelemetryHelper, handler, holdMessageLock, "In-Process/Bus");

			Routes.RegisterGlobalEventHandler(registerableHandler, holdMessageLock);

			TelemetryHelper.TrackEvent(string.Format("Cqrs/RegisterGlobalEventHandler/{0}", typeof(TMessage).FullName), new Dictionary<string, string> { { "Type", "In-Process/Bus" } });
			TelemetryHelper.Flush();

#if NET40
#else
			await Task.CompletedTask;
#endif
		}

		#endregion

		#region Implementation of ICommandReceiver

		/// <summary>
		/// Receives a <see cref="ICommand{TAuthenticationToken}"/> from the command bus.
		/// </summary>
		public virtual
#if NET40
			bool? ReceiveCommand
#else
			async Task<bool?> ReceiveCommandAsync
#endif
			(ICommand<TAuthenticationToken> command)
		{
#if NET40
			Send
#else
			await SendAsync
#endif
				(command);
			return true;
		}

		/// <summary>
		/// Receives an <see cref="IEvent{TAuthenticationToken}"/> from the event bus.
		/// </summary>
		public virtual
#if NET40
			bool? ReceiveEvent
#else
			async Task<bool?> ReceiveEventAsync
#endif
			(IEvent<TAuthenticationToken> @event)
		{
#if NET40
			Publish
#else
			await PublishAsync
#endif
				(@event);
			return true;
		}

		void ICommandReceiver.Start()
		{
			// This is in-process so doesn't need to do anything
		}

		#endregion

		#region Implementation of IEventReceiver

		void IEventReceiver.Start()
		{
			// This is in-process so doesn't need to do anything
		}

		#endregion
	}
}