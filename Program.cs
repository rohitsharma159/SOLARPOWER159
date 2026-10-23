
    
  
Func<ClaimsPrincipal, bool> admin = u => u.Identity?.IsAuthenticated == true && u.IsInRole("Admin");
app.MapGet("/api/health", () => Results.Ok(new { ok = true, database = "SQLite" }));
app.MapGet("/api/auth/me", (ClaimsPrincipal u) => Results.Ok(new { authenticated = admin(u) }));
app.MapPost("/api/auth/login", async Task<IResult> (LoginRequest req, HttpContext ctx, IConfiguration cfg) => {
    var expectedUser = cfg["Admin:Username"] ?? "admin"; var expectedPass = cfg["Admin:Password"] ?? "ChangeThisNow!";
    var a = Encoding.UTF8.GetBytes(req.Username ?? ""); var b = Encoding.UTF8.GetBytes(expectedUser);
    var p = Encoding.UTF8.GetBytes(req.Password ?? ""); var q = Encoding.UTF8.GetBytes(expectedPass);
    if (a.Length != b.Length || p.Length != q.Length || !CryptographicOperations.FixedTimeEquals(a, b) || !CryptographicOperations.FixedTimeEquals(p, q)) return Results.Json(new { error = "Invalid admin ID or password." }, statusCode: 401);
    var claims = new[] { new Claim(ClaimTypes.Name, expectedUser), new Claim(ClaimTypes.Role, "Admin") };
    await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));
    return Results.Ok(new { authenticated = true });
});
app.MapPost("/api/auth/logout", async Task<IResult> (HttpContext ctx) => { await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme); return Results.Ok(new { authenticated = false }); });
app.MapGet("/api/data/{collection}", async Task<IResult> (string collection, ClaimsPrincipal user, StoreDb db) => {
    if (!IsKnown(collection)) return Results.NotFound();
    if (collection is not ("products" or "gallery") && !admin(user)) return Results.Unauthorized();
    var items = await db.Items.Where(x => x.Collection == collection).OrderBy(x => x.Id).ToListAsync();
    var result = new List<Dictionary<string, object?>>();
    foreach (var item in items) { var row = JsonSerializer.Deserialize<Dictionary<string, object?>>(item.Json) ?? new(); row["id"] = item.Id; result.Add(row); }
    return Results.Ok(result);
});
app.MapPut("/api/data/{collection}/{id}", async Task<IResult> (string collection, string id, JsonElement data, ClaimsPrincipal user, StoreDb db) => {
    if (!IsKnown(collection)) return Results.NotFound();
    if ((collection is "products" or "gallery") && !admin(user)) return Results.Unauthorized();
    if (collection is not ("products" or "gallery" or "orders" or "enquiries" or "feedback")) return Results.BadRequest(new { error = "Unknown collection." });
    if (data.ValueKind != JsonValueKind.Object || data.GetRawText().Length > 1_500_000) return Results.BadRequest(new { error = "Invalid or oversized data." });
    var item = await db.Items.FirstOrDefaultAsync(x => x.Collection == collection && x.Id == id);
    if (item is null) { item = new StoreItem { Id = id, Collection = collection, Json = data.GetRawText() }; db.Items.Add(item); }
    else { if (collection is not ("products" or "gallery") && !admin(user)) return Results.Unauthorized(); item.Json = data.GetRawText(); }
    await db.SaveChangesAsync(); return Results.Ok(new { id });
});
app.MapPatch("/api/data/{collection}/{id}", async Task<IResult> (string collection, string id, JsonElement patch, ClaimsPrincipal user, StoreDb db) => {
    if (!admin(user)) return Results.Unauthorized(); if (!IsKnown(collection)) return Results.NotFound();
    var item = await db.Items.FirstOrDefaultAsync(x => x.Collection == collection && x.Id == id); if (item is null) return Results.NotFound();
    var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(item.Json) ?? new();
    foreach (var p in patch.EnumerateObject()) dict[p.Name] = p.Value.Clone(); item.Json = JsonSerializer.Serialize(dict); await db.SaveChangesAsync(); return Results.Ok(new { id });
});
app.MapDelete("/api/data/{collection}/{id}", async Task<IResult> (string collection, string id, ClaimsPrincipal user, StoreDb db) => {
    if (!admin(user)) return Results.Unauthorized(); if (!IsKnown(collection)) return Results.NotFound();
    var item = await db.Items.FirstOrDefaultAsync(x => x.Collection == collection && x.Id == id); if (item is null) return Results.NoContent(); db.Items.Remove(item); await db.SaveChangesAsync(); return Results.NoContent();
});

