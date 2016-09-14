﻿using System;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Moq;
using Xunit;

namespace NEStore.MongoDb.Tests
{
	public class MongoDbBucketTests
	{
		[Fact]
		public async Task Query_empty_collections()
		{
			using (var fixture = new MongoDbEventStoreFixture())
			{
				Assert.Equal(0, (await fixture.Bucket.GetBucketRevisionAsync()));
				Assert.Equal(0, (await fixture.Bucket.GetStreamRevisionAsync(Guid.NewGuid())));
				Assert.Equal(0, (await fixture.Bucket.GetStreamIdsAsync()).Count());
			}
		}

		[Fact]
		public async Task Write_an_event()
		{
			using (var fixture = new MongoDbEventStoreFixture())
			{
				var streamId = Guid.NewGuid();

				var result = await fixture.Bucket.WriteAsync(streamId, 0, new[] {new {n1 = "v1"}});

				Assert.Equal(1, (await fixture.Bucket.GetBucketRevisionAsync()));
				Assert.Equal(streamId, (await fixture.Bucket.GetStreamIdsAsync()).Single());
				Assert.Equal(1, (await fixture.Bucket.GetStreamRevisionAsync(streamId)));

				var storedEvents = await fixture.Bucket.GetEventsAsync(streamId);
				Assert.Equal("v1", ((dynamic) storedEvents.Single()).n1);

				var commits = await fixture.Bucket.GetCommitsAsync(toBucketRevision: 1);
				Assert.Equal(1, commits.Count());

				var ids = await fixture.Bucket.GetStreamIdsAsync();
				Assert.Equal(streamId, ids.Single());

				await result.DispatchTask;

				Assert.Equal(false, await fixture.Bucket.HasUndispatchedCommitsAsync());
			}
		}

		[Fact]
		public async Task Cannot_write_with_revision_less_than_0()
		{
			using (var fixture = new MongoDbEventStoreFixture())
			{
				var streamId = Guid.NewGuid();
				
				await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => fixture.Bucket.WriteAsync(streamId, -1, new[] { new { n1 = "v2" } }));
			}
		}

