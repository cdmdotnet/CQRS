using System;
using Chinchilla.Logging;
using Cqrs.Domain;
using Cqrs.Authentication;
using Cqrs.Configuration;
using Cqrs.Events;

namespace Cqrs.Tests.Substitutes
{
	public class TestSagaEventHandlers
		: SagaEventHandler<ISingleSignOnToken, TestSaga>
		, IEventHandler<ISingleSignOnToken, TestAggregateDidSomething>
		, IEventHandler<ISingleSignOnToken, TestAggregateDidSomethingElse>
		, IEventHandler<ISingleSignOnToken, TestAggregateDidSomethingElse2>
	{
		public int TimesRun { get; set; }

		public TestSagaEventHandlers(IDependencyResolver dependencyResolver, ILogger logger, ISagaUnitOfWork<ISingleSignOnToken> sagaUnitOfWork) : base(dependencyResolver, logger, sagaUnitOfWork)
		{
		}

		/// <summary>
		/// A constructor for the <see cref="Cqrs.Domain.Factories.IAggregateFactory"/>
		/// </summary>
		public TestSagaEventHandlers(IDependencyResolver dependencyResolver, ILogger logger) : base(dependencyResolver, logger)
		{
		}

		#region Implementation of IMessageHandler<in TestAggregateDidSomething>

		public void Handle(TestAggregateDidSomething message)
		{
			// There are two ways for this to pan out.
			// 1) Events WILL arrive in order in which case this handler would ADD TO and all others would GET FROM the UOW
			// 2) Events may not arrive in order in which case all handlers should try to GET FROM and if it fails ADD TO the UOW
			// Given this is a test, we'll code for the first.

			var saga = new TestSaga(DependencyResolver, message.Id == Guid.Empty ? Guid.NewGuid() : message.Id);
			saga.Handle(message);
			SagaUnitOfWork.Add(saga);
			SagaUnitOfWork.Commit();

			TimesRun++;
		}

		#endregion

		#region Implementation of IMessageHandler<in TestAggregateDidSomethingElse>

		public void Handle(TestAggregateDidSomethingElse message)
		{
			TestSaga saga = GetSaga(message.Id);
			saga.Handle(message);
			SagaUnitOfWork.Add(saga);
			SagaUnitOfWork.Commit();

			TimesRun++;
		}

		#endregion

		#region Implementation of IMessageHandler<in TestAggregateDidSomethingElse2>

		public void Handle(TestAggregateDidSomethingElse2 message)
		{
			TestSaga saga = GetSaga(message.Id);
			saga.Handle(message);
			SagaUnitOfWork.Add(saga);
			SagaUnitOfWork.Commit();

			TimesRun++;
		}

		#endregion
	}

	public class TestSaga : Saga<ISingleSignOnToken>
	{
		public int DidSomethingCount;

		public bool DidSomething;

		public bool DidSomethingElse;

		public bool DidSomethingElse2;

		public bool Responded;

		private TestSaga(IDependencyResolver dependencyResolver)
			: base(dependencyResolver, dependencyResolver.Resolve<ILogger>())
		{
			ApplyChange(new SagaEvent<ISingleSignOnToken>(new TestAggregateCreated()));
			Version++;
		}

		public TestSaga(IDependencyResolver dependencyResolver, Guid id)
			: base(dependencyResolver, dependencyResolver.Resolve<ILogger>())
		{
			Id = id;
			ApplyChange(new SagaEvent<ISingleSignOnToken>(new TestAggregateCreated()));
			Version++;
		}

		#region Implementation of IMessageHandler<in TestAggregateDidSomething>

		public void Handle(TestAggregateDidSomething message)
		{
			// This could happen out of order in this test
			if (DidSomethingElse2)
			{
				CommandPublisher.Publish(new TestAggregateDoSomething3());
				// This is a testing variable
				Responded = true;
			}
			ApplyChange(message);
		}

		#endregion

		#region Implementation of IMessageHandler<in TestAggregateDidSomethingElse>

		public void Handle(TestAggregateDidSomethingElse message)
		{
			ApplyChange(message);
		}

		#endregion

		#region Implementation of IMessageHandler<in TestAggregateDidSomethingElse>

		public void Handle(TestAggregateDidSomethingElse2 message)
		{
			// This could happen out of order in this test
			if (DidSomething)
			{
				CommandPublisher.Publish(new TestAggregateDoSomething3());
				// This is a testing variable
				Responded = true;
			}
			ApplyChange(message);
		}

		#endregion

		public void Apply(TestAggregateDidSomething e)
		{
			DidSomethingCount++;
			DidSomething = true;
		}

		public void Apply(TestAggregateDidSomethingElse e)
		{
			DidSomethingElse = true;
		}
	}
}
