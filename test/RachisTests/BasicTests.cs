﻿using System;
using System.Threading;
using System.Threading.Tasks;
using Raven.Client.ServerWide;
using Raven.Server.Rachis;
using Raven.Server.ServerWide.Context;
using Sparrow.Json;
using Tests.Infrastructure;
using Xunit;

namespace RachisTests
{
    public class BasicTests : RachisConsensusTestBase
    {
        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(7)]
        public async Task CanApplyCommitAcrossAllCluster(int amount)
        {
            var leader = await CreateNetworkAndGetLeader(amount);
            long lastIndex = 0;
            using (var ctx = JsonOperationContext.ShortTermSingleUse())
            {
                for (var i = 0; i < 5; i++)
                {
                    var (index, _) = await leader.PutAsync(new TestCommand { Name = "test", Value = i });
                    lastIndex = index;
                }
            }

            foreach (var r in RachisConsensuses)
            {
                var condition = await
                    r.WaitForCommitIndexChange(RachisConsensus.CommitIndexModification.GreaterOrEqual, lastIndex)
                        .WaitAsync(5000);
                TransactionOperationContext context;
                using (r.ContextPool.AllocateOperationContext(out context))
                using (context.OpenReadTransaction())
                {
                    var commitIndex = r.GetLastCommitIndex(context);
                    Assert.True(condition, $"Last commit is {commitIndex} wanted {lastIndex}");
                }
            }

        }

        [Fact]
        public async Task RavenDB_13659()
        {
            var leader = await CreateNetworkAndGetLeader(1);
            var mre = new ManualResetEvent(false);
            var tcs = new TaskCompletionSource<object>();

            leader.Timeout.Start(() =>
            {
                mre.Set();
                try
                {
                    using (leader.ContextPool.AllocateOperationContext(out TransactionOperationContext context))
                    using (context.OpenWriteTransaction())
                    {

                    }
                    tcs.SetResult(null);
                }
                catch (Exception e)
                {
                    tcs.SetException(e);
                }
            });

            using (leader.ContextPool.AllocateOperationContext(out TransactionOperationContext context))
            using (context.OpenWriteTransaction())
            {
                mre.WaitOne();
                leader.SetNewStateInTx(context, RachisState.Follower, null, leader.CurrentTerm, "deadlock");
                context.Transaction.Commit();
            }

            await tcs.Task;
        }
    }
}