// Solar Expert chatbot: Gemini API
app.MapPost("/api/chat", async Task<IResult> (ChatRequest req, IConfiguration cfg, IHttpClientFactory factory, StoreDb db) => {
    var key = cfg["Gemini:ApiKey"];
    if (string.IsNullOrWhiteSpace(key)) return Results.Ok(new { reply = "Solar Expert is not configured yet. Please try again later." });

    var messages = (req.Messages ?? new List<ChatMessage>())
        .TakeLast(6)
        .Where(m => (m.Role == "user" || m.Role == "assistant") && !string.IsNullOrWhiteSpace(m.Content))
        .Select(m => new {
            role = m.Role == "assistant" ? "model" : "user",
            parts = new[] { new { text = m.Content.Length > 1500 ? m.Content[..1500] : m.Content } }
        }).ToList();
    if (messages.Count == 0) return Results.BadRequest(new { error = "Message required." });

    // Gemini conversation history must start with a user message.
    while (messages.Count > 0 && messages[0].role == "model") messages.RemoveAt(0);
    if (messages.Count == 0 || messages[^1].role != "user") return Results.BadRequest(new { error = "A user message is required." });

    var products = await db.Items.Where(x => x.Collection == "products").Take(60).ToListAsync();
    var catalog = string.Join("\n", products.Select(x => {
        using var d = JsonDocument.Parse(x.Json); var r = d.RootElement;
        return $"{Get(r, "name")} ({Get(r, "cat")}) ₹{Get(r, "price")}: {Get(r, "desc")}";
    }));
    var body = new {
        systemInstruction = new { parts = new[] { new { text = "You are Solar Expert for an Indian solar store. Answer briefly in the customer's language and recommend products from this catalog.\n" + catalog } } },
        contents = messages,
        generationConfig = new { maxOutputTokens = 600 }
    };

    try {
        using var request = new HttpRequestMessage(HttpMethod.Post,
            "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent");
        request.Headers.Add("x-goog-api-key", key);
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        var response = await factory.CreateClient().SendAsync(request);
        var responseText = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) {
            Console.Error.WriteLine($"Gemini API returned HTTP {(int)response.StatusCode}: {responseText}");
            return Results.Ok(new { reply = "Sorry, I could not answer right now. Please try again." });
        }
        using var doc = JsonDocument.Parse(responseText);
        if (!doc.RootElement.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
            return Results.Ok(new { reply = "I couldn't generate a response. Please try another question." });
        var candidate = candidates[0];
        if (!candidate.TryGetProperty("content", out var content) || !content.TryGetProperty("parts", out var parts))
            return Results.Ok(new { reply = "I couldn't generate a response. Please try again." });
        var reply = string.Join("", parts.EnumerateArray().Where(p => p.TryGetProperty("text", out _)).Select(p => p.GetProperty("text").GetString() ?? ""));
        return Results.Ok(new { reply = string.IsNullOrWhiteSpace(reply) ? "Please try asking your question another way." : reply });
    }
    catch (Exception ex) {
        Console.Error.WriteLine($"Gemini request failed: {ex.Message}");
        return Results.Ok(new { reply = "Sorry, I could not answer right now. Please try again." });
    }
});

app.MapControllers();
app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");
app.Run();

static bool IsKnown(string c) => c is "products" or "gallery" or "orders" or "enquiries" or "feedback";
static string Get(JsonElement e, string name) => e.TryGetProperty(name, out var v) ? v.ToString() : "";

public sealed class StoreDb(DbContextOptions<StoreDb> options) : DbContext(options) {
    public DbSet<StoreItem> Items => Set<StoreItem>();
    protected override void OnModelCreating(ModelBuilder b) {
        b.Entity<StoreItem>().HasKey(x => new { x.Collection, x.Id });
        b.Entity<StoreItem>().Property(x => x.Collection).HasMaxLength(32);
        b.Entity<StoreItem>().Property(x => x.Id).HasMaxLength(100);
    }
}
public sealed class StoreItem { public string Collection { get; set; } = ""; public string Id { get; set; } = ""; public string Json { get; set; } = "{}"; }
public sealed record LoginRequest(string? Username, string? Password);
public sealed record ChatMessage(string Role, string Content);
public sealed record ChatRequest(List<ChatMessage>? Messages);

