using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using JumpingNinja.Api.Leaderboard;
using Xunit;

namespace JumpingNinja.Tests;

public sealed class LeaderboardApiTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task LeaderboardEndpointsRequireAuthentication()
    {
        using var factory = AuthApiFactory.CreateIsolated(
            "jumping-ninja-leaderboard-auth-" + Guid.NewGuid().ToString("N"));
        using HttpClient client = factory.CreateClient();

        var requests = new[]
        {
            (HttpMethod.Get, "/api/v1/ninjas", (object?)null),
            (HttpMethod.Post, "/api/v1/ninjas", new CreateNinjaRequest { Name = "Red" }),
            (HttpMethod.Post, "/api/v1/ninjas/import", new ImportNinjaRequest
            {
                LegacyProfileId = Guid.NewGuid().ToString("D"),
                Name = "Old",
                BestScore = 1
            }),
            (HttpMethod.Put, $"/api/v1/ninjas/{Guid.NewGuid()}/best-score", new SubmitBestScoreRequest { BestScore = 1 }),
            (HttpMethod.Get, "/api/v1/leaderboard", (object?)null),
            (HttpMethod.Get, "/api/v1/leaderboard/targets", (object?)null)
        };

        foreach (var (method, path, payload) in requests)
        {
            using HttpResponseMessage response = await SendAsync(client, method, path, null, payload);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public async Task NinjaNamesAreCaseInsensitiveAndScoresCannotBeNegative()
    {
        using var factory = AuthApiFactory.CreateIsolated(
            "jumping-ninja-validation-" + Guid.NewGuid().ToString("N"));
        using HttpClient client = factory.CreateClient();
        string token = await RegisterAsync(client, "valid_" + Guid.NewGuid().ToString("N")[..6]);

        await CreateNinjaAsync(client, token, "Red");
        using (HttpResponseMessage duplicate = await SendAsync(
                   client,
                   HttpMethod.Post,
                   "/api/v1/ninjas",
                   token,
                   new CreateNinjaRequest { Name = " red " }))
        {
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
            using JsonDocument body = await ReadJsonAsync(duplicate);
            Assert.Equal("ninja_name_taken", body.RootElement.GetProperty("code").GetString());
        }

        using (HttpResponseMessage negative = await SendAsync(
                   client,
                   HttpMethod.Put,
                   $"/api/v1/ninjas/{Guid.NewGuid()}/best-score",
                   token,
                   new SubmitBestScoreRequest { BestScore = -1 }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, negative.StatusCode);
            using JsonDocument body = await ReadJsonAsync(negative);
            Assert.Equal("score_invalid", body.RootElement.GetProperty("code").GetString());
        }
    }

    [Fact]
    public async Task LeaderboardAssignsCompetitionRanksForThreeWayTies()
    {
        using var factory = AuthApiFactory.CreateIsolated(
            "jumping-ninja-tie-ranks-" + Guid.NewGuid().ToString("N"));
        using HttpClient first = factory.CreateClient();
        using HttpClient second = factory.CreateClient();
        using HttpClient third = factory.CreateClient();
        using HttpClient fourth = factory.CreateClient();
        string firstToken = await RegisterAsync(first, "tie_first_" + Guid.NewGuid().ToString("N")[..4]);
        string secondToken = await RegisterAsync(second, "tie_second_" + Guid.NewGuid().ToString("N")[..4]);
        string thirdToken = await RegisterAsync(third, "tie_third_" + Guid.NewGuid().ToString("N")[..4]);
        string fourthToken = await RegisterAsync(fourth, "tie_fourth_" + Guid.NewGuid().ToString("N")[..4]);

        NinjaResponse firstNinja = await CreateNinjaAsync(first, firstToken, "First");
        NinjaResponse secondNinja = await CreateNinjaAsync(second, secondToken, "Second");
        NinjaResponse thirdNinja = await CreateNinjaAsync(third, thirdToken, "Third");
        NinjaResponse fourthNinja = await CreateNinjaAsync(fourth, fourthToken, "Fourth");
        await SubmitScoreAsync(first, firstToken, firstNinja.Id, 20);
        await SubmitScoreAsync(second, secondToken, secondNinja.Id, 20);
        await SubmitScoreAsync(third, thirdToken, thirdNinja.Id, 20);
        await SubmitScoreAsync(fourth, fourthToken, fourthNinja.Id, 10);

        using HttpResponseMessage response = await SendAsync(
            first,
            HttpMethod.Get,
            "/api/v1/leaderboard?limit=100",
            firstToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = await ReadJsonAsync(response);
        JsonElement entries = body.RootElement.GetProperty("entries");
        Assert.Equal(4, entries.GetArrayLength());
        Assert.Equal(1, entries[0].GetProperty("rank").GetInt32());
        Assert.Equal(1, entries[1].GetProperty("rank").GetInt32());
        Assert.Equal(1, entries[2].GetProperty("rank").GetInt32());
        Assert.Equal(4, entries[3].GetProperty("rank").GetInt32());
    }

    [Fact]
    public async Task NinjasAreAccountScopedAndLeaderboardAggregatesOneRowPerAccount()
    {
        using var factory = AuthApiFactory.CreateIsolated(
            "jumping-ninja-leaderboard-" + Guid.NewGuid().ToString("N"));
        using HttpClient alice = factory.CreateClient();
        using HttpClient bob = factory.CreateClient();
        string aliceToken = await RegisterAsync(alice, "alice_" + Guid.NewGuid().ToString("N")[..6]);
        string bobToken = await RegisterAsync(bob, "bob_" + Guid.NewGuid().ToString("N")[..6]);

        var aliceNinjaA = await CreateNinjaAsync(alice, aliceToken, "Red");
        var aliceNinjaB = await CreateNinjaAsync(alice, aliceToken, "Blue");
        await SubmitScoreAsync(alice, aliceToken, aliceNinjaA.Id, 12);
        var improved = await SubmitScoreAsync(alice, aliceToken, aliceNinjaB.Id, 20);
        Assert.True(improved.AccountImproved);

        var bobNinja = await CreateNinjaAsync(bob, bobToken, "Scout");
        await SubmitScoreAsync(bob, bobToken, bobNinja.Id, 20);

        using (var ninjasResponse = await SendAsync(alice, HttpMethod.Get, "/api/v1/ninjas", aliceToken))
        {
            Assert.Equal(HttpStatusCode.OK, ninjasResponse.StatusCode);
            using JsonDocument body = await ReadJsonAsync(ninjasResponse);
            Assert.Equal(2, body.RootElement.GetProperty("ninjas").GetArrayLength());
            Assert.Equal(20, body.RootElement.GetProperty("accountBest").GetProperty("bestScore").GetInt32());
        }

        using (var leaderboardResponse = await SendAsync(
                   alice,
                   HttpMethod.Get,
                   "/api/v1/leaderboard?limit=100",
                   aliceToken))
        {
            Assert.Equal(HttpStatusCode.OK, leaderboardResponse.StatusCode);
            using JsonDocument body = await ReadJsonAsync(leaderboardResponse);
            JsonElement entries = body.RootElement.GetProperty("entries");
            Assert.Equal(2, entries.GetArrayLength());
            Assert.Equal(1, entries[0].GetProperty("rank").GetInt32());
            Assert.Equal(1, entries[1].GetProperty("rank").GetInt32());
            Assert.Equal("Blue", entries[0].GetProperty("ninjaName").GetString());
            Assert.Equal(1, body.RootElement.GetProperty("currentUser").GetProperty("rank").GetInt32());
        }

        var lower = await SubmitScoreAsync(alice, aliceToken, aliceNinjaB.Id, 3);
        Assert.False(lower.NinjaImproved);
        Assert.False(lower.AccountImproved);
        Assert.Equal(20, lower.Ninja.BestScore);

        using var forbidden = await SendAsync(
            bob,
            HttpMethod.Put,
            $"/api/v1/ninjas/{aliceNinjaA.Id}/best-score",
            bobToken,
            new SubmitBestScoreRequest { BestScore = 99 });
        Assert.Equal(HttpStatusCode.NotFound, forbidden.StatusCode);
    }

    [Fact]
    public async Task LegacyImportIsIdempotentAndSameNamesMerge()
    {
        using var factory = AuthApiFactory.CreateIsolated(
            "jumping-ninja-import-" + Guid.NewGuid().ToString("N"));
        using HttpClient owner = factory.CreateClient();
        using HttpClient other = factory.CreateClient();
        string ownerToken = await RegisterAsync(owner, "owner_" + Guid.NewGuid().ToString("N")[..6]);
        string otherToken = await RegisterAsync(other, "other_" + Guid.NewGuid().ToString("N")[..6]);
        Guid legacyId = Guid.NewGuid();

        HttpResponseMessage first = await SendAsync(
            owner,
            HttpMethod.Post,
            "/api/v1/ninjas/import",
            ownerToken,
            new ImportNinjaRequest { LegacyProfileId = legacyId.ToString("N"), Name = "Old Ninja", BestScore = 7 });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using JsonDocument firstBody = await ReadJsonAsync(first);
        string ninjaId = firstBody.RootElement.GetProperty("ninja").GetProperty("id").GetString()!;

        HttpResponseMessage retry = await SendAsync(
            owner,
            HttpMethod.Post,
            "/api/v1/ninjas/import",
            ownerToken,
            new ImportNinjaRequest { LegacyProfileId = legacyId.ToString("D"), Name = "Old Ninja", BestScore = 9 });
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        using JsonDocument retryBody = await ReadJsonAsync(retry);
        Assert.Equal(ninjaId, retryBody.RootElement.GetProperty("ninja").GetProperty("id").GetString());
        Assert.Equal(9, retryBody.RootElement.GetProperty("ninja").GetProperty("bestScore").GetInt32());

        using (HttpResponseMessage list = await SendAsync(
                   owner,
                   HttpMethod.Get,
                   "/api/v1/ninjas",
                   ownerToken))
        {
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            using JsonDocument listBody = await ReadJsonAsync(list);
            Assert.Equal(1, listBody.RootElement.GetProperty("ninjas").GetArrayLength());
        }

        HttpResponseMessage merged = await SendAsync(
            owner,
            HttpMethod.Post,
            "/api/v1/ninjas/import",
            ownerToken,
            new ImportNinjaRequest { LegacyProfileId = Guid.NewGuid().ToString("D"), Name = "old ninja", BestScore = 11 });
        Assert.Equal(HttpStatusCode.OK, merged.StatusCode);
        using JsonDocument mergedBody = await ReadJsonAsync(merged);
        Assert.True(mergedBody.RootElement.GetProperty("mergedByName").GetBoolean());
        Assert.Equal(ninjaId, mergedBody.RootElement.GetProperty("ninja").GetProperty("id").GetString());
        Assert.Equal(11, mergedBody.RootElement.GetProperty("ninja").GetProperty("bestScore").GetInt32());

        HttpResponseMessage claimed = await SendAsync(
            other,
            HttpMethod.Post,
            "/api/v1/ninjas/import",
            otherToken,
            new ImportNinjaRequest { LegacyProfileId = legacyId.ToString("D"), Name = "Copied", BestScore = 50 });
        Assert.Equal(HttpStatusCode.Conflict, claimed.StatusCode);
    }

    [Fact]
    public async Task LegacyImportRejectsMalformedIdsWithStableError()
    {
        using var factory = AuthApiFactory.CreateIsolated(
            "jumping-ninja-import-invalid-" + Guid.NewGuid().ToString("N"));
        using HttpClient client = factory.CreateClient();
        string token = await RegisterAsync(client, "invalid_" + Guid.NewGuid().ToString("N")[..6]);

        using HttpResponseMessage response = await SendAsync(
            client,
            HttpMethod.Post,
            "/api/v1/ninjas/import",
            token,
            new ImportNinjaRequest
            {
                LegacyProfileId = "not-a-guid",
                Name = "Old Ninja",
                BestScore = 8
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using JsonDocument body = await ReadJsonAsync(response);
        Assert.Equal("legacy_profile_invalid", body.RootElement.GetProperty("code").GetString());
        Assert.Equal("legacyProfileId", body.RootElement.GetProperty("field").GetString());
    }

    [Fact]
    public async Task NinjaLimitAndTargetsAreEnforced()
    {
        using var factory = AuthApiFactory.CreateIsolated(
            "jumping-ninja-limit-" + Guid.NewGuid().ToString("N"));
        using HttpClient client = factory.CreateClient();
        string token = await RegisterAsync(client, "limit_" + Guid.NewGuid().ToString("N")[..6]);

        for (int index = 0; index < LeaderboardRules.MaximumNinjasPerAccount; index++)
        {
            HttpResponseMessage response = await SendAsync(
                client,
                HttpMethod.Post,
                "/api/v1/ninjas",
                token,
                new CreateNinjaRequest { Name = "N" + index });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        HttpResponseMessage limit = await SendAsync(
            client,
            HttpMethod.Post,
            "/api/v1/ninjas",
            token,
            new CreateNinjaRequest { Name = "Overflow" });
        Assert.Equal(HttpStatusCode.Conflict, limit.StatusCode);
    }

    [Fact]
    public async Task TargetsGroupTiesAndExcludeTheCurrentAccount()
    {
        using var factory = AuthApiFactory.CreateIsolated(
            "jumping-ninja-targets-" + Guid.NewGuid().ToString("N"));
        using HttpClient current = factory.CreateClient();
        using HttpClient first = factory.CreateClient();
        using HttpClient second = factory.CreateClient();
        using HttpClient higher = factory.CreateClient();
        string currentToken = await RegisterAsync(current, "current_" + Guid.NewGuid().ToString("N")[..5]);
        string firstToken = await RegisterAsync(first, "first_" + Guid.NewGuid().ToString("N")[..5]);
        string secondToken = await RegisterAsync(second, "second_" + Guid.NewGuid().ToString("N")[..4]);
        string higherToken = await RegisterAsync(higher, "higher_" + Guid.NewGuid().ToString("N")[..4]);

        NinjaResponse currentNinja = await CreateNinjaAsync(current, currentToken, "Current");
        NinjaResponse firstNinja = await CreateNinjaAsync(first, firstToken, "First");
        NinjaResponse secondNinja = await CreateNinjaAsync(second, secondToken, "Second");
        NinjaResponse higherNinja = await CreateNinjaAsync(higher, higherToken, "Higher");
        await SubmitScoreAsync(current, currentToken, currentNinja.Id, 5);
        await SubmitScoreAsync(first, firstToken, firstNinja.Id, 5);
        await SubmitScoreAsync(second, secondToken, secondNinja.Id, 5);
        await SubmitScoreAsync(higher, higherToken, higherNinja.Id, 9);

        using HttpResponseMessage response = await SendAsync(
            current,
            HttpMethod.Get,
            "/api/v1/leaderboard/targets?fromScore=5&limit=20",
            currentToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = await ReadJsonAsync(response);
        JsonElement targets = body.RootElement.GetProperty("targets");
        Assert.Equal(2, targets.GetArrayLength());
        Assert.Equal(5, targets[0].GetProperty("score").GetInt32());
        Assert.Equal(2, targets[0].GetProperty("accountCount").GetInt32());
        Assert.NotEqual("current", targets[0].GetProperty("username").GetString());
        Assert.Equal(9, targets[1].GetProperty("score").GetInt32());
    }

    private static async Task<string> RegisterAsync(HttpClient client, string username)
    {
        using var response = await SendAsync(
            client,
            HttpMethod.Post,
            "/api/v1/auth/register",
            null,
            new { username, password = "TestPassword123" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using JsonDocument body = await ReadJsonAsync(response);
        return body.RootElement.GetProperty("accessToken").GetString()!;
    }

    private static async Task<NinjaResponse> CreateNinjaAsync(
        HttpClient client,
        string token,
        string name)
    {
        using var response = await SendAsync(
            client,
            HttpMethod.Post,
            "/api/v1/ninjas",
            token,
            new CreateNinjaRequest { Name = name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using JsonDocument body = await ReadJsonAsync(response);
        return JsonSerializer.Deserialize<NinjaResponse>(body.RootElement.GetRawText(), JsonOptions)!;
    }

    private static async Task<ScoreSubmissionResponse> SubmitScoreAsync(
        HttpClient client,
        string token,
        Guid ninjaId,
        int score)
    {
        using var response = await SendAsync(
            client,
            HttpMethod.Put,
            $"/api/v1/ninjas/{ninjaId}/best-score",
            token,
            new SubmitBestScoreRequest { BestScore = score });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = await ReadJsonAsync(response);
        return JsonSerializer.Deserialize<ScoreSubmissionResponse>(body.RootElement.GetRawText(), JsonOptions)!;
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        string? token,
        object? payload = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (payload is not null)
        {
            request.Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");
        }

        return await client.SendAsync(request);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        await using Stream body = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(body);
    }
}
