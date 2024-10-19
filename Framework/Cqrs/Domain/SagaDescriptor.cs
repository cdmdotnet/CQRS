#region Copyright
// // -----------------------------------------------------------------------
// // <copyright company="Chinchilla Software Limited">
// // 	Copyright Chinchilla Software Limited. All rights reserved.
// // </copyright>
// // -----------------------------------------------------------------------
#endregion

using System.Runtime.Serialization;

namespace Cqrs.Domain
{
	internal class SagaDescriptor<TSaga, TAuthenticationToken> : ISagaDescriptor<TAuthenticationToken>
		where TSaga : ISaga<TAuthenticationToken>
	{
		ISaga<TAuthenticationToken> ISagaDescriptor<TAuthenticationToken>.Saga
		{
			get { return Saga; }
		}

		[DataMember]
		public TSaga Saga { get; set; }

		[DataMember]
		public int Version { get; set; }

		[DataMember]
		public bool UseSnapshots { get; set; }
	}
}