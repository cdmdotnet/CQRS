#region Copyright
// // -----------------------------------------------------------------------
// // <copyright company="Chinchilla Software Limited">
// // 	Copyright Chinchilla Software Limited. All rights reserved.
// // </copyright>
// // -----------------------------------------------------------------------
#endregion

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Chinchilla.Logging;
using Cqrs.Entities;

namespace Cqrs.Azure.Storage
{
	/// <summary>
	/// A <see cref="IEnumerable{TData}"/> that uses Azure Blobl Storage for storage.
	/// </summary>
	public class BlobStorageStore<TData>
		: StorageStore<TData, BlobContainerClient, BlobServiceClient>
	{
		internal Func<TData, string> GenerateFileName { get; set; }

		/// <summary>
		/// Initializes a new instance of the <see cref="BlobStorageStore{TData}"/> class using the specified container.
		/// </summary>
		public BlobStorageStore(ILogger logger)
			: base(logger)
		{
		}

		#region Overrides of StorageStore<TData,TableClient>

		/// <summary>
		/// Create a new <see cref="BlobServiceClient"/>
		/// </summary>
		protected override BlobServiceClient CreateClient(string connectionString)
		{
			var blobClientOptions = new BlobClientOptions();
			blobClientOptions.Retry.Mode = RetryMode.Exponential;
			blobClientOptions.Retry.Delay = TimeSpan.FromSeconds(10);
			blobClientOptions.Retry.MaxRetries = 6;
			try
			{
				return new BlobServiceClient(connectionString, blobClientOptions);
			}
			catch (FileLoadException ex)
			{
				if (ex.FileName.StartsWith("System.Diagnostics.DiagnosticSource"))
				{
					throw new InvalidOperationException($"Add Application Insights for Blob Storage.", ex);
				}
				throw ex;
			}
		}

		#endregion

		#region Implementation of IEnumerable

		/// <summary>
		/// Returns an enumerator that iterates through the collection.
		/// </summary>
		/// <returns>
		/// A <see cref="T:System.Collections.Generic.IEnumerator`1"/> that can be used to iterate through the collection.
		/// </returns>
		public override IEnumerator<TData> GetEnumerator()
		{
			IEnumerator<TData> results = null;
			Task.Run
			(
#if NET472
#else
				async
#endif
					() =>
				{
					results = 
#if NET472
						OpenStreamsForReading()
#else
						(await OpenStreamsForReadingAsync())
#endif
							.Select(Deserialise)
							.GetEnumerator();
				}
			).Wait();
			return results;
		}

#endregion

		#region Implementation of IQueryable

		/// <summary>
		/// Gets the expression tree that is associated with the instance of <see cref="T:System.Linq.IQueryable"/>.
		/// </summary>
		/// <returns>
		/// The <see cref="T:System.Linq.Expressions.Expression"/> that is associated with this instance of <see cref="T:System.Linq.IQueryable"/>.
		/// </returns>
		public override Expression Expression
		{
			get
			{
				Expression result = null;
				Task.Run
				(
#if NET472
#else
					async
#endif
						() =>
					{
						result =
#if NET472
						OpenStreamsForReading()
#else
						(await OpenStreamsForReadingAsync())
#endif
							.Select(Deserialise)
							.AsQueryable()
							.Expression;
					}
				).Wait();
				return result;
			}
		}

		/// <summary>
		/// Gets the type of the element(s) that are returned when the expression tree associated with this instance of <see cref="T:System.Linq.IQueryable"/> is executed.
		/// </summary>
		/// <returns>
		/// A <see cref="T:System.Type"/> that represents the type of the element(s) that are returned when the expression tree associated with this object is executed.
		/// </returns>
		public override Type ElementType
		{
			get
			{
				Type result = null;
				Task.Run
				(
#if NET472
#else
					async
#endif
						() =>
					{
						result =
#if NET472
						OpenStreamsForReading()
#else
						(await OpenStreamsForReadingAsync())
#endif
							.Select(Deserialise)
							.AsQueryable()
							.ElementType;
					}
				).Wait();
				return result;
			}
		}

		/// <summary>
		/// Gets the query provider that is associated with this data source.
		/// </summary>
		/// <returns>
		/// The <see cref="T:System.Linq.IQueryProvider"/> that is associated with this data source.
		/// </returns>
		public override IQueryProvider Provider
		{
			get
			{
				IQueryProvider result = null;
				Task.Run
				(
#if NET472
#else
					async
#endif
						() =>
					{
						result =
#if NET472
						OpenStreamsForReading()
#else
						(await OpenStreamsForReadingAsync())
#endif
							.Select(Deserialise)
							.AsQueryable()
							.Provider;
					}
				).Wait();
				return result;
			}
		}

		#endregion

		/// <summary>
		/// Save the provided <paramref name="data"/> asynchronously.
		/// </summary>
		protected virtual
#if NET472
			void SaveData
#else
			async Task SaveDataAsync
#endif
				<TResult>(TData data, Func<TData, BlobClient, TResult> function, Func<TData, string> customFilenameFunction = null)
		{
			IList<Task> persistTasks = new List<Task>();
			foreach ((BlobServiceClient Client, BlobContainerClient Container) tuple in WritableCollection)
			{
				TData taskData = data;
				BlobContainerClient container = tuple.Container;
				Task task = SafeTask.RunSafely
				(
					() =>
					{
						string fileName = string.Format("{0}.json", (customFilenameFunction ?? GenerateFileName)(taskData));
						BlobClient cloudBlockBlob = GetBlobReference(container, fileName);
						object result = function(taskData, cloudBlockBlob);
						Task t = result as Task;
						if (t != null)
							t.Wait();
						else if (typeof(TResult) == typeof(Uri))
						{
							Uri uri = (Uri)(object)result;

							Logger.LogDebug($"The data entity '{fileName}' was persisted at uri '{uri}'");
						}
					}
				);
				persistTasks.Add(task);
			}

			bool anyFailed;
#if NET472
			anyFailed = Task.Factory.ContinueWhenAll(persistTasks.ToArray(), tasks =>
			{
				return tasks.Any(task => task.IsFaulted);
			}).Result;
#else
			Task t = Task.WhenAll(persistTasks);
			try
			{
				await t;
			}
			catch { }

			anyFailed = t.Status == TaskStatus.Faulted;
#endif
			if (anyFailed)
				throw new AggregateException("Persisting data to blob storage failed. Check the logs for more details.");
		}

		#region Implementation of IDataStore<TData>

		/// <summary>
		/// Add the provided <paramref name="data"/> to the data store and persist the change.
		/// </summary>
		public override
#if NET472
			void Add
#else
			async Task AddAsync
#endif
				(TData data)
		{
#if NET472
			SaveData
#else
			await SaveDataAsync
#endif
			(
				data,
#if NET472
#else
				async
#endif
				(taskData, cloudBlockBlob) =>
				{
					try
					{
						Response<BlobContentInfo> result =
#if NET472
							cloudBlockBlob.Upload
#else
							await cloudBlockBlob.UploadAsync
#endif
								(Serialise(taskData), new BlobHttpHeaders { ContentType = "application/json" });

						return cloudBlockBlob.Uri;
					}
					catch (Exception exception)
					{
						Logger.LogError("There was an issue persisting data to blob storage.", exception: exception);
						throw;
					}
				}
			);
		}

		/// <summary>
		/// Remove the provided <paramref name="data"/> (normally by <see cref="IEntity.Rsn"/>) from the data store and persist the change.
		/// </summary>
		public override
#if NET472
			void Destroy
#else
			async Task DestroyAsync
#endif
				(TData data)
		{
#if NET472
			SaveData
#else
			await SaveDataAsync
#endif
			(
				data,
#if NET472
#else
				async
#endif
					(taskData, cloudBlockBlob) =>
				{
					try
					{
						return
#if NET472
						cloudBlockBlob.DeleteIfExists
#else
						await cloudBlockBlob.DeleteIfExistsAsync
#endif
							(DeleteSnapshotsOption.IncludeSnapshots);
					}
					catch (Exception exception)
					{
						Logger.LogError("There was an issue deleting data from blob storage.", exception: exception);
						throw;
					}
				}
			);
		}

		/// <summary>
		/// Remove all contents (normally by use of a truncate operation) from the data store and persist the change.
		/// </summary>
		public override
#if NET472
			void RemoveAll
#else
			async Task RemoveAllAsync
#endif
				()
		{
			foreach ((BlobServiceClient Client, BlobContainerClient Container) tuple in WritableCollection)
#if NET472
				tuple.Container.DeleteIfExists();
#else
				await tuple.Container.DeleteIfExistsAsync();
#endif
		}

		/// <summary>
		/// Update the provided <paramref name="data"/> in the data store and persist the change.
		/// </summary>
		public override
#if NET472
			void Update
#else
			async Task UpdateAsync
#endif
				(TData data)
		{
#if NET472
			Add
#else
			await AddAsync
#endif
				(data);
		}

		#endregion

		/// <summary>
		/// Creates a <see cref="BlobContainerClient"/> with the specified name <paramref name="containerName"/> if it doesn't already exist.
		/// </summary>
		/// <param name="storageAccount">The storage account to create the <see cref="BlobContainerClient"/> is</param>
		/// <param name="containerName">The name of the <see cref="BlobContainerClient"/>.</param>
		/// <param name="isPublic">Whether or not this <see cref="BlobContainerClient"/> is publicly accessible.</param>
		protected override
#if NET472
			BlobContainerClient CreateSource
#else
			async Task<BlobContainerClient> CreateSourceAsync
#endif
				(BlobServiceClient storageAccount, string containerName, bool isPublic = true)
		{
			BlobContainerClient containerClient = storageAccount.GetBlobContainerClient(containerName);
#if NET472
			containerClient.CreateIfNotExists
#else
			await containerClient.CreateIfNotExistsAsync
#endif
				(PublicAccessType.None);

			return containerClient;
		}

		/// <summary>
		/// Opens stream for reading from a block blob.
		/// </summary>
		protected virtual
#if NET472
			IEnumerable<Stream> OpenStreamsForReading
#else
			async Task<IEnumerable<Stream>> OpenStreamsForReadingAsync
#endif
				(Func<BlobItem, bool> predicate = null, string blobPrefix = null, string folderName = null)
		{
			ConcurrentQueue<Stream> results = null;
			for(int i = 0; i < 100; i++)
			{
				AsyncPageable<BlobItem> blobs;
				if (!string.IsNullOrWhiteSpace(folderName))
					blobs = ReadableSource.GetBlobsAsync(prefix: folderName);
				else
					blobs = ReadableSource.GetBlobsAsync(prefix: blobPrefix);
				var query = new Dictionary<string, BlobItem>();
#if NET472
				Task.Run(async () =>
				{
#endif
					await foreach (BlobItem blob in blobs)
						query.Add(blob.Name, blob);
#if NET472
				}).Wait();
#endif

				IEnumerable<BlobItem> sourceQuery;
				if (predicate != null)
					sourceQuery = query.Values.Where(predicate);
				else
					sourceQuery = query.Values;
				IList<BlobItem> source = sourceQuery.ToList();

				results = new ConcurrentQueue<Stream>();
				var downloadTasks = new ConcurrentQueue<Task>();
				foreach (BlobItem x in source)
				{
#if NET472
					downloadTasks.Enqueue
					(
						Task.Run(async () =>
						{
#endif
							BlobClient blobClient = ReadableSource.GetBlobClient(x.Name);
								BlobDownloadResult downloadResult = await blobClient.DownloadContentAsync();
								BinaryData as1 = downloadResult.Content;
								results.Enqueue(as1.ToStream());
#if NET472
						})
					);
#endif
				}

#if NET472
				bool hasFinished = false;
				Task.Run(async () =>
				{
					await Task.WhenAll(downloadTasks).ContinueWith(state => { hasFinished = !state.IsFaulted; });
				}
				).Wait();

				if (!hasFinished)
				{
					Logger.LogError("Loading streams faulted.");
					// Now we just go round again
					// throw new Exception("Did not read all blobs.");
				}
				else
#endif
				{
					// We discovered that sometimes getting blobs can return null streams... not helpful. Seems to be a race condition
					// Turns out this was probably due to using a non threadsafe collection... so the below might not be needed anymore
					if (results.Count == source.Count && !results.Any(x => x == null))
						break;
				}

				Thread.Sleep(250);
				results = null;
			}
			if (results == null)
				throw new InvalidOperationException("Opening streams returned null objects when none should be returned.");
			return results;
		}

		/// <summary>
		/// Gets a reference to a block blob in the container.
		/// </summary>
		/// <param name="container">The container to get the reference from</param>
		/// <param name="blobName">The name of the blob.</param>
		/// <returns>A reference to a block blob.</returns>
		protected virtual BlobClient GetBlobReference(BlobContainerClient container, string blobName)
		{
			return container.GetBlobClient(blobName);
		}

		/// <summary>
		/// Get <typeparamref name="TData"/> by its name.
		/// </summary>
		public virtual
#if NET472
			TData GetByName
#else
			async Task<TData> GetByNameAsync
#endif
				(string name)
		{
			return
			(
#if NET472
				OpenStreamsForReading
#else
				await OpenStreamsForReadingAsync
#endif
					(blobPrefix: name.Replace("\\", "/"))
			)
			.Select(Deserialise)
			.SingleOrDefault();
		}

		/// <summary>
		/// Get all <typeparamref name="TData"/> items in the folder.
		/// </summary>
		public virtual
#if NET472
			IEnumerable<TData> GetByFolder
#else
			async Task<IEnumerable<TData>> GetByFolderAsync
#endif
				(string folderName, Func<BlobItem, bool> predicate = null)
		{
			string folder = new Uri(string.Format(folderName.StartsWith("..\\") ? "http://l/2/{0}" : "http://l/{0}", folderName)).AbsolutePath.Substring(1);
			return
			(
#if NET472
				OpenStreamsForReading
#else
				await OpenStreamsForReadingAsync
#endif
					(folderName: folder, predicate: predicate)
			)
			.Select(Deserialise);
		}
	}
}