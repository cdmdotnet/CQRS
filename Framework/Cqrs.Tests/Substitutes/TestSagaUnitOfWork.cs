using System;
using System.Collections.Generic;
using System.Linq;
using Cqrs.Authentication;
using Cqrs.Domain;

namespace Cqrs.Tests.Substitutes
{
	public class TestSagaUnitOfWork
		: ISagaUnitOfWork<ISingleSignOnToken>
		, ISagaUnitOfWork<Guid>
	{
		public static IDictionary<Guid, Tuple<ISaga<Guid>, int, int, int>> TestGuidSagasWithAddCountGetCountAndCommitCount { get; set; }

		public static IDictionary<Guid, Tuple<ISaga<ISingleSignOnToken>, int, int, int>> TestSagasWithAddCountGetCountAndCommitCount { get; set; }

		static TestSagaUnitOfWork()
		{
			TestGuidSagasWithAddCountGetCountAndCommitCount = new Dictionary<Guid, Tuple<ISaga<Guid>, int, int, int>>();
			TestSagasWithAddCountGetCountAndCommitCount = new Dictionary<Guid, Tuple<ISaga<ISingleSignOnToken>, int, int, int>>();
		}

		#region Implementation of ISagaUnitOfWork<Guid>

		/// <summary>
		/// Add an item into the <see cref="ISagaUnitOfWork{TAuthenticationToken}"/> ready to be committed.
		/// </summary>
		public void Add<TSaga>(TSaga saga, bool useSnapshots = false)
			where TSaga : ISaga<Guid>
		{
			Tuple<ISaga<Guid>, int, int, int> testSaga;
			if (!TestGuidSagasWithAddCountGetCountAndCommitCount.TryGetValue(saga.Id, out testSaga))
				testSaga = new Tuple<ISaga<Guid>, int, int, int>(saga, 1, 0, 0);
			else
				testSaga = new Tuple<ISaga<Guid>, int, int, int>(saga, testSaga.Item2 + 1, testSaga.Item3, testSaga.Item4);
			TestGuidSagasWithAddCountGetCountAndCommitCount[saga.Id] = testSaga;
		}

		/// <summary>
		/// Get an item from the <see cref="ISagaUnitOfWork{TAuthenticationToken}"/> if it has already been loaded.
		/// </summary>
		public TSaga Get<TSaga>(Guid id, int? expectedVersion = null, bool useSnapshots = false)
			where TSaga : ISaga<Guid>
		{
			Tuple<ISaga<Guid>, int, int, int> testSaga = TestGuidSagasWithAddCountGetCountAndCommitCount[id];
			testSaga = new Tuple<ISaga<Guid>, int, int, int>(testSaga.Item1, testSaga.Item2, testSaga.Item3 + 1, testSaga.Item4);
			TestGuidSagasWithAddCountGetCountAndCommitCount[id] = testSaga;

			return (TSaga)testSaga.Item1;
		}

		/// <summary>
		/// Commit any changed <see cref="Saga{TAuthenticationToken}"/> added to this <see cref="ISagaUnitOfWork{TAuthenticationToken}"/> via <see cref="ISagaUnitOfWork{TAuthenticationToken}.Add{TSaga}"/>
		/// </summary>
		public void Commit()
		{
			IList<Tuple<ISaga<Guid>, int, int, int>> trackList1 = TestGuidSagasWithAddCountGetCountAndCommitCount.Values.ToList();
			foreach (Tuple<ISaga<Guid>, int, int, int> tracked in trackList1)
			{
				var testSaga = new Tuple<ISaga<Guid>, int, int, int>(tracked.Item1, tracked.Item2, tracked.Item3, tracked.Item4 + 1);
				TestGuidSagasWithAddCountGetCountAndCommitCount[testSaga.Item1.Id] = testSaga;
			}
			IList<Tuple<ISaga<ISingleSignOnToken>, int, int, int>> trackList2 = TestSagasWithAddCountGetCountAndCommitCount.Values.ToList();
			foreach (Tuple<ISaga<ISingleSignOnToken>, int, int, int> tracked in trackList2)
			{
				var testSaga = new Tuple<ISaga<ISingleSignOnToken>, int, int, int>(tracked.Item1, tracked.Item2, tracked.Item3, tracked.Item4 + 1);
				TestSagasWithAddCountGetCountAndCommitCount[testSaga.Item1.Id] = testSaga;
			}
		}

		#endregion

		#region Implementation of ISagaUnitOfWork<ISingleSignOnToken>

		/// <summary>
		/// Add an item into the <see cref="ISagaUnitOfWork{TAuthenticationToken}"/> ready to be committed.
		/// </summary>
		void ISagaUnitOfWork<ISingleSignOnToken>.Add<TSaga>(TSaga saga, bool useSnapshots)
		{
			Tuple<ISaga<ISingleSignOnToken>, int, int, int> testSaga;
			if (!TestSagasWithAddCountGetCountAndCommitCount.TryGetValue(saga.Id, out testSaga))
				testSaga = new Tuple<ISaga<ISingleSignOnToken>, int, int, int>(saga, 1, 0, 0);
			else
				testSaga = new Tuple<ISaga<ISingleSignOnToken>, int, int, int>(saga, testSaga.Item2 + 1, testSaga.Item3, testSaga.Item4);
			TestSagasWithAddCountGetCountAndCommitCount[saga.Id] = testSaga;
		}

		/// <summary>
		/// Get an item from the <see cref="ISagaUnitOfWork{TAuthenticationToken}"/> if it has already been loaded.
		/// </summary>
		TSaga ISagaUnitOfWork<ISingleSignOnToken>.Get<TSaga>(Guid id, int? expectedVersion, bool useSnapshots)
		{
			Tuple<ISaga<ISingleSignOnToken>, int, int, int> testSaga = TestSagasWithAddCountGetCountAndCommitCount[id];
			testSaga = new Tuple<ISaga<ISingleSignOnToken>, int, int, int>(testSaga.Item1, testSaga.Item2, testSaga.Item3 + 1, testSaga.Item4);
			TestSagasWithAddCountGetCountAndCommitCount[id] = testSaga;

			return (TSaga)testSaga.Item1;
		}

		#endregion
	}
}