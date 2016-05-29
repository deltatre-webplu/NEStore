﻿using System;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Moq;
using Xunit;

namespace StreamLedger.MongoDb.UnitTests
{
	public class MongoDbBucketTests
	{
		[Fact]
		public async Task Query_empty_collections()
		{
			using (var fixture = new MongoDbLedgerFixture())
			{
				Assert.Equal(0, (await fixture.Bucket.GetBucketRevisionAsync()));
				Assert.Equal(0, (await fixture.Bucket.GetStreamRevisionAsync(Guid.NewGuid())));
				Assert.Equal(0, (await fixture.Bucket.GetStreamIdsAsync()).Count());
			}
		}

		[Fact]
		public async Task Write_an_event()
		{
			using (var fixture = new MongoDbLedgerFixture())
			{
				var streamId = Guid.NewGuid();

				var result = await fixture.Bucket.WriteAsync(streamId, 0, new[] {new {n1 = "v1"}});

				Assert.Equal(1, (await fixture.Bucket.GetBucketRevisionAsync()));
				Assert.Equal(streamId, (await fixture.Bucket.GetStreamIdsAsync()).Single());
				Assert.Equal(1, (await fixture.Bucket.GetStreamRevisionAsync(streamId)));

				var storedEvents = await fixture.Bucket.GetEventsAsync(streamId);
				Assert.Equal("v1", ((dynamic) storedEvents.Single()).n1);

				var commits = await fixture.Bucket.GetCommitsAsync(0, 1);
				Assert.Equal(1, commits.Count());

				var ids = await fixture.Bucket.GetStreamIdsAsync();
				Assert.Equal(streamId, ids.Single());

				await result.DispatchTask;

				Assert.Equal(false, await fixture.Bucket.HasUndispatchedCommitsAsync());
			}
		}

		[Fact]
		public async Task Write_multiple_events()
		{
			using (var fixture = new MongoDbLedgerFixture())
			{
				var streamId = Guid.NewGuid();

				var result = await fixture.Bucket.WriteAsync(streamId, 0, new[] {new {n1 = "v1"}, new {n1 = "v2"}, new {n1 = "v3"}});

				Assert.Equal(1, (await fixture.Bucket.GetBucketRevisionAsync()));
				Assert.Equal(streamId, (await fixture.Bucket.GetStreamIdsAsync()).Single());
				Assert.Equal(3, (await fixture.Bucket.GetStreamRevisionAsync(streamId)));

				var storedEvents = await fixture.Bucket.GetEventsAsync(streamId);
				Assert.Equal("v1", ((dynamic) storedEvents.ElementAt(0)).n1);
				Assert.Equal("v2", ((dynamic) storedEvents.ElementAt(1)).n1);
				Assert.Equal("v3", ((dynamic) storedEvents.ElementAt(2)).n1);

				var commits = await fixture.Bucket.GetCommitsAsync(0, 1);
				Assert.Equal(1, commits.Count());

				var ids = await fixture.Bucket.GetStreamIdsAsync();
				Assert.Equal(streamId, ids.Single());

				await result.DispatchTask;

				Assert.Equal(false, await fixture.Bucket.HasUndispatchedCommitsAsync());
			}
		}

