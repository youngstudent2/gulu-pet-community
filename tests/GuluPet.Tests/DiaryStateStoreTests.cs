using GuluPet.Diary;

namespace GuluPet.Tests;

internal static class DiaryStateStoreTests
{
    public static void RunAll()
    {
        Run(nameof(MeaningfulRecordUsesStrictThreshold), MeaningfulRecordUsesStrictThreshold);
        Run(nameof(StateRoundTripsAndReplacesAtomically), StateRoundTripsAndReplacesAtomically);
        Run(nameof(CorruptStateIsQuarantined), CorruptStateIsQuarantined);
        Run(nameof(LedgerDeduplicatesUnlocksAndRespectsFailures), LedgerDeduplicatesUnlocksAndRespectsFailures);
        Run(nameof(LegacyBalanceFailureMigratesToRetryable),
            LegacyBalanceFailureMigratesToRetryable);
        Run(nameof(PromptUsesMummyAndExpressesReservedCharacter),
            PromptUsesMummyAndExpressesReservedCharacter);
    }

    private static void MeaningfulRecordUsesStrictThreshold()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DiaryDayState exact = DiaryDayState.Create(
            new DateOnly(2026, 8, 3),
            now) with
        {
            RuntimeSeconds = 1800,
        };
        BehaviorTestCheck.False(exact.HasMeaningfulRecord);
        BehaviorTestCheck.True((exact with
        {
            RuntimeSeconds = 1801,
        }).HasMeaningfulRecord);
        BehaviorTestCheck.True((exact with
        {
            Interactions =
            [
                new DiaryInteractionCount
                {
                    Kind = DiaryInteractionKinds.Click,
                    Count = 1,
                },
            ],
        }).HasMeaningfulRecord);
    }

    private static void StateRoundTripsAndReplacesAtomically()
    {
        WithTemporaryStore(
            store =>
            {
                DateTimeOffset now = DateTimeOffset.UtcNow;
                var ledger = new DiaryLedger(store);
                ledger.AddRuntime(new DateOnly(2026, 8, 3), 1200, now);
                ledger.AddRuntime(new DateOnly(2026, 8, 3), 700, now.AddMinutes(12));
                ledger.RecordInteraction(
                    new DateOnly(2026, 8, 3),
                    DiaryInteractionKinds.Petting,
                    now.AddMinutes(13));

                DiaryState loaded = new DiaryStateStore(store.StatePath).Load();
                DiaryDayState day = loaded.Days.Single();
                BehaviorTestCheck.Equal(1900L, day.RuntimeSeconds);
                BehaviorTestCheck.Equal(1, day.TotalInteractionCount);
                string directory = Path.GetDirectoryName(store.StatePath)!;
                BehaviorTestCheck.Equal(
                    0,
                    Directory.GetFiles(directory, ".*.tmp").Length);
                BehaviorTestCheck.False(File.Exists(
                    Path.Combine(directory, "diary.backup.json")));
            });
    }

    private static void CorruptStateIsQuarantined()
    {
        WithTemporaryStore(
            store =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(store.StatePath)!);
                File.WriteAllText(store.StatePath, "{not-json");
                DiaryState recovered = store.Load();
                BehaviorTestCheck.Equal(0, recovered.Days.Count);
                BehaviorTestCheck.False(File.Exists(store.StatePath));
                BehaviorTestCheck.Equal(
                    1,
                    Directory.GetFiles(
                        Path.GetDirectoryName(store.StatePath)!,
                        "diary.corrupt-*.json").Length);
            });
    }

    private static void LedgerDeduplicatesUnlocksAndRespectsFailures()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateOnly date = new(2026, 8, 3);
        var ledger = new DiaryLedger(
            initialState: DiaryState.CreateDefault(now));
        ledger.AddRuntime(date, 1801, now);
        var postcard = new DiaryUnlockRecord
        {
            Id = "postcard-001",
            Title = "海边来信",
            Detail = "北海",
            UnlockedAtUtc = now,
        };
        BehaviorTestCheck.True(ledger.RecordPostcard(date, postcard, now));
        BehaviorTestCheck.False(ledger.RecordPostcard(date, postcard, now));
        var memory = new DiaryUnlockRecord
        {
            Id = "memory-001",
            Title = "旧标题",
            UnlockedAtUtc = now,
        };
        BehaviorTestCheck.True(ledger.RecordMemory(date, memory, now));
        DiaryUnlockRecord describedMemory = memory with
        {
            Description = "咕噜侧躺在地板上，用前爪拍打眼前来回移动的细绳。",
        };
        BehaviorTestCheck.True(ledger.RecordMemory(
            date,
            describedMemory,
            now.AddSeconds(1)));
        BehaviorTestCheck.False(ledger.RecordMemory(
            date,
            describedMemory,
            now.AddSeconds(2)));
        BehaviorTestCheck.Equal(
            describedMemory.Description,
            ledger.Current.Days.Single().Memories.Single().Description);

        BehaviorTestCheck.Equal(
            1,
            ledger.GetGenerationCandidates(
                date,
                now,
                TimeSpan.FromMinutes(30)).Count);
        ledger.RecordGenerationFailure(
            date,
            DiaryGenerationFailureDisposition.Retryable,
            "http-503",
            now);
        BehaviorTestCheck.Equal(
            0,
            ledger.GetGenerationCandidates(
                date,
                now.AddMinutes(29),
                TimeSpan.FromMinutes(30)).Count);
        BehaviorTestCheck.Equal(
            1,
            ledger.GetGenerationCandidates(
                date,
                now.AddMinutes(30),
                TimeSpan.FromMinutes(30)).Count);

        ledger.RecordGenerationFailure(
            date,
            DiaryGenerationFailureDisposition.Terminal,
            "http-401",
            now.AddMinutes(30));
        BehaviorTestCheck.Equal(
            0,
            ledger.GetGenerationCandidates(
                date,
                now.AddHours(1),
                TimeSpan.FromMinutes(30)).Count);
    }

    private static void LegacyBalanceFailureMigratesToRetryable()
    {
        DateTimeOffset attemptedAtUtc = new(
            2026,
            8,
            3,
            10,
            0,
            0,
            TimeSpan.Zero);
        DateOnly date = DateOnly.FromDateTime(attemptedAtUtc.UtcDateTime);
        var ledger = new DiaryLedger(
            initialState: new DiaryState
            {
                UpdatedAtUtc = attemptedAtUtc,
                Days =
                [
                    DiaryDayState.Create(date, attemptedAtUtc) with
                    {
                        RuntimeSeconds = 1801,
                        GenerationFailure = new DiaryGenerationFailure
                        {
                            Disposition = DiaryGenerationFailureDisposition
                                .InsufficientBalance,
                            Code = "insufficient-balance",
                            AttemptCount = 1,
                            LastAttemptAtUtc = attemptedAtUtc,
                        },
                    },
                ],
            });

        DiaryGenerationFailure migrated = ledger.Current.Days.Single()
            .GenerationFailure!;
        BehaviorTestCheck.Equal(
            DiaryGenerationFailureDisposition.Retryable,
            migrated.Disposition);
        BehaviorTestCheck.Equal(
            attemptedAtUtc.AddHours(6),
            migrated.RetryNotBeforeUtc);
        BehaviorTestCheck.Equal(
            0,
            ledger.GetGenerationCandidates(
                date,
                attemptedAtUtc.AddHours(6).AddTicks(-1),
                TimeSpan.FromMinutes(30)).Count);
        BehaviorTestCheck.Equal(
            1,
            ledger.GetGenerationCandidates(
                date,
                attemptedAtUtc.AddHours(6),
                TimeSpan.FromMinutes(30)).Count);
    }

    private static void PromptUsesMummyAndExpressesReservedCharacter()
    {
        var timeZone = TimeZoneInfo.CreateCustomTimeZone(
            "Diary-Prompt-China",
            TimeSpan.FromHours(8),
            "Diary Prompt China",
            "Diary Prompt China");
        var policy = new DiaryDatePolicy(timeZone);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DiaryDayState day = DiaryDayState.Create(
            new DateOnly(2026, 8, 3),
            now) with
        {
            RuntimeSeconds = 7200,
            Weather = new DiaryWeatherSnapshot
            {
                Kind = "rain",
                TemperatureCelsius = 28.4,
                ObservedAtUtc = now,
            },
            Memories =
            [
                new DiaryUnlockRecord
                {
                    Id = "memory-11",
                    Title = "这个旧标题不应发送",
                    Description = "咕噜仰躺在瓷砖地面上，反复弯曲后腿，最后收腿起身。",
                    UnlockedAtUtc = now,
                },
            ],
        };
        string prompt = DiaryPromptBuilder.BuildUserPrompt(
            DiaryPromptBuilder.CreateInput(day, policy),
            timeZone);
        BehaviorTestCheck.True(prompt.Contains(
            "\"runtimeMinutes\": 120",
            StringComparison.Ordinal));
        BehaviorTestCheck.True(prompt.Contains("下雨", StringComparison.Ordinal));
        BehaviorTestCheck.True(prompt.Contains("interactions", StringComparison.Ordinal));
        BehaviorTestCheck.True(prompt.Contains(
            "\"desc\": \"咕噜仰躺在瓷砖地面上",
            StringComparison.Ordinal));
        BehaviorTestCheck.False(prompt.Contains(
            "这个旧标题不应发送",
            StringComparison.Ordinal));
        BehaviorTestCheck.False(prompt.Contains(
            "\"title\"",
            StringComparison.Ordinal));
        BehaviorTestCheck.True(
            DiaryPromptBuilder.SystemPrompt.Contains(
                "没有互动但运行超过 30 分钟",
                StringComparison.Ordinal));
        BehaviorTestCheck.True(
            DiaryPromptBuilder.SystemPrompt.Contains(
                "把用户称作“妈咪”",
                StringComparison.Ordinal));
        BehaviorTestCheck.False(
            DiaryPromptBuilder.SystemPrompt.Contains(
                "把用户称作“主人”",
                StringComparison.Ordinal));
        BehaviorTestCheck.True(
            DiaryPromptBuilder.SystemPrompt.Contains(
                "少量不改变结构化事实的氛围性想象",
                StringComparison.Ordinal));
        BehaviorTestCheck.True(
            DiaryPromptBuilder.SystemPrompt.Contains(
                "不得新增妈咪主动触发的交互",
                StringComparison.Ordinal));
        BehaviorTestCheck.True(
            DiaryPromptBuilder.SystemPrompt.Contains(
                "累计在线时间",
                StringComparison.Ordinal));
        BehaviorTestCheck.True(
            DiaryPromptBuilder.SystemPrompt.Contains(
                "desc 是逐段核对过的回忆视频画面描述",
                StringComparison.Ordinal));
        BehaviorTestCheck.True(prompt.Contains(
            "少量氛围性细节",
            StringComparison.Ordinal));
        string[] characterContracts =
        [
            "也是一只母猫",
            "平时不太理人",
            "和妈咪已经相处很多年",
            "有很深的喜欢与宽容",
            "不会直白说爱",
            "偶尔才会不经意露出一点可爱",
            "若无其事地恢复矜持",
            "不得为了表现了解而虚构妈咪",
            "不要把咕噜写成热情黏人",
        ];
        foreach (string contract in characterContracts)
        {
            BehaviorTestCheck.True(
                DiaryPromptBuilder.SystemPrompt.Contains(
                    contract,
                    StringComparison.Ordinal));
        }
    }


    private static void WithTemporaryStore(Action<DiaryStateStore> test)
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"GuluPet.Diary.{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            test(new DiaryStateStore(Path.Combine(root, "diary.json")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void Run(string name, Action test)
    {
        test();
        Console.WriteLine($"PASS {nameof(DiaryStateStoreTests)}.{name}");
    }

}