		[Fact]
		public async Task Cannot_write_with_revision_not_sequential()
		{
			using (var fixture = new MongoDbEventStoreFixture())
			{
				var streamId = Guid.NewGuid();

				await fixture.Bucket.WriteAsync(streamId, 0, new[] {new {n1 = "v1"}});

				await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => fixture.Bucket.WriteAsync(streamId, 2, new[] { new { n1 = "v2" } }));
			}
		}

		[Fact]
		public async Task Cannot_write_with_the_same_revision_multiple_times()
		{
			using (var fixture = new MongoDbEventStoreFixture())
			{
				var streamId = Guid.NewGuid();

				var result = await fixture.Bucket.WriteAsync(streamId, 0, new[] { new { n1 = "v1" } });

				await result.DispatchTask;

				await Assert.ThrowsAsync<ConcurrencyWriteException>(() => fixture.Bucket.WriteAsync(streamId, 0, new[] { new { n1 = "v2" } }));
			}
		}

		[Fact]
		public async Task Cannot_write_with_the_same_revision_multiple_times_using_mongo_index()
		{
			using (var fixture = new MongoDbEventStoreFixture())
			{
				var streamId = Guid.NewGuid();

				fixture.Bucket.CheckStreamRevisionBeforeWriting = false;

				var result = await fixture.Bucket.WriteAsync(streamId, 0, new[] { new { n1 = "v1" } });

				await result.DispatchTask;

				await Assert.ThrowsAsync<ConcurrencyWriteException>(() => fixture.Bucket.WriteAsync(streamId, 0, new[] { new { n1 = "v2" } }));
			}
		}

		[Fact]
		public async Task Write_multiple_events()
		{
			using (var fixture = new MongoDbEventStoreFixture())
			{
				var streamId = Guid.NewGuid();

				var result = await fixture.Bucket.WriteAsync(streamId, 0, new[] {new {n1 = "v1"}, new {n1 = "v2"}, new {n1 = "v3"}});

				Assert.Equal(1, (await fixture.Bucket.GetBucketRevisionAsync()));
				Assert.Equal(streamId, (await fixture.Bucket.GetStreamIdsAsync()).Single());
				Assert.Equal(3, (await fixture.Bucket.GetStreamRevisionAsync(streamId)));

				var storedEvents = (await fixture.Bucket.GetEventsAsync(streamId)).ToList();
				Assert.Equal("v1", ((dynamic) storedEvents.ElementAt(0)).n1);
				Assert.Equal("v2", ((dynamic) storedEvents.ElementAt(1)).n1);
				Assert.Equal("v3", ((dynamic) storedEvents.ElementAt(2)).n1);

				var commits = await fixture.Bucket.GetCommitsAsync(toBucketRevision: 1);
				Assert.Equal(1, commits.Count());

				var ids = await fixture.Bucket.GetStreamIdsAsync();
				Assert.Equal(streamId, ids.Single());

				await result.DispatchTask;

				Assert.Equal(false, await fixture.Bucket.HasUndispatchedCommitsAsync());
			}
		}

		[Fact]
		public async Task Write_multiple_commits_with_multiple_events()
		{
			using (var fixture = new MongoDbEventStoreFixture())
			{
				var streamId = Guid.NewGuid();

				var result1 = await fixture.Bucket.WriteAsync(streamId, 0, new[] { new { n1 = "v1" }, new { n1 = "v2" }, new { n1 = "v3" } });
				var result2 = await fixture.Bucket.WriteAsync(streamId, 3, new[] { new { n1 = "v4" } });
				var result3 = await fixture.Bucket.WriteAsync(streamId, 4, new[] { new { n1 = "v5" }, new { n1 = "v6" }, new { n1 = "v7" } });

				Assert.Equal(3, (await fixture.Bucket.GetBucketRevisionAsync()));
				Assert.Equal(streamId, (await fixture.Bucket.GetStreamIdsAsync()).Single());
				Assert.Equal(7, (await fixture.Bucket.GetStreamRevisionAsync(streamId)));

				var storedEvents = (await fixture.Bucket.GetEventsAsync(streamId)).ToList();
				Assert.Equal("v1", ((dynamic)storedEvents.ElementAt(0)).n1);
				Assert.Equal("v2", ((dynamic)storedEvents.ElementAt(1)).n1);
				Assert.Equal("v3", ((dynamic)storedEvents.ElementAt(2)).n1);
				Assert.Equal("v4", ((dynamic)storedEvents.ElementAt(3)).n1);
				Assert.Equal("v5", ((dynamic)storedEvents.ElementAt(4)).n1);
				Assert.Equal("v6", ((dynamic)storedEvents.ElementAt(5)).n1);
				Assert.Equal("v7", ((dynamic)storedEvents.ElementAt(6)).n1);

				var commits = await fixture.Bucket.GetCommitsAsync(toBucketRevision: 1);
				Assert.Equal(1, commits.Count());

				var ids = await fixture.Bucket.GetStreamIdsAsync();
				Assert.Equal(streamId, ids.Single());

				await result1.DispatchTask;
				await result2.DispatchTask;
				await result3.DispatchTask;

				Assert.Equal(false, await fixture.Bucket.HasUndispatchedCommitsAsync());
			}
		}

		[Fact]
		public async Task Write_multiple_commits()
		{
			using (var fixture = new MongoDbEventStoreFixture())
			{
				var streamId = Guid.NewGuid();

				var writeResult1 = await fixture.Bucket.WriteAsync(streamId, 0, new[] {new {n1 = "v1"}});
				var writeResult2 = await fixture.Bucket.WriteAsync(streamId, 1, new[] {new {n1 = "v2"}});
				var writeResult3 = await fixture.Bucket.WriteAsync(streamId, 2, new[] {new {n1 = "v3"}});

				Assert.Equal(3, (await fixture.Bucket.GetBucketRevisionAsync()));
				Assert.Equal(streamId, (await fixture.Bucket.GetStreamIdsAsync()).Single());
				Assert.Equal(3, (await fixture.Bucket.GetStreamRevisionAsync(streamId)));

				var storedEvents = (await fixture.Bucket.GetEventsAsync(streamId)).ToList();
				Assert.Equal("v1", ((dynamic) storedEvents.ElementAt(0)).n1);
				Assert.Equal("v2", ((dynamic) storedEvents.ElementAt(1)).n1);
				Assert.Equal("v3", ((dynamic) storedEvents.ElementAt(2)).n1);

				var commits = await fixture.Bucket.GetCommitsAsync(toBucketRevision: 3);
				Assert.Equal(3, commits.Count());

				var ids = await fixture.Bucket.GetStreamIdsAsync();
				Assert.Equal(streamId, ids.Single());

				await Task.WhenAll(writeResult1.DispatchTask, writeResult2.DispatchTask, writeResult3.DispatchTask);

				Assert.Equal(false, await fixture.Bucket.HasUndispatchedCommitsAsync());
			}
		}

		[Fact]
		public async Task Write_multiple_streams()
		{
			using (var fixture = new MongoDbEventStoreFixture())
			{
				var streamId1 = Guid.NewGuid();
				var streamId2 = Guid.NewGuid();

				await fixture.Bucket.WriteAsync(streamId1, 0, new[] { new { n1 = "v1" } });
				await fixture.Bucket.WriteAsync(streamId2, 0, new[] { new { n1 = "v1" } });
				await fixture.Bucket.WriteAsync(streamId1, 1, new[] { new { n1 = "v2" } });

				Assert.Equal(3, (await fixture.Bucket.GetBucketRevisionAsync()));
				var streams = (await fixture.Bucket.GetStreamIdsAsync())
					.ToList();
				Assert.Equal(2, streams.Count);
				Assert.True(streams.Contains(streamId1));
				Assert.True(streams.Contains(streamId2));

				Assert.Equal(2, (await fixture.Bucket.GetStreamRevisionAsync(streamId1)));
				Assert.Equal(1, (await fixture.Bucket.GetStreamRevisionAsync(streamId2)));

				var storedEvents = (await fixture.Bucket.GetEventsAsync(streamId1)).ToList();
				Assert.Equal(2, storedEvents.Count);
				Assert.Equal("v1", ((dynamic)storedEvents.ElementAt(0)).n1);
				Assert.Equal("v2", ((dynamic)storedEvents.ElementAt(1)).n1);

				storedEvents = (await fixture.Bucket.GetEventsAsync(streamId2)).ToList();
				Assert.Equal(1, storedEvents.Count);
				Assert.Equal("v1", ((dynamic)storedEvents.ElementAt(0)).n1);
			}
		}

		[Fact]
		public async Task Rollback()
		{
			using (var fixture = new MongoDbEventStoreFixture())
			{
				var streamId = Guid.NewGuid();

				await fixture.Bucket.WriteAsync(streamId, 0, new[] { new { n1 = "v1" } });
				await fixture.Bucket.WriteAsync(streamId, 1, new[] { new { n1 = "v2" } });

				Assert.Equal(2, (await fixture.Bucket.GetBucketRevisionAsync()));

				await fixture.Bucket.RollbackAsync(1);

				Assert.Equal(1, (await fixture.Bucket.GetBucketRevisionAsync()));

				var storedEvents = (await fixture.Bucket.GetEventsAsync(streamId)).ToList();
				Assert.Equal(1, storedEvents.Count);
				Assert.Equal("v1", ((dynamic)storedEvents.ElementAt(0)).n1);
			}
		}

		[Fact]
		public async Task When_rollback_then_next_commit_should_the_right_bucket_revistion()
		{
			using (var fixture = new MongoDbEventStoreFixture())
			{
				var streamId = Guid.NewGuid();

				await fixture.Bucket.WriteAsync(streamId, 0, new[] { new { n1 = "v1" } });
				await fixture.Bucket.WriteAsync(streamId, 1, new[] { new { n1 = "v2" } });

				await fixture.Bucket.RollbackAsync(1);

				await fixture.Bucket.WriteAsync(streamId, 1, new[] { new { n1 = "v3" } });

				var commits = (await fixture.Bucket.GetCommitsAsync(streamId)).ToList();
				Assert.Equal(2, commits.Count);
				Assert.Equal(1, commits.ElementAt(0).BucketRevision);
				Assert.Equal(2, commits.ElementAt(1).BucketRevision);

				Assert.Equal("v1", ((dynamic)commits.ElementAt(0).Events.First()).n1);
				Assert.Equal("v3", ((dynamic)commits.ElementAt(1).Events.First()).n1);
			}
		}

		[Fact]
		public async Task Events_are_dispatched()
		{
			using (var fixture = new MongoDbEventStoreFixture())
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
			using (var fixture = new MongoDbEventStoreFixture())
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
			using (var fixture = new MongoDbEventStoreFixture())
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

		[Fact]
		public async Task Cannot_write_new_event_if_there_are_undispatched_events()
		{
			using (var fixture = new MongoDbEventStoreFixture())
			{
				var streamId = Guid.NewGuid();

				var @event = new { n1 = "v1" };

				// Create an undispatched event
				fixture.Dispatcher.Setup(p => p.DispatchAsync(It.IsAny<object>()))
					.Throws(new MyException("Some dispatch exception"));
				var result = await fixture.Bucket.WriteAsync(streamId, 0, new[] { @event });
				try
				{
					await result.DispatchTask;
				}
				catch (MyException)
				{ }
				Assert.Equal(true, await fixture.Bucket.HasUndispatchedCommitsAsync());


				await Assert.ThrowsAsync<UndispatchedEventsFoundException>(() => fixture.Bucket.WriteAsync(streamId, 1, new[] { @event }));
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

			public MyException(string message) : base(message)
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