		[Fact]
		public async Task Write_multiple_commits()
		{
			using (var fixture = new MongoDbLedgerFixture())
			{
				var streamId = Guid.NewGuid();

				var writeResult1 = await fixture.Bucket.WriteAsync(streamId, 0, new[] {new {n1 = "v1"}});
				var writeResult2 = await fixture.Bucket.WriteAsync(streamId, 1, new[] {new {n1 = "v2"}});
				var writeResult3 = await fixture.Bucket.WriteAsync(streamId, 2, new[] {new {n1 = "v3"}});

				Assert.Equal(3, (await fixture.Bucket.GetBucketRevisionAsync()));
				Assert.Equal(streamId, (await fixture.Bucket.GetStreamIdsAsync()).Single());
				Assert.Equal(3, (await fixture.Bucket.GetStreamRevisionAsync(streamId)));

				var storedEvents = await fixture.Bucket.GetEventsAsync(streamId);
				Assert.Equal("v1", ((dynamic) storedEvents.ElementAt(0)).n1);
				Assert.Equal("v2", ((dynamic) storedEvents.ElementAt(1)).n1);
				Assert.Equal("v3", ((dynamic) storedEvents.ElementAt(2)).n1);

				var commits = await fixture.Bucket.GetCommitsAsync(0, 3);
				Assert.Equal(3, commits.Count());

				var ids = await fixture.Bucket.GetStreamIdsAsync();
				Assert.Equal(streamId, ids.Single());

				await Task.WhenAll(writeResult1.DispatchTask, writeResult2.DispatchTask, writeResult3.DispatchTask);

				Assert.Equal(false, await fixture.Bucket.HasUndispatchedCommitsAsync());
			}
		}

		[Fact]
		public async Task Events_are_dispatched()
		{
			using (var fixture = new MongoDbLedgerFixture())
			{
				var streamId = Guid.NewGuid();

				var @event = new {n1 = "v1"};

				var result = await fixture.Bucket.WriteAsync(streamId, 0, new[] {@event});

				fixture.Dispatcher.Verify(p => p.DispatchAsync(@event), Times.Once());

				await result.DispatchTask;

				Assert.Equal(false, await fixture.Bucket.HasUndispatchedCommitsAsync());
			}
		}

		[Fact]
		public async Task If_dispatch_fail_commits_is_marked_as_undispatched()
		{
			using (var fixture = new MongoDbLedgerFixture())
			{
				var streamId = Guid.NewGuid();

				var @event = new {n1 = "v1"};

				fixture.Dispatcher.Setup(p => p.DispatchAsync(It.IsAny<object>()))
					.Throws(new MyException("Some dispatch exception"));

				var result = await fixture.Bucket.WriteAsync(streamId, 0, new[] {@event});

				fixture.Dispatcher.Verify(p => p.DispatchAsync(@event), Times.Once());

				try
				{
					await result.DispatchTask;
				}
				catch (MyException)
				{ }

				Assert.Equal(true, await fixture.Bucket.HasUndispatchedCommitsAsync());
			}
		}

		[Fact]
		public async Task Can_redispatch_undispatched_events()
		{
			using (var fixture = new MongoDbLedgerFixture())
			{
				var streamId = Guid.NewGuid();

				var @event = new {n1 = "v1"};

				// Create an undispatched event
				fixture.Dispatcher.Setup(p => p.DispatchAsync(It.IsAny<object>()))
					.Throws(new MyException("Some dispatch exception"));
				var result = await fixture.Bucket.WriteAsync(streamId, 0, new[] {@event});
				try
				{
					await result.DispatchTask;
				}
				catch (MyException )
				{ }
				Assert.Equal(true, await fixture.Bucket.HasUndispatchedCommitsAsync());

				// Redispatch events
				fixture.Dispatcher.Reset();
				await fixture.Bucket.DispatchUndispatchedAsync();
				fixture.Dispatcher.Verify(p => p.DispatchAsync(It.IsAny<object>()), Times.Once());
				Assert.Equal(false, await fixture.Bucket.HasUndispatchedCommitsAsync());
			}
		}

		[Serializable]
		private class MyException : Exception
		{
			//
			// For guidelines regarding the creation of new exception types, see
			//    http://msdn.microsoft.com/library/default.asp?url=/library/en-us/cpgenref/html/cpconerrorraisinghandlingguidelines.asp
			// and
			//    http://msdn.microsoft.com/library/default.asp?url=/library/en-us/dncscol/html/csharp07192001.asp
			//

			public MyException()
			{
			}

			public MyException(string message) : base(message)
			{
			}

			public MyException(string message, Exception inner) : base(message, inner)
			{
			}

			protected MyException(
				SerializationInfo info,
				StreamingContext context) : base(info, context)
			{
			}
		}
	}
}