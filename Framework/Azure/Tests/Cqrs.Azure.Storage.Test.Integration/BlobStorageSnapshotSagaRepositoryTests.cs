#region Copyright
// // -----------------------------------------------------------------------
// // <copyright company="Chinchilla Software Limited">
// // 	Copyright Chinchilla Software Limited. All rights reserved.
// // </copyright>
// // -----------------------------------------------------------------------
#endregion

using System;
using System.Collections.Generic;

using Chinchilla.Logging;
using Chinchilla.Logging.Configuration;
using Chinchilla.StateManagement.Threaded;
using Cqrs.Azure.ServiceBus.Tests.Unit;
using Cqrs.Azure.Storage.Events;
using Cqrs.Commands;
using Cqrs.Configuration;
using Cqrs.Domain;
using Cqrs.Domain.Factories;
using Cqrs.Events;
using Cqrs.Snapshots;
using Moq;
using NUnit.Framework;

using TestClass = NUnit.Framework.TestFixtureAttribute;
using TestMethod = NUnit.Framework.TestAttribute;
using TestInitialize = NUnit.Framework.SetUpAttribute;
using TestCleanup = NUnit.Framework.TearDownAttribute;
using TestContext = System.Object;





#if NET472
#else
using System.Threading.Tasks;
#endif
#if NET472_OR_GREATER
#else
using Cqrs.Azure.ConfigurationManager;
using Microsoft.Extensions.Configuration;
#endif

