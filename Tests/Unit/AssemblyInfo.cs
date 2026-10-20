// Модульные тесты гоняются последовательно: AppSettings.AttachStore кладет хранилище настроек в
// статическое поле, одно на процесс, и параллельные классы подменяли бы его друг другу.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
