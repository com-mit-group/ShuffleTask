using ShuffleTask.Models;
using ShuffleTask.Services;

return new ShuffleTask.Tests.ImportanceUrgencyTestHarness().Run();

namespace ShuffleTask.Tests
{
    internal sealed class ImportanceUrgencyTestHarness
    {
        private readonly List<TestResult> _results = new();
        private bool _hasFailure;

        internal int Run()
        {
            RunTest("Imminent deadline outranks weekly repeat", () =>
            {
                var now = new DateTime(2024, 1, 1, 9, 0, 0, DateTimeKind.Local);
                var settings = new AppSettings { StreakBias = 0.3 };

                var deadlineTask = new TaskItem
                {
                    Title = "Project report",
                    Importance = 5,
                    Deadline = now.AddHours(24),
                    Repeat = RepeatType.None,
                    AllowedPeriod = AllowedPeriod.Any
                };

                var repeatingTask = new TaskItem
                {
                    Title = "Weekly laundry",
                    Importance = 3,
                    Repeat = RepeatType.Weekly,
                    Weekdays = Weekdays.Mon,
                    LastDoneAt = now.AddDays(-7),
                    AllowedPeriod = AllowedPeriod.Any
                };

                var deadlineScore = ImportanceUrgencyCalculator.Calculate(deadlineTask, now, settings);
                var repeatScore = ImportanceUrgencyCalculator.Calculate(repeatingTask, now, settings);

                Assert(deadlineScore.CombinedScore > repeatScore.CombinedScore,
                    $"Expected deadline score {deadlineScore.CombinedScore:F2} to exceed repeating score {repeatScore.CombinedScore:F2}");
                Assert(deadlineScore.WeightedDeadlineUrgency > repeatScore.WeightedDeadlineUrgency,
                    "Deadline urgency should be higher for dated work");
            });

            RunTest("Repeating task urgency is dampened", () =>
            {
                var now = new DateTime(2024, 1, 1, 9, 0, 0, DateTimeKind.Local);
                var settings = new AppSettings { StreakBias = 0.5 };

                var routineTask = new TaskItem
                {
                    Title = "Daily stand-up",
                    Importance = 4,
                    Repeat = RepeatType.Daily,
                    LastDoneAt = now.AddHours(-3),
                    AllowedPeriod = AllowedPeriod.Any
                };

                var deadlineTask = new TaskItem
                {
                    Title = "Submit taxes",
                    Importance = 3,
                    Deadline = now.AddHours(6),
                    Repeat = RepeatType.None,
                    AllowedPeriod = AllowedPeriod.Any
                };

                var routineScore = ImportanceUrgencyCalculator.Calculate(routineTask, now, settings);
                var deadlineScore = ImportanceUrgencyCalculator.Calculate(deadlineTask, now, settings);

                Assert(routineScore.WeightedUrgency < deadlineScore.WeightedUrgency,
                    "Routine work should have less urgency weight than the imminent deadline");
                Assert(deadlineScore.CombinedScore > routineScore.CombinedScore,
                    "Deadline-driven work should lead the combined score");
            });

            RunTest("Scheduler favors highest combined score", () =>
            {
                var now = new DateTime(2024, 1, 1, 9, 0, 0, DateTimeKind.Local);
                var settings = new AppSettings { StreakBias = 0.3, StableRandomnessPerDay = true };
                var scheduler = new SchedulerService(deterministic: true);

                var deadlineTask = new TaskItem
                {
                    Id = "A",
                    Title = "Prepare slides",
                    Importance = 4,
                    Deadline = now.AddHours(4),
                    Repeat = RepeatType.None,
                    AllowedPeriod = AllowedPeriod.Any
                };

                var routineTask = new TaskItem
                {
                    Id = "B",
                    Title = "Daily inbox zero",
                    Importance = 5,
                    Repeat = RepeatType.Daily,
                    LastDoneAt = now.AddHours(-2),
                    AllowedPeriod = AllowedPeriod.Any
                };

                var picked = scheduler.PickNextTask(new[] { deadlineTask, routineTask }, settings, now);
                Assert(picked?.Id == "A", "Scheduler should pick the deadline task with the higher combined score");
            });

            foreach (var result in _results)
            {
                var prefix = result.Success ? "[PASS]" : "[FAIL]";
                var suffix = result.Success ? string.Empty : $": {result.ErrorMessage}";
                Console.WriteLine($"{prefix} {result.Name}{suffix}");
            }

            if (_hasFailure)
            {
                Console.WriteLine("One or more tests failed.");
                return 1;
            }

            Console.WriteLine("All importance-urgency tests passed.");
            return 0;
        }

        private void RunTest(string name, Action test)
        {
            try
            {
                test();
                _results.Add(new TestResult(name, true, null));
            }
            catch (Exception ex)
            {
                _hasFailure = true;
                _results.Add(new TestResult(name, false, ex.Message));
            }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }

    internal readonly record struct TestResult(string Name, bool Success, string? ErrorMessage);
}
