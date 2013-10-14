﻿using System;
using System.Collections.Generic;
using System.Linq;
using HangFire.States;
using ServiceStack.Redis;

namespace HangFire.Web
{
    internal static class JobStorage
    {
        private static readonly IRedisClient Redis = RedisFactory.Create();

        public static long ScheduledCount()
        {
            lock (Redis)
            {
                return Redis.GetSortedSetCount("hangfire:schedule");
            }
        }

        public static long EnqueuedCount(string queue)
        {
            lock (Redis)
            {
                return Redis.GetListCount(String.Format("hangfire:queue:{0}", queue));
            }
        }

        public static long FailedCount()
        {
            lock (Redis)
            {
                return Redis.GetSortedSetCount("hangfire:failed");
            }
        }

        public static long ProcessingCount()
        {
            lock (Redis)
            {
                return Redis.GetSortedSetCount("hangfire:processing");
            }
        }

        public static IList<KeyValuePair<string, ProcessingJobDto>> ProcessingJobs(
            int from, int count)
        {
            lock (Redis)
            {
                var jobIds = Redis.GetRangeFromSortedSet(
                    "hangfire:processing",
                    from,
                    from + count - 1);

                return GetJobsWithProperties(Redis,
                    jobIds,
                    new[] { "Type", "Args" },
                    new[] { "StartedAt", "ServerName" },
                    (job, state) => new ProcessingJobDto
                    {
                        ServerName = state[1],
                        Args = JobHelper.FromJson<Dictionary<string, string>>(job[1]),
                        Type = job[0],
                        Queue = JobHelper.TryToGetQueue(job[0]),
                        StartedAt = JobHelper.FromStringTimestamp(state[0])
                    }).OrderBy(x => x.Value.StartedAt).ToList();
            }
        }

        public static IDictionary<string, ScheduleDto> ScheduledJobs(int from, int count)
        {
            lock (Redis)
            {
                var scheduledJobs = Redis.GetRangeWithScoresFromSortedSet(
                    "hangfire:schedule",
                    from,
                    from + count - 1);

                var result = new Dictionary<string, ScheduleDto>();

                foreach (var scheduledJob in scheduledJobs)
                {
                    var job = Redis.GetValuesFromHash(
                        String.Format("hangfire:job:{0}", scheduledJob.Key),
                        new[] { "Type", "Args" });

                    var dto = job.TrueForAll(x => x == null)
                        ? null
                        : new ScheduleDto
                        {
                            ScheduledAt = JobHelper.FromTimestamp((long)scheduledJob.Value),
                            Args = JobHelper.FromJson<Dictionary<string, string>>(job[1]),
                            Queue = JobHelper.TryToGetQueue(job[0]),
                            Type = job[0]
                        };

                    result.Add(scheduledJob.Key, dto);
                }

                return result;
            }
        }

        public static IDictionary<DateTime, long> SucceededByDatesCount()
        {
            lock (Redis)
            {
                return GetTimelineStats(Redis, "succeeded");
            }
        }

        public static IDictionary<DateTime, long> FailedByDatesCount()
        {
            lock (Redis)
            {
                return GetTimelineStats(Redis, "failed");
            }
        }

        public static IList<ServerDto> Servers()
        {
            lock (Redis)
            {
                var serverNames = Redis.GetAllItemsFromSet("hangfire:servers");
                var result = new List<ServerDto>(serverNames.Count);
                foreach (var serverName in serverNames)
                {
                    var server = Redis.GetAllEntriesFromHash(
                        String.Format("hangfire:server:{0}", serverName));

                    var queues = Redis.GetAllItemsFromSet(
                        String.Format("hangfire:server:{0}:queues", serverName));

                    result.Add(new ServerDto
                        {
                            Name = serverName,
                            WorkersCount = int.Parse(server["Workers"]),
                            Queues = queues,
                            StartedAt = JobHelper.FromStringTimestamp(server["StartedAt"])
                        });
                }

                return result;
            }
        }

        public static IList<KeyValuePair<string, FailedJobDto>> FailedJobs(int from, int count)
        {
            lock (Redis)
            {
                var failedJobIds = Redis.GetRangeFromSortedSetDesc(
                    "hangfire:failed",
                    from,
                    from + count - 1);

                return GetJobsWithProperties(
                    Redis,
                    failedJobIds,
                    new[] { "Type", "Args" },
                    new[] { "FailedAt", "ExceptionType", "ExceptionMessage", "ExceptionDetails" },
                    (job, state) => new FailedJobDto
                    {
                        Type = job[0],
                        Queue = JobHelper.TryToGetQueue(job[0]),
                        Args = JobHelper.FromJson<Dictionary<string, string>>(job[1]),
                        FailedAt = JobHelper.FromStringTimestamp(state[0]),
                        ExceptionType = state[1],
                        ExceptionMessage = state[2],
                        ExceptionDetails = state[3],
                    });
            }
        }

