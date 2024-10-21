#region Copyright
// // -----------------------------------------------------------------------
// // <copyright company="Chinchilla Software Limited">
// // 	Copyright Chinchilla Software Limited. All rights reserved.
// // </copyright>
// // -----------------------------------------------------------------------
#endregion

using System;
using System.Runtime.Serialization;
using Cqrs.Events;

namespace Cqrs.Domain.Exceptions
{
	/// <summary>
	/// An <see cref="IEvent{TAuthenticationToken}"/> was processed out of order or an expected <see cref="IEvent{TAuthenticationToken}"/> was not found.
	/// </summary>
	[Serializable]
	public class ConcurrencyException : Exception
	{
		/// <summary>
		/// Instantiate a new instance of <see cref="ConcurrencyException"/> with the provided identifier of the <see cref="IAggregateRoot{TAuthenticationToken}"/> that had a concurrency issue.
		/// </summary>
		/// <param name="id">The identifier of the <see cref="IAggregateRoot{TAuthenticationToken}"/> that wasn't found.</param>
		/// <param name="expectedVersion">The version that was expected.</param>
		/// <param name="foundVersion">The version that was found.</param>
		public ConcurrencyException(Guid id, int? expectedVersion = null, int? foundVersion = null)
			: base(GenerateMessage(id, expectedVersion, foundVersion))
		{
			Id = id;
			ExpectedVersion = expectedVersion;
			FoundVersion = foundVersion;
		}

		static string GenerateMessage(Guid id, int? expectedVersion = null, int? foundVersion = null)
		{
			string pattern = $"A different version than expected was found in aggregate {id}";
			if (expectedVersion != null)
				pattern = string.Concat(pattern, $". Expected Version {expectedVersion}");
			if (foundVersion != null)
				pattern = string.Concat(pattern, $". Found Version {foundVersion}");
			return pattern;
		}

		/// <summary>
		/// The identifier of the <see cref="IAggregateRoot{TAuthenticationToken}"/> that had a concurrency issue.
		/// </summary>
		[DataMember]
		public Guid Id { get; set; }

		/// <summary>
		/// The version that was expected.
		/// </summary>
		[DataMember]
		public int? ExpectedVersion { get; set; }

		/// <summary>
		/// The version that was found.
		/// </summary>
		[DataMember]
		public int? FoundVersion { get; set; }
	}
}