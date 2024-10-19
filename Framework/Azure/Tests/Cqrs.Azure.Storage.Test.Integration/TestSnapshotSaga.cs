using Chinchilla.Logging;
using Cqrs.Azure.ServiceBus.Tests.Unit;
using Cqrs.Configuration;
using Cqrs.Events;
using Cqrs.Snapshots;
using System;

namespace Cqrs.Azure.Storage.Test.Integration
{
	public class TestSnapshotSaga : SnapshotSaga<Guid, TestSagaSnapshot>
	{
		private TestSnapshotSaga(IDependencyResolver dependencyResolver, ILogger logger)
			: base(dependencyResolver, logger)
		{
		}

		private TestSnapshotSaga(IDependencyResolver dependencyResolver, ILogger logger, Guid id)
			: base(dependencyResolver, logger)
		{
			Id = id;
		}

		public int EventCount { get; private set; }

		#region Implementation of IMessageHandler<in TestAggregateDidSomething>

		public void Handle(TestEvent message)
		{
			ApplyChange(message);
		}

		public void Apply(TestEvent e)
		{
			EventCount++;
		}

		protected override void SetId(ISagaEvent<Guid> sagaEvent)
		{
			// We set Id as the eventstore is using that and not an IEventWithIdentity
			sagaEvent.Id = Rsn;
			sagaEvent.Rsn = Rsn;
		}

		protected override TestSagaSnapshot CreateSnapshot()
		{
			return new TestSagaSnapshot { EventCount = EventCount };
		}

		protected override void RestoreFromSnapshot(TestSagaSnapshot snapshot)
		{
			EventCount = snapshot.EventCount;
		}

		#endregion
	}

	public class TestSagaSnapshot : Snapshot
	{
		public int EventCount { get; set; }
	}
}