        public static IList<KeyValuePair<string, SucceededJobDto>> SucceededJobs(int from, int count)
        {
            lock (Redis)
            {
                var succeededJobIds = Redis.GetRangeFromList(
                    "hangfire:succeeded",
                    from, 
                    from + count - 1);

                return GetJobsWithProperties(
                    Redis,
                    succeededJobIds,
                    new[] { "Type", "Args" },
                    new[] { "SucceededAt" },
                    (job, state) => new SucceededJobDto
                    {
                        Type = job[0],
                        Queue = JobHelper.TryToGetQueue(job[0]),
                        Args = JobHelper.FromJson<Dictionary<string, string>>(job[1]),
                        SucceededAt = JobHelper.FromStringTimestamp(state[0]),
                    });
            }
        }

        public static IList<QueueWithTopEnqueuedJobsDto> Queues()
        {
            lock (Redis)
            {
                var queues = Redis.GetAllItemsFromSet("hangfire:queues");
                var result = new List<QueueWithTopEnqueuedJobsDto>(queues.Count);

                foreach (var queue in queues)
                {
                    var firstJobIds = Redis.GetRangeFromList(
                        String.Format("hangfire:queue:{0}", queue), -5, -1);

                    var jobs = GetJobsWithProperties(
                        Redis,
                        firstJobIds,
                        new[] { "Type", "Args" },
                        new[] { "EnqueuedAt" },
                        (job, state) => new EnqueuedJobDto
                        {
                            Type = job[0],
                            Args = JobHelper.FromJson<Dictionary<string, string>>(job[1]),
                            EnqueuedAt = JobHelper.FromStringTimestamp(state[0]),
                        });

                    var length = Redis.GetListCount(String.Format("hangfire:queue:{0}", queue));
                    var dequeued = Redis.GetListCount(String.Format("hangfire:queue:{0}:dequeued", queue));

                    result.Add(new QueueWithTopEnqueuedJobsDto
                    {
                        Name = queue,
                        FirstJobs = jobs,
                        Length = length,
                        Dequeued = dequeued
                    });
                }

                return result;
            }
        }

        public static IList<KeyValuePair<string, EnqueuedJobDto>> EnqueuedJobs(
            string queue, int from, int perPage)
        {
            var firstJobIds = Redis.GetRangeFromList(
                String.Format("hangfire:queue:{0}", queue), 
                from, 
                from + perPage - 1);

            return GetJobsWithProperties(
                Redis,
                firstJobIds,
                new[] { "Type", "Args" },
                new[] { "EnqueuedAt" },
                (job, state) => new EnqueuedJobDto
                {
                    Type = job[0],
                    Args = JobHelper.FromJson<Dictionary<string, string>>(job[1]),
                    EnqueuedAt = JobHelper.FromStringTimestamp(state[0]),
                });
        }

        public static IDictionary<DateTime, long> HourlySucceededJobs()
        {
            lock (Redis)
            {
                return GetHourlyTimelineStats(Redis, "succeeded");
            }
        }

        public static IDictionary<DateTime, long> HourlyFailedJobs()
        {
            lock (Redis)
            {
                return GetHourlyTimelineStats(Redis, "failed");
            }
        }

        public static bool RetryJob(string jobId)
        {
            lock (Redis)
            {
                var jobType = Redis.GetValueFromHash(String.Format("hangfire:job:{0}", jobId), "Type");

                var queue = JobHelper.TryToGetQueue(jobType);
                if (String.IsNullOrEmpty(queue))
                {
                    return false;
                }

                // TODO: clear retry attempts counter.

                return JobState.Apply(
                    Redis,
                    new EnqueuedState(jobId, "The job has been retried by a user.", queue),
                    FailedState.Name);
            }
        }

        public static bool EnqueueScheduled(string jobId)
        {
            lock (Redis)
            {
                var jobType = Redis.GetValueFromHash(String.Format("hangfire:job:{0}", jobId), "Type");
                var queue = JobHelper.TryToGetQueue(jobType);

                if (String.IsNullOrEmpty(queue))
                {
                    return false;
                }

                return JobState.Apply(
                    Redis, 
                    new EnqueuedState(jobId, "The job has been enqueued by a user.", queue),
                    ScheduledState.Name);
            }
        }

