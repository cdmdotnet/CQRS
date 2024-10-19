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
	internal interface ISagaDescriptor
	{
		[DataMember]
		int Version { get; set; }

		[DataMember]
		bool UseSnapshots { get; set; }
	}

	internal interface ISagaDescriptor<TAuthenticationToken> : ISagaDescriptor
	{
		[DataMember]
		ISaga<TAuthenticationToken> Saga { get; }
	}
}