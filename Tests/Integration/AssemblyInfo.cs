// Интеграционные тесты гоняются последовательно: часть из них чистит пулы подключений SQLite
// (SqliteGate.ClearAllPools) — это общее на процесс, и параллельные тесты задевали бы чужие
// подключения. Вторая причина — статика AppSettings (папка данных и хранилище настроек).
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