        public static JobDetailsDto JobDetails(string jobId)
        {
            lock (Redis)
            {
                var job = Redis.GetAllEntriesFromHash(String.Format("hangfire:job:{0}", jobId));
                if (job.Count == 0) return null;

                var hiddenProperties = new[] { "Type", "Args", "State" };

                var historyList = Redis.GetAllItemsFromList(
                    String.Format("hangfire:job:{0}:history", jobId));

                var history = historyList
                    .Select(JobHelper.FromJson<Dictionary<string, string>>)
                    .ToList();

                return new JobDetailsDto
                {
                    Type = job["Type"],
                    Arguments = JobHelper.FromJson<Dictionary<string, string>>(job["Args"]),
                    State = job.ContainsKey("State") ? job["State"] : null,
                    Properties = job.Where(x => !hiddenProperties.Contains(x.Key)).ToDictionary(x => x.Key, x => x.Value),
                    History = history
                };
            }
        }

        private static Dictionary<DateTime, long> GetHourlyTimelineStats(
            IRedisClient redis, string type)
        {
            var endDate = DateTime.UtcNow;
            var dates = new List<DateTime>();
            for (var i = 0; i < 24; i++)
            {
                dates.Add(endDate);
                endDate = endDate.AddHours(-1);
            }

            var keys = dates.Select(x => String.Format("hangfire:stats:{0}:{1}", type, x.ToString("yyyy-MM-dd-HH"))).ToList();
            var valuesMap = redis.GetValuesMap(keys);

            var result = new Dictionary<DateTime, long>();
            for (var i = 0; i < dates.Count; i++)
            {
                long value;
                if (!long.TryParse(valuesMap[valuesMap.Keys.ElementAt(i)], out value))
                {
                    value = 0;
                }

                result.Add(dates[i], value);
            }

            return result;
        }

        private static Dictionary<DateTime, long> GetTimelineStats(
            IRedisClient redis, string type)
        {
            var endDate = DateTime.UtcNow.Date;
            var startDate = endDate.AddDays(-7);
            var dates = new List<DateTime>();

            while (startDate <= endDate)
            {
                dates.Add(endDate);
                endDate = endDate.AddDays(-1);
            }

            var stringDates = dates.Select(x => x.ToString("yyyy-MM-dd")).ToList();
            var keys = stringDates.Select(x => String.Format("hangfire:stats:{0}:{1}", type, x)).ToList();

            var valuesMap = redis.GetValuesMap(keys);

            var result = new Dictionary<DateTime, long>();
            for (var i = 0; i < stringDates.Count; i++)
            {
                long value;
                if (!long.TryParse(valuesMap[valuesMap.Keys.ElementAt(i)], out value))
                {
                    value = 0;
                }
                result.Add(dates[i], value);
            }

            return result;
        }

        private static IList<KeyValuePair<string, T>> GetJobsWithProperties<T>(
            IRedisClient redis,
            IEnumerable<string> jobIds,
            string[] properties,
            string[] stateProperties,
            Func<List<string>, List<string>, T> selector)
        {
            return jobIds
                .Select(x => new
                {
                    JobId = x,
                    Job = redis.GetValuesFromHash(String.Format("hangfire:job:{0}", x), properties),
                    State = redis.GetValuesFromHash(String.Format("hangfire:job:{0}:state", x), stateProperties)
                })
                .Select(x => new KeyValuePair<string, T>(
                    x.JobId,
                    x.Job.TrueForAll(y => y == null) ? default(T) : selector(x.Job, x.State)))
                .ToList();
        }

        public static long SucceededListCount()
        {
            lock (Redis)
            {
                return Redis.GetListCount("hangfire:succeeded");
            }
        }

        public static StatisticsDto GetStatistics()
        {
            lock (Redis)
            {
                var stats = new StatisticsDto();

                var queues = Redis.GetAllItemsFromSet("hangfire:queues");

                using (var pipeline = Redis.CreatePipeline())
                {
                    pipeline.QueueCommand(
                        x => x.GetSetCount("hangfire:servers"),
                        x => stats.Servers = x);

                    pipeline.QueueCommand(
                        x => x.GetSetCount("hangfire:queues"), 
                        x => stats.Queues = x);

                    pipeline.QueueCommand(
                        x => x.GetSortedSetCount("hangfire:schedule"), 
                        x => stats.Scheduled = x);

                    pipeline.QueueCommand(
                        x => x.GetSortedSetCount("hangfire:processing"), 
                        x => stats.Processing = x);

                    pipeline.QueueCommand(
                        x => x.GetValue("hangfire:stats:succeeded"), 
                        x => stats.Succeeded = long.Parse(x ?? "0"));

                    pipeline.QueueCommand(
                        x => x.GetSortedSetCount("hangfire:failed"),
                        x => stats.Failed = x);

                    foreach (var queue in queues)
                    {
                        var queueName = queue;
                        pipeline.QueueCommand(
                            x => x.GetListCount(String.Format("hangfire:queue:{0}", queueName)),
                            x => stats.Enqueued += x);
                    }

                    pipeline.Flush();
                }

                return stats;
            }
        }
    }
}