namespace Cqrs.Azure.Storage.Test.Integration
{
	/// <summary>
	/// A series of tests on the <see cref="BlobStorageEventStore{TAuthenticationToken}"/> class
	/// </summary>
	[TestClass]
	public class BlobStorageSnapshotSagaRepositoryTests
	{
		/// <summary>
		/// Tests the <see cref="IEventStore{TAuthenticationToken}.Save"/> method
		/// Passing a valid test <see cref="IEvent{TAuthenticationToken}"/>
		/// Expecting the test <see cref="IEvent{TAuthenticationToken}"/> is able to be read.
		/// </summary>
		[TestMethod]
		public virtual
#if NET472
			void
#else
			async Task
#endif
				Get_ValidEvent_EventCanBeRetreived()
		{
			// Arrange
			IConfigurationManager configurationManager;
#if NET472_OR_GREATER
			configurationManager = new Configuration.ConfigurationManager();
#else
			IConfigurationRoot config = new ConfigurationBuilder()
				.AddJsonFile("cqrs.json", optional: true, reloadOnChange: true)
				.AddEnvironmentVariables()
				.Build();

			configurationManager = new CloudConfigurationManager(config);
			DependencyResolver.ConfigurationManager = configurationManager;
#endif

			var mockRepository = new MockRepository(MockBehavior.Strict);

			var mockEventPublisher = mockRepository.Create<
#if NET472
				IEventPublisher
#else
				IAsyncEventPublisher
#endif
				<Guid>>();
			mockEventPublisher
				.Setup(x => x.
#if NET472
					Publish
#else
					PublishAsync
#endif
					(It.Is<IEvent<Guid>>(y => true)))
#if NET472
#else
					.Returns(Task.CompletedTask)
#endif
				;
			mockEventPublisher
				.Setup(x => x.
#if NET472
					Publish
#else
					PublishAsync
#endif
					(It.Is<IEnumerable<IEvent<Guid>>>(y => true)))
#if NET472
#else
					.Returns(Task.CompletedTask)
#endif
				;
			var mockCommandPublisher = mockRepository.Create <
#if NET472
				ICommandPublisher
#else
				IAsyncCommandPublisher
#endif
				<Guid>>();
			mockCommandPublisher
				.Setup(x => x.
#if NET472
					Publish
#else
					PublishAsync
#endif
					(It.Is<ICommand<Guid>>(y => true)))
#if NET472
#else
					.Returns(Task.CompletedTask)
#endif
				;
			mockCommandPublisher
				.Setup(x => x.
#if NET472
					Publish
#else
					PublishAsync
#endif
					(It.Is<IEnumerable<ICommand<Guid>>>(y => true)))
#if NET472
#else
					.Returns(Task.CompletedTask)
#endif
				;

			Mock<IDependencyResolver> mockDependencyResolver = mockRepository.Create<IDependencyResolver>();
			mockDependencyResolver
				.Setup(x => x.Resolve<IConfigurationManager>())
				.Returns(configurationManager);
			mockDependencyResolver
				.Setup(x => x.Resolve<
#if NET472
				ICommandPublisher
#else
				IAsyncCommandPublisher
#endif
				<Guid>>())
				.Returns(mockCommandPublisher.Object);
			mockDependencyResolver
				.Setup(x => x.Resolve<
#if NET472
				IEventPublisher
#else
				IAsyncEventPublisher
#endif
				<Guid>>())
				.Returns(mockEventPublisher.Object);

			var correlationIdHelper = new CorrelationIdHelper(new ContextItemCollectionFactory());
			correlationIdHelper.SetCorrelationId(Guid.NewGuid());
			var logger = new ConsoleLogger(new LoggerSettingsConfigurationSection(), correlationIdHelper);
			var snapshotStore = new BlobStorageSnapshotStore(configurationManager, new SnapshotDeserialiser(), logger, correlationIdHelper, new DefaultSnapshotBuilder(), new BlobStorageSnapshotStoreConnectionStringFactory(configurationManager, logger));
			IAggregateFactory aggregateFactory = new AggregateFactory(mockDependencyResolver.Object, logger);
			var eventStore = new BlobStorageEventStore<Guid>(new DefaultEventBuilder<Guid>(), new EventDeserialiser<Guid>(), logger, new BlobStorageEventStoreConnectionStringFactory(configurationManager, logger));
			var sagaRepository = new SagaRepository<Guid>(aggregateFactory, eventStore, mockEventPublisher.Object, mockCommandPublisher.Object, correlationIdHelper);
			var snapshotRepository = new SnapshotSagaRepository<Guid>(snapshotStore, new DefaultSnapshotStrategy<Guid>(), sagaRepository, eventStore, aggregateFactory);

			var id1 = Guid.NewGuid();
			var id2 = Guid.NewGuid();
			var saga1 = aggregateFactory.Create<TestSnapshotSaga>(id1);
			var saga2 = aggregateFactory.Create<TestSnapshotSaga>(id2);

			var unitOfWork = new SagaUnitOfWork<Guid>(snapshotRepository, sagaRepository);

			// Act
			for (int i = 0; i < 40; i++)
			{
#if NET472
				unitOfWork.Add
#else
				await unitOfWork.AddAsync
#endif
					(saga1, true);
#if NET472
				unitOfWork.Add
#else
				await unitOfWork.AddAsync
#endif
					(saga2, true);

				var @event = new TestEvent
				{
					Rsn = i % 2 == 1 ? id1 : id2,
					Id = i % 2 == 1 ? id1 : id2,
					CorrelationId = correlationIdHelper.GetCorrelationId(),
					Frameworks = new List<string> { $"Test {i}" },
					TimeStamp = DateTimeOffset.UtcNow
				};
				(
					i % 2 == 1
						? saga1
						: saga2
				).Handle(@event);

#if NET472
				unitOfWork.Commit();
#else
				await unitOfWork.CommitAsync();
#endif
			}

			// Assert
			TestSnapshotSaga _saga1 =
#if NET472
				unitOfWork.Get
#else
				await unitOfWork.GetAsync
#endif
					<TestSnapshotSaga>(id1, useSnapshots: true);
			TestSnapshotSaga _saga2 =
#if NET472
				unitOfWork.Get
#else
				await unitOfWork.GetAsync
#endif
					<TestSnapshotSaga>(id2, useSnapshots: true);
			Assert.AreEqual(20, _saga1.EventCount);
			Assert.AreEqual(20, _saga2.EventCount);
		}
	}
}