using System.Net;
using System.Text.Json;
using Aiursoft.Canon;
using Aiursoft.Kanban.Configuration;
using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.InMemory;
using Aiursoft.Kanban.Services;
using Aiursoft.Kanban.Services.BackgroundJobs;
using Aiursoft.WebTools;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aiursoft.Kanban.Tests;

[TestClass]
public class EmbeddingRetryTests
{
    private InMemoryContext _db = null!;
    private MemoryCache _memory = null!;
    private GlobalSettingsService _settings = null!;
    private CardEmbeddingCache _cache = null!;
    private CardVectorSearchService _service = null!;
    private KanbanCard _item = null!;

    [TestInitialize]
    public async Task Initialize()
    {
        _db = new InMemoryContext(new DbContextOptionsBuilder<InMemoryContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _memory = new MemoryCache(new MemoryCacheOptions());
        _settings = new GlobalSettingsService(_db, new ConfigurationBuilder().Build(), null!, _memory,
            new EphemeralDataProtectionProvider(), NullLogger<GlobalSettingsService>.Instance);
        _db.GlobalSettings.AddRange(
            new GlobalSetting { Key = SettingsMap.EnableEmbeddingBasedSearch, Value = "True" },
            new GlobalSetting { Key = SettingsMap.EmbeddingOllamaInstance, Value = "https://embedding.example.com" },
            new GlobalSetting { Key = SettingsMap.EmbeddingModel, Value = "test-embedding-model" });
        _item = new KanbanCard
        {
            Title = "Matching card", Description = new string('a', 10000), Column = new KanbanColumn { Name = "To do", Board = new KanbanBoard { Name = "Test board", UserId = "owner" } },
            Embedding = EmbeddingHelper.Serialize([1f, 0f])
        };
        _db.KanbanCards.Add(_item);
        await _db.SaveChangesAsync();
        _cache = new CardEmbeddingCache(NullLogger<CardEmbeddingCache>.Instance);
        await _cache.LoadAsync(_db);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _db.Dispose();
        _memory.Dispose();
    }

    [TestMethod]
    [DataRow(HttpStatusCode.BadRequest)]
    [DataRow(HttpStatusCode.RequestEntityTooLarge)]
    public async Task QueryShrinksContextErrorsAndCachesOnlyTheSuccessfulVector(HttpStatusCode status)
    {
        using var handler = new EmbeddingHandler { ErrorStatus = status };
        _service = new CardVectorSearchService(_db, _cache, _settings, handler, NullLogger<CardVectorSearchService>.Instance);
        var query = "Introduction " + new string('a', 10000) + " Conclusion";

        var result = await _service.SearchAsync(_db.KanbanCards.AsNoTracking(), query, [ _item.Column.BoardId ], 1, 10);

        Assert.IsTrue(result.UsedAi);
        Assert.AreEqual(1, result.TotalCount);
        CollectionAssert.AreEqual(new[] { 8000, 4000, 2000 }, handler.Inputs.Select(input => input.Length).ToArray());
        foreach (var input in handler.Inputs) Assert.AreEqual(query[..input.Length], input);
        Assert.AreEqual(1, await _db.SearchEmbeddings.CountAsync());
        await _service.SearchAsync(_db.KanbanCards.AsNoTracking(), query, [ _item.Column.BoardId ], 1, 10);
        Assert.AreEqual(3, handler.Inputs.Count, "Successful query embeddings must be reused.");
    }

    [TestMethod]
    [DataRow(HttpStatusCode.BadRequest, "Request body length exceeds the limit")]
    [DataRow(HttpStatusCode.RequestEntityTooLarge, "Request body length exceeds the limit")]
    [DataRow(HttpStatusCode.RequestEntityTooLarge, "Context service unavailable")]
    [DataRow(HttpStatusCode.InternalServerError, "context length exceeded")]
    public async Task UnrelatedQueryErrorsFallBackWithoutShrinking(HttpStatusCode status, string error)
    {
        using var handler = new EmbeddingHandler { ErrorStatus = status, ErrorMessage = error, ContextLimit = 0 };
        _service = new CardVectorSearchService(_db, _cache, _settings, handler, NullLogger<CardVectorSearchService>.Instance);
        var query = new string('a', 10000);

        var result = await _service.SearchAsync(_db.KanbanCards.AsNoTracking(), query, [ _item.Column.BoardId ], 1, 10);

        Assert.IsFalse(result.UsedAi);
        Assert.AreEqual(1, handler.Inputs.Count);
        Assert.AreEqual(8000, handler.Inputs[0].Length);
        Assert.AreEqual(0, await _db.SearchEmbeddings.CountAsync());
    }

    [TestMethod]
    public async Task RepeatedContextErrorsStopAtTheMinimumBudget()
    {
        using var handler = new EmbeddingHandler { ContextLimit = 0 };
        _service = new CardVectorSearchService(_db, _cache, _settings, handler, NullLogger<CardVectorSearchService>.Instance);
        var query = new string('a', 10000);

        var result = await _service.SearchAsync(_db.KanbanCards.AsNoTracking(), query, [ _item.Column.BoardId ], 1, 10);

        Assert.IsFalse(result.UsedAi);
        CollectionAssert.AreEqual(new[] { 8000, 4000, 2000, 1000, 500 }, handler.Inputs.Select(input => input.Length).ToArray());
        Assert.AreEqual(0, await _db.SearchEmbeddings.CountAsync());
    }

    [TestMethod]
    [DataRow(HttpStatusCode.BadRequest, "Request body length exceeds the limit")]
    [DataRow(HttpStatusCode.RequestEntityTooLarge, "Request body length exceeds the limit")]
    [DataRow(HttpStatusCode.InternalServerError, "context length exceeded")]
    public async Task BackgroundErrorsNeverRetryWithUnrelatedTruncation(HttpStatusCode status, string error)
    {
        _item.Embedding = null;
        await _db.SaveChangesAsync();
        using var handler = new EmbeddingHandler { ErrorStatus = status, ErrorMessage = error, ContextLimit = 0 };
        var retry = new RetryEngine(NullLogger<RetryEngine>.Instance);
        using var provider = new ServiceCollection().AddSingleton<TemplateDbContext>(_db).BuildServiceProvider();
        var job = new GenerateCardEmbeddingsJob(handler, provider.GetRequiredService<IServiceScopeFactory>(), _settings,
            retry, NullLogger<GenerateCardEmbeddingsJob>.Instance);

        await job.ExecuteAsync();

        Assert.IsTrue(handler.Inputs.Count > 0);
        Assert.IsTrue(handler.Inputs.All(input => input.Length == 8000), "Transient job retries must not shrink unrelated failures.");
        Assert.IsNull(_item.Embedding);
    }

    [TestMethod]
    [DataRow("Order")]
    [DataRow("Column")]
    [DataRow("Priority")]
    [DataRow("Assignee")]
    [DataRow("Schedule")]
    [DataRow("ActualTimes")]
    [DataRow("Recurrence")]
    public async Task MetadataChangesReuseTheExistingCardEmbedding(string change)
    {
        _item.LastEmbeddedAt = _item.LastUpdatedAt;
        await _db.SaveChangesAsync();
        var contentVersion = _item.LastUpdatedAt;
        var vector = _item.Embedding!.ToArray();

        switch (change)
        {
            case "Order": _item.Order++; break;
            case "Column":
                _item.Column = new KanbanColumn { Name = "Done", Board = _item.Column.Board };
                break;
            case "Priority": _item.Priority = Priority.Urgent; break;
            case "Assignee": _item.AssignedUserId = "assignee"; break;
            case "Schedule":
                _item.DueDate = DateTime.UtcNow.AddDays(7);
                _item.PlannedStartTime = DateTime.UtcNow;
                break;
            case "ActualTimes":
                _item.ActualStartTime = DateTime.UtcNow;
                _item.ActualEndTime = DateTime.UtcNow.AddHours(1);
                break;
            case "Recurrence":
                _item.RecurrenceInterval = 1;
                _item.RecurrenceUnit = RecurrenceUnit.Week;
                break;
        }
        await _db.SaveChangesAsync();

        using var handler = new EmbeddingHandler { ContextLimit = int.MaxValue };
        using var provider = new ServiceCollection().AddSingleton<TemplateDbContext>(_db).BuildServiceProvider();
        var job = new GenerateCardEmbeddingsJob(handler, provider.GetRequiredService<IServiceScopeFactory>(), _settings,
            new RetryEngine(NullLogger<RetryEngine>.Instance), NullLogger<GenerateCardEmbeddingsJob>.Instance);
        await job.ExecuteAsync();

        Assert.AreEqual(0, handler.Inputs.Count, "Metadata changes must not call the embedding API.");
        Assert.AreEqual(contentVersion, _item.LastUpdatedAt);
        CollectionAssert.AreEqual(vector, _item.Embedding);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task TextChangesRegenerateTheCardEmbeddingOnce(bool changeTitle)
    {
        _item.Description = "Original description";
        await _db.SaveChangesAsync();
        _item.LastEmbeddedAt = _item.LastUpdatedAt;
        await _db.SaveChangesAsync();
        if (changeTitle) _item.Title = "Changed title";
        else _item.Description = "Changed description";
        await _db.SaveChangesAsync();
        Assert.IsTrue(_item.LastUpdatedAt > _item.LastEmbeddedAt);
        _item.Order++;
        await _db.SaveChangesAsync();

        using var handler = new EmbeddingHandler { ContextLimit = int.MaxValue };
        using var provider = new ServiceCollection().AddSingleton<TemplateDbContext>(_db).BuildServiceProvider();
        var job = new GenerateCardEmbeddingsJob(handler, provider.GetRequiredService<IServiceScopeFactory>(), _settings,
            new RetryEngine(NullLogger<RetryEngine>.Instance), NullLogger<GenerateCardEmbeddingsJob>.Instance);
        await job.ExecuteAsync();
        await job.ExecuteAsync();

        Assert.HasCount(1, handler.Inputs);
        Assert.AreEqual($"{_item.Title}\n{_item.Description}", handler.Inputs[0]);
        Assert.AreEqual(_item.LastUpdatedAt, _item.LastEmbeddedAt);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task OnlyConcurrentTextChangesDiscardAnEmbeddingResult(bool changeTitle)
    {
        _item.Embedding = null;
        await _db.SaveChangesAsync();
        using var handler = new EmbeddingHandler
        {
            ContextLimit = int.MaxValue,
            OnFirstRequest = async () =>
            {
                if (changeTitle) _item.Title = "Edited during generation";
                else _item.Order++;
                await _db.SaveChangesAsync();
            }
        };
        using var provider = new ServiceCollection().AddSingleton<TemplateDbContext>(_db).BuildServiceProvider();
        var job = new GenerateCardEmbeddingsJob(handler, provider.GetRequiredService<IServiceScopeFactory>(), _settings,
            new RetryEngine(NullLogger<RetryEngine>.Instance), NullLogger<GenerateCardEmbeddingsJob>.Instance);

        await job.ExecuteAsync();
        if (changeTitle) Assert.IsNull(_item.Embedding, "An old vector must not be saved after a text edit.");
        else Assert.IsNotNull(_item.Embedding, "A move must not discard a valid vector.");
        await job.ExecuteAsync();
        await job.ExecuteAsync();

        Assert.AreEqual(changeTitle ? 2 : 1, handler.Inputs.Count);
        Assert.AreEqual(_item.LastUpdatedAt, _item.LastEmbeddedAt);
    }

    private sealed class EmbeddingHandler : HttpMessageHandler, IHttpClientFactory
    {
        public List<string> Inputs { get; } = [];
        public HttpStatusCode ErrorStatus { get; init; } = HttpStatusCode.RequestEntityTooLarge;
        public string ErrorMessage { get; init; } = "{\"error\":\"the input length exceeds the context length\"}";
        public int ContextLimit { get; init; } = 2000;
        public Func<Task>? OnFirstRequest { get; init; }

        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.AreEqual("/api/embed", request.RequestUri!.AbsolutePath);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Assert.AreEqual("test-embedding-model", body.RootElement.GetProperty("model").GetString());
            var input = body.RootElement.GetProperty("input").GetString()!;
            Inputs.Add(input);
            if (Inputs.Count == 1 && OnFirstRequest != null) await OnFirstRequest();
            return input.Length > ContextLimit
                ? new HttpResponseMessage(ErrorStatus) { Content = new StringContent(ErrorMessage) }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { embeddings = new[] { new[] { 1f, 0f } } }) };
        }
    }
}